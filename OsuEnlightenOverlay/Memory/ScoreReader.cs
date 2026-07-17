using System;
using System.Collections.Generic;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// 플레이 중 스코어 상태 읽기 — Ruleset → gameplayBase → scoreBase 체인.
    ///
    /// Play 모드에서만 유효하다. 다른 씬에서는 Ruleset 객체가 없거나 낡은 값을
    /// 가리키므로 OsuMemoryReader가 Clear()로 ScoreLive를 내린다.
    ///
    /// 카운트가 65000 이상이면 체인이 엉뚱한 곳을 가리키는 것으로 보고 통째로
    /// 버린다 — 부분적으로 갱신된 값을 HUD에 내보내지 않기 위함.
    /// </summary>
    internal class ScoreReader
    {
        readonly ProcessMemory pm;

        IntPtr rulesetSlot = IntPtr.Zero;

        public bool ScoreLive { get; private set; }
        public int TotalScore { get; private set; }
        public int MaxCombo { get; private set; }
        public int CurrentCombo { get; private set; }
        public ushort Count300 { get; private set; }
        public ushort Count100 { get; private set; }
        public ushort Count50 { get; private set; }
        public ushort CountMiss { get; private set; }
        public double Accuracy { get; private set; }
        public List<int> HitErrors { get; private set; } = new List<int>();

        // ── Refresh 배치 읽기 범위 ──
        // MaxCombo(0x68)부터 CurrentCombo(0x94, ushort)까지를 한 번에 덮음.
        const int ScoreBatchBase = Offsets.Score_MaxCombo;
        const int ScoreBatchSize = Offsets.Score_CurrentCombo + sizeof(ushort) - ScoreBatchBase;

        // 재사용 버퍼 — 매 프레임 new 할당 방지 (GC 스톨 방지)
        byte[] reusedScoreBatch = new byte[ScoreBatchSize];
        byte[] buf120; // HitErrors 배치 읽기용 (30 int = 120바이트)

        /// <summary>Ruleset static slot을 찾았는지 — Refresh 호출 전 확인.</summary>
        public bool HasSlot { get { return rulesetSlot != IntPtr.Zero; } }

        public ScoreReader(ProcessMemory pm)
        {
            this.pm = pm;
        }

        /// <summary>
        /// 기동 시 배치 스캔(D1) 결과를 받아 slot 해석 — 전체 메모리를 다시 읽지 않는다.
        /// </summary>
        public void ApplyScan(AobScanRequest req)
        {
            rulesetSlot = AobScanner.ResolveSlot(pm, Signatures.Ruleset, req);
        }

        /// <summary>Play 모드가 아닐 때 — 낡은 스코어가 HUD에 남지 않도록.</summary>
        public void Clear()
        {
            ScoreLive = false;
        }

        /// <summary>
        /// G3 재접속 — PID 종속 상태 리셋. rulesetSlot은 ApplyScan이 덮어쓰지만(assign),
        /// 스캔이 ApplyScan 이전에 실패할 수 있으므로 먼저 0으로 둔다.
        /// </summary>
        public void ResetForReconnect()
        {
            rulesetSlot = IntPtr.Zero;
            ScoreLive = false;
        }

        public void Refresh()
        {
            ScoreLive = false;

            IntPtr rulesetObj;
            if (!pm.ReadPointer(rulesetSlot, out rulesetObj) || rulesetObj == IntPtr.Zero)
                return;

            IntPtr gameplayBase;
            if (!pm.ReadPointer(rulesetObj + Offsets.Ruleset_GameplayBase, out gameplayBase) || gameplayBase == IntPtr.Zero)
                return;

            IntPtr scoreBase;
            if (!pm.ReadPointer(gameplayBase + Offsets.GameplayBase_ScoreBase, out scoreBase) || scoreBase == IntPtr.Zero)
                return;

            // 배치 읽기 — MaxCombo(0x68) ~ CurrentCombo(0x94)를 한 번의 ReadProcessMemory로.
            // 버퍼 내 위치는 Offsets 상수에서 컴파일 타임에 산출 (진실의 원천 = Offsets).
            byte[] scoreBatch = reusedScoreBatch;
            if (!pm.ReadBytes(scoreBase + ScoreBatchBase, scoreBatch, ScoreBatchSize)) return;

            int maxCombo = ProcessMemory.GetInt32(scoreBatch, Offsets.Score_MaxCombo - ScoreBatchBase);
            int totalScore = ProcessMemory.GetInt32(scoreBatch, Offsets.Score_TotalScore - ScoreBatchBase);
            ushort c100 = ProcessMemory.GetUInt16(scoreBatch, Offsets.Score_Count100 - ScoreBatchBase);
            ushort c300 = ProcessMemory.GetUInt16(scoreBatch, Offsets.Score_Count300 - ScoreBatchBase);
            ushort c50 = ProcessMemory.GetUInt16(scoreBatch, Offsets.Score_Count50 - ScoreBatchBase);
            ushort cMiss = ProcessMemory.GetUInt16(scoreBatch, Offsets.Score_CountMiss - ScoreBatchBase);
            ushort curCombo = ProcessMemory.GetUInt16(scoreBatch, Offsets.Score_CurrentCombo - ScoreBatchBase);

            if (c300 >= 65000 || c100 >= 65000 || c50 >= 65000 || cMiss >= 65000)
                return;

            TotalScore = totalScore;
            MaxCombo = maxCombo;
            CurrentCombo = curCombo;
            Count300 = c300;
            Count100 = c100;
            Count50 = c50;
            CountMiss = cMiss;
            ScoreLive = true;

            // Accuracy 읽기 — gameplayBase + 0x48 → accuracyObj + 0x0C → double
            RefreshAccuracy(gameplayBase);

            // Hit Errors 읽기 — scoreBase + 0x38 → List<int>
            RefreshHitErrors(scoreBase);
        }

        /// <summary>
        /// Accuracy 읽기 — gameplayBase + 0x48 → accuracyObj + 0x0C → double.
        /// NEWNEWOVERLAY osu_reader.cpp RefreshScore 포팅.
        /// </summary>
        void RefreshAccuracy(IntPtr gameplayBase)
        {
            try
            {
                IntPtr accObj;
                if (!pm.ReadPointer(gameplayBase + Offsets.GameplayBase_Accuracy, out accObj) || accObj == IntPtr.Zero)
                    return;

                double acc;
                if (pm.ReadDouble(accObj + Offsets.Accuracy_Value, out acc))
                    Accuracy = acc;
            }
            catch { }
        }

        /// <summary>
        /// Hit Errors 읽기 — scoreBase + 0x38 → List<int> → items 배열 순회.
        /// 최근 30개만 유지, ±10000ms 범위 외 값 거부.
        /// NEWNEWOVERLAY osu_reader.cpp 포팅.
        /// </summary>
        void RefreshHitErrors(IntPtr scoreBase)
        {
            HitErrors.Clear();
            try
            {
                IntPtr listObj;
                if (!pm.ReadPointer(scoreBase + Offsets.Score_HitErrors, out listObj) || listObj == IntPtr.Zero)
                    return;

                IntPtr itemsArr;
                if (!pm.ReadPointer(listObj + Offsets.List_Items, out itemsArr) || itemsArr == IntPtr.Zero)
                    return;

                int size;
                if (!pm.ReadInt32(listObj + Offsets.List_Size, out size) || size <= 0)
                    return;

                // 최근 30개만
                int start = Math.Max(0, size - 30);
                int count = size - start;

                // 배치 읽기 — 30개 int = 120바이트를 한 번의 ReadProcessMemory로 읽기
                // (개별 ReadInt32 30번 → 1번으로 감소, syscall 오버헤드 제거)
                if (count > 0 && itemsArr != IntPtr.Zero)
                {
                    int byteCount = count * 4;
                    byte[] buf = buf120 ?? (buf120 = new byte[120]);
                    if (byteCount > buf.Length) byteCount = buf.Length;
                    if (pm.ReadBytes(itemsArr + Offsets.Array_Data + start * 4, buf, byteCount))
                    {
                        int numInts = byteCount / 4;
                        for (int i = 0; i < numInts; i++)
                        {
                            int error = ProcessMemory.GetInt32(buf, i * 4);
                            if (Math.Abs(error) <= 10000)
                                HitErrors.Add(error);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
