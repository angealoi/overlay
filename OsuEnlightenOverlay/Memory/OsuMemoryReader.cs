using System;
using System.Collections.Generic;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// osu! stable 메모리 리더 — SIG.MD 기반.
    /// AOB 스캔으로 static field slot을 해석하고, 매 프레임 live 값을 읽음.
    /// </summary>
    public class OsuMemoryReader : IDisposable
    {
        ProcessMemory pm = new ProcessMemory();

        // 해상도/레터박싱 읽기 — WindowManager + ConfigManager Dictionary
        ResolutionReader resolution;

        // 스코어 읽기 — Ruleset → gameplayBase → scoreBase (Play 모드 전용)
        ScoreReader score;

        IntPtr timeSlot = IntPtr.Zero;
        IntPtr modeSlot = IntPtr.Zero;
        IntPtr modsSlot = IntPtr.Zero;
        IntPtr beatmapStaticAddr = IntPtr.Zero;
        IntPtr playModeSlot = IntPtr.Zero;
        List<IntPtr> cursorSlots = new List<IntPtr>();
        HashSet<long> cursorWriteSlots = new HashSet<long>(); // 연속 3개 그룹 = 커서 쓰기 함수 출력
        IntPtr cursorPositionSlot = IntPtr.Zero; // CursorPosition static slot (autopilot 커서)
        bool cursorSlotIsProvisional = false;    // 폴백으로 고른 상태 — 제대로 식별되면 승격

        public OsuMemoryReader()
        {
            resolution = new ResolutionReader(pm);
            score = new ScoreReader(pm);
        }

        public int TimeMs { get; private set; }
        public int AudioState { get; private set; }
        public int Mode { get; private set; }
        public uint MenuMods { get; private set; }
        public float CursorX { get; private set; }
        public float CursorY { get; private set; }
        public float BeatmapAR { get; private set; }
        public float BeatmapCS { get; private set; }
        public float BeatmapHP { get; private set; }
        public float BeatmapOD { get; private set; }
        public string BeatmapFolder { get; private set; }
        public string BeatmapOsuFilename { get; private set; }
        public string BeatmapDifficultyName { get; private set; }
        public int PlayMode { get; private set; }

        // ── 스코어 — ScoreReader 위임 ──
        public bool ScoreLive { get { return score.ScoreLive; } }
        public int TotalScore { get { return score.TotalScore; } }
        public int MaxCombo { get { return score.MaxCombo; } }
        public int CurrentCombo { get { return score.CurrentCombo; } }
        public ushort Count300 { get { return score.Count300; } }
        public ushort Count100 { get { return score.Count100; } }
        public ushort Count50 { get { return score.Count50; } }
        public ushort CountMiss { get { return score.CountMiss; } }

        // HUD용 추가 상태
        public double Accuracy { get { return score.Accuracy; } }
        public List<int> HitErrors { get { return score.HitErrors; } }

        // ── 레터박싱/해상도 — ResolutionReader 위임 ──
        /// <summary>실제 렌더링 너비 (WindowManager.Width)</summary>
        public int WindowWidth { get { return resolution.WindowWidth; } }
        /// <summary>실제 렌더링 높이 (WindowManager.Height, MenuHeight 제외 전)</summary>
        public int WindowHeight { get { return resolution.WindowHeight; } }
        /// <summary>레터박싱 여부 (osu! UI: "Render at native resolution")</summary>
        public bool IsLetterboxing { get { return resolution.IsLetterboxing; } }
        /// <summary>레터박스 수평 위치 (-100~100, 0=중앙)</summary>
        public int LetterboxPositionX { get { return resolution.LetterboxPositionX; } }
        /// <summary>레터박스 수직 위치 (-100~100, 0=중앙)</summary>
        public int LetterboxPositionY { get { return resolution.LetterboxPositionY; } }
        /// <summary>모니터 실제 네이티브 해상도 너비 (Win32 API)</summary>
        public int DesktopWidth { get { return resolution.DesktopWidth; } }
        /// <summary>모니터 실제 네이티브 해상도 높이 (Win32 API)</summary>
        public int DesktopHeight { get { return resolution.DesktopHeight; } }

        // 재사용 버퍼 — 매 프레임 new 할당 방지 (GC 스톨 방지)
        List<HitObjectJudgement> reusedJudgements = new List<HitObjectJudgement>(64);
        byte[] reusedHoBatch = new byte[0x118]; // hoPtr+0x10 ~ hoPtr+0x128 (IsTracking 0x120 포함)

        public bool IsOpen { get { return pm.IsOpen; } }
        public int ProcessId { get { return pm.ProcessId; } }

        /// <summary>
        /// 현재 플레이 중인 맵의 .osu 파일 전체 경로.
        /// osu! 설치 폴더/Songs/{BeatmapFolder}/{BeatmapOsuFilename}
        /// </summary>
        public string CurrentBeatmapPath
        {
            get
            {
                if (string.IsNullOrEmpty(BeatmapFolder) || string.IsNullOrEmpty(BeatmapOsuFilename))
                    return null;

                // osu! 설치 경로 — 프로세스 실행 파일 경로에서 추출
                string osuDir = OsuInstallDir;
                if (osuDir == null) return null;

                string path = System.IO.Path.Combine(osuDir, "Songs", BeatmapFolder, BeatmapOsuFilename);
                if (System.IO.File.Exists(path))
                    return path;

                return null;
            }
        }

        string cachedOsuInstallDir;

        /// <summary>
        /// osu! 설치 디렉토리 — 캐싱됨 (최초 1회만 조회).
        /// </summary>
        public string OsuInstallDir
        {
            get
            {
                if (cachedOsuInstallDir != null)
                    return cachedOsuInstallDir;
                try
                {
                    var procs = System.Diagnostics.Process.GetProcessById(ProcessId);
                    string exePath = procs.MainModule.FileName;
                    procs.Dispose();
                    cachedOsuInstallDir = System.IO.Path.GetDirectoryName(exePath);
                    return cachedOsuInstallDir;
                }
                catch
                {
                    // 폴백: 기본 경로
                    if (System.IO.Directory.Exists(@"C:\osu!"))
                        return @"C:\osu!";
                    return null;
                }
            }
        }

        public bool Initialize()
        {
            if (!pm.OpenOsu())
                return false;
            return ScanStaticSlots();
        }

        /// <summary>
        /// 기동 시 static slot 일괄 스캔.
        /// 예전에는 시그니처마다 전체 메모리를 다시 읽어 **전체 패스가 9회** 돌았다 (D1).
        /// 이제 모든 패턴을 한 배치로 넘겨 **1회 패스**로 끝낸다.
        /// </summary>
        bool ScanStaticSlots()
        {
            var time = new AobScanRequest(Signatures.AudioEngineTime.Pattern, false);
            var mode = new AobScanRequest(Signatures.GameBaseMode.Pattern, false);
            var mods = new AobScanRequest(Signatures.MenuMods.Pattern, false);
            // CurrentBeatmap과 PlayMode는 패턴이 같고 OperandSkip만 다르다 — 요청 하나로 둘 다 해결
            var beatmap = new AobScanRequest(Signatures.CurrentBeatmap.Pattern, false);
            var ruleset = new AobScanRequest(Signatures.Ruleset.Pattern, false);
            var player = new AobScanRequest(Signatures.PlayerInstance.Pattern, false);
            var config = new AobScanRequest(Signatures.ConfigDictionary.Pattern, false);
            // 커서는 JIT가 여러 코드 사이트에 같은 코드를 방출하므로 전체 매치가 필요
            var cursor = new AobScanRequest(Signatures.CursorXY.Pattern, true);

            AobScanner.ScanBatch(pm, new[] { time, mode, mods, beatmap, ruleset, player, config, cursor });

            timeSlot = AobScanner.ResolveSlot(pm, Signatures.AudioEngineTime, time);
            if (timeSlot == IntPtr.Zero)
                return false;

            modeSlot = AobScanner.ResolveSlot(pm, Signatures.GameBaseMode, mode);
            if (modeSlot == IntPtr.Zero)
                return false;

            modsSlot = AobScanner.ResolveSlot(pm, Signatures.MenuMods, mods);
            if (modsSlot == IntPtr.Zero)
                return false;

            beatmapStaticAddr = AobScanner.ResolveSlot(pm, Signatures.CurrentBeatmap, beatmap);
            if (beatmapStaticAddr == IntPtr.Zero)
                return false;

            playModeSlot = AobScanner.ResolveSlot(pm, Signatures.PlayMode, beatmap);
            score.ApplyScan(ruleset);
            ApplyCursorScan(cursor);
            if (player.First != IntPtr.Zero)
                pm.ReadPointer(player.First + Signatures.PlayerInstance.OperandSkip, out playerInstanceSlot);
            resolution.ApplyScan(config);

            return timeSlot != IntPtr.Zero;
        }

        /// <summary>
        /// 커서 관련 static slot AOB 스캔 — 비용이 크므로 기동 시 1회만.
        /// 실제 CursorPosition slot 선택은 IdentifyCursorPositionSlot이 담당하며,
        /// 성공할 때까지 매 프레임 재시도한다 (아래 주석 참고).
        /// </summary>
        void ScanCursorSlots()
        {
            var req = new AobScanRequest(Signatures.CursorXY.Pattern, true);
            AobScanner.ScanBatch(pm, new[] { req });
            ApplyCursorScan(req);
        }

        /// <summary>
        /// 커서 스캔 결과 적용 — 기동 시 배치 스캔과 재스캔이 공유한다.
        /// </summary>
        void ApplyCursorScan(AobScanRequest req)
        {
            List<IntPtr> matches = req.Results;

            foreach (IntPtr match in matches)
            {
                IntPtr operandAddr = match + Signatures.CursorXY.OperandSkip;
                IntPtr slot;
                if (pm.ReadPointer(operandAddr, out slot))
                    cursorSlots.Add(slot);
            }

            // 커서 쓰기 함수의 출력 slot들은 4바이트 간격으로 뭉쳐 있다(slot, slot+4, slot+8).
            // CursorPosition은 InputManager의 value type static Vector2로 다른 packed static
            // 영역에 홀로 떨어져 있다. 따라서 "이웃이 있는 slot = 쓰기 함수 출력"으로 본다.
            //
            // 이전에는 삼중쌍(s, s+4, s+8)이 모두 잡혀야 그룹으로 인정했는데, AOB 스캔이
            // 7개 중 6개만 찾으면(JIT 타이밍으로 한 코드 사이트가 아직 컴파일 전) 삼중쌍이
            // 깨져 그룹 탐지가 통째로 실패하고, 쓰기 slot이 후보로 새어 들어와 먼저 선택됐다.
            // 그러면 오버레이 커서가 인게임 커서 대신 물리 마우스를 따라간다
            // (쓰기 slot은 정수 좌표, CursorPosition은 보간된 소수 좌표).
            //
            // ±4/±8 이내 이웃 유무로 판정하면 삼중쌍이 깨져도 안전하다.
            // 실측 2개 세션(6개/7개 매치) 모두에서 정답 slot만 후보로 남는 것을 확인.
            var slotSet = new HashSet<long>();
            foreach (IntPtr s in cursorSlots)
                slotSet.Add(s.ToInt64());

            cursorWriteSlots.Clear();
            foreach (long s in slotSet)
            {
                if (slotSet.Contains(s - 8) || slotSet.Contains(s - 4) ||
                    slotSet.Contains(s + 4) || slotSet.Contains(s + 8))
                    cursorWriteSlots.Add(s);
            }

            IdentifyCursorPositionSlot();
        }

        // 커서 슬롯 재스캔 제한 — AOB 스캔은 전체 메모리를 훑어 수백 ms가 든다.
        // 렌더 스레드에서 도므로 무제한 재시도하면 계속 끊긴다.
        const long CursorRescanIntervalTicks = 2 * TimeSpan.TicksPerSecond;
        const int CursorRescanMaxAttempts = 10;
        long cursorRescanLastTicks;
        int cursorRescanAttempts;

        /// <summary>
        /// 커서 AOB 재스캔 — 슬롯 목록 자체에 CursorPosition이 없을 때만.
        /// 시간(2초)·횟수(10회) 제한. 확정되면 더 이상 호출되지 않는다.
        /// </summary>
        void TryRescanCursorSlots()
        {
            if (cursorRescanAttempts >= CursorRescanMaxAttempts) return;

            long now = DateTime.UtcNow.Ticks;
            if (now - cursorRescanLastTicks < CursorRescanIntervalTicks) return;
            cursorRescanLastTicks = now;
            cursorRescanAttempts++;

            int before = cursorSlots.Count;
            // 이전 스캔 결과를 완전히 버린다 — 폴백으로 잡아둔 주소가 새 목록에
            // 없을 수 있으므로 남겨두면 안 된다.
            cursorSlots.Clear();
            cursorWriteSlots.Clear();
            cursorPositionSlot = IntPtr.Zero;
            cursorSlotIsProvisional = false;
            ScanCursorSlots(); // 내부에서 IdentifyCursorPositionSlot까지 수행

            Console.WriteLine("[Cursor] 재스캔 " + cursorRescanAttempts + "/" + CursorRescanMaxAttempts
                + ": slots " + before + " -> " + cursorSlots.Count
                + (cursorSlotIsProvisional ? " (아직 미확정)" : " (확정)"));
        }

        /// <summary>
        /// cursorSlots 중 CursorPosition을 식별 — 값이 유효한 첫 후보를 선택.
        ///
        /// 반드시 성공할 때까지 재시도해야 한다. 기동 시점에 osu!가 메뉴에 있으면
        /// CursorPosition이 아직 갱신되지 않아 (0,0)으로 읽히고, TryReadCursor가 이를
        /// 거부해 정답 슬롯이 후보에서 탈락한다. 1회성 식별이면 그대로 폴백
        /// (cursorSlots[1])이 영구 고정되는데, 이 인덱스는 JIT 코드 배치 순서에
        /// 의존하므로 osu! 세션마다 다른 슬롯을 가리킬 수 있다 — 잘못 걸리면
        /// 커서가 (0,0)에 영구히 박힌다.
        /// </summary>
        void IdentifyCursorPositionSlot()
        {
            foreach (IntPtr slot in cursorSlots)
            {
                if (cursorWriteSlots.Contains(slot.ToInt64()))
                    continue; // 커서 쓰기 함수 출력 — skip

                IntPtr source;
                if (!pm.ReadPointer(slot, out source) || source == IntPtr.Zero)
                    continue;

                float x, y;
                if (TryReadCursor(source, out x, out y))
                {
                    cursorPositionSlot = slot;
                    cursorSlotIsProvisional = false; // 확정
                    return;
                }
            }

            // 폴백: 아직 값이 유효하지 않아 식별에 실패한 경우, 쓰기 그룹을 제외한
            // 첫 후보를 임시로 쓴다. provisional로 표시해 유효 값이 들어오는 즉시 승격된다.
            //
            // 예전에는 인덱스로 slot[1]을 집었는데, 그 인덱스는 AOB 스캔이 훑는 JIT 코드
            // 배치 순서에 의존한다 — 실측에서 slot[1]이 쓰기 슬롯인 세션이 있었다.
            if (cursorPositionSlot == IntPtr.Zero)
            {
                foreach (IntPtr slot in cursorSlots)
                {
                    if (cursorWriteSlots.Contains(slot.ToInt64()))
                        continue;
                    cursorPositionSlot = slot;
                    cursorSlotIsProvisional = true;
                    break;
                }
            }
        }

        public void RefreshLiveValues()
        {
            if (!IsOpen) return;

            int timeVal;
            if (pm.ReadInt32(timeSlot, out timeVal))
                TimeMs = timeVal;

            int audioStateVal;
            if (pm.ReadInt32(timeSlot + Offsets.AudioState_FromTimeSlot, out audioStateVal))
                AudioState = audioStateVal;

            int modeVal;
            if (pm.ReadInt32(modeSlot, out modeVal))
                Mode = modeVal;

            uint modsVal;
            if (pm.ReadUInt32(modsSlot, out modsVal))
                MenuMods = modsVal;

            // Mode: 0=Menu, 1=Edit, 2=Play, 3=Exit, 4=SelectEdit, 5=SelectPlay, 7=Rank
            // Play(2)와 SelectPlay(5)에서만 커서/비트맵/해상도 스캔
            bool needScan = Mode == Offsets.Mode_Play || Mode == Offsets.Mode_SelectPlay;

            if (needScan)
            {
                RefreshCursor();
                RefreshBeatmap();
                resolution.Refresh();
            }

            if (playModeSlot != IntPtr.Zero)
            {
                int playModeVal;
                if (pm.ReadInt32(playModeSlot, out playModeVal))
                    PlayMode = playModeVal;
            }

            if (Mode == Offsets.Mode_Play && score.HasSlot)
                score.Refresh();
            else
                score.Clear();
        }

        void RefreshCursor()
        {
            // 슬롯이 미식별이거나 폴백(추정)이면 재식별 시도.
            // 기동 시 osu!가 메뉴에 있으면 CursorPosition이 (0,0)이라 식별이 실패하는데,
            // Play에 진입해 커서가 살아나면 여기서 정답 슬롯으로 확정/승격된다.
            if (cursorPositionSlot == IntPtr.Zero || cursorSlotIsProvisional)
            {
                // 1단계: 이미 찾아둔 슬롯들로 재식별 (싸다).
                IdentifyCursorPositionSlot();

                // 2단계: 그래도 확정 못 하면 CursorPosition 코드 사이트가 스캔 당시
                // 아직 JIT되지 않아 슬롯 목록에 아예 없는 경우다 — AOB 재스캔.
                //
                // 실측: 같은 osu!라도 세션에 따라 매치가 5~7개로 다르고, 5개인 세션엔
                // 정답 슬롯(0x...5010)이 통째로 빠져 있었다. 스캔이 기동 시 1회뿐이라
                // 그 세션은 영영 커서를 못 읽고 (0,0)에 박혔다.
                //
                // AOB 스캔은 전체 메모리를 훑어 비싸므로 시간·횟수를 제한한다.
                if (cursorPositionSlot == IntPtr.Zero || cursorSlotIsProvisional)
                    TryRescanCursorSlots();
            }

            // CursorPosition static slot만 사용 (autopilot/auto mod 인게임 커서)
            if (cursorPositionSlot == IntPtr.Zero)
            {
                CursorX = 0;
                CursorY = 0;
                return;
            }

            IntPtr source;
            if (!pm.ReadPointer(cursorPositionSlot, out source) || source == IntPtr.Zero)
            {
                CursorX = 0;
                CursorY = 0;
                return;
            }

            float x, y;
            if (TryReadCursor(source, out x, out y))
            {
                CursorX = x;
                CursorY = y;
            }
        }

        /// <summary>float이 정규(normal) 값인지 — 0은 허용, 비정규(subnormal)는 거부.</summary>
        static bool IsNormalOrZero(float v)
        {
            if (v == 0) return true;
            return Math.Abs(v) >= MinNormalFloat;
        }

        // float의 최소 정규값. 이보다 작은 0이 아닌 값은 비정규(subnormal)다.
        const float MinNormalFloat = 1.17549435E-38f;

        bool TryReadCursor(IntPtr source, out float x, out float y)
        {
            x = 0; y = 0;
            if (!pm.ReadFloat(source + Offsets.Cursor_X, out x)) return false;
            if (!pm.ReadFloat(source + Offsets.Cursor_Y, out y)) return false;

            if (float.IsNaN(x) || float.IsNaN(y)) return false;
            if (float.IsInfinity(x) || float.IsInfinity(y)) return false;
            if (Math.Abs(x) > 32768 || Math.Abs(y) > 32768) return false;
            if (x == 0 && y == 0) return false;
            if (x == 1.0f && y == 1.0f) return false;

            // 비정규(subnormal) 거부 — 실제 좌표는 항상 정규 float이다.
            //
            // 커서 쓰기 함수의 슬롯은 좌표를 int로 담고 있어서, float으로 읽으면
            // 작은 정수의 비트 패턴이 그대로 비정규값으로 보인다:
            //   int 890 -> 0x0000037A -> float 1.247156E-42
            //   int 655 -> 0x0000028F -> float 9.178505E-43
            // 이 값들은 0이 아니므로 위의 (x==0 && y==0) 검사를 통과해버렸고,
            // 그 결과 엉뚱한 슬롯이 CursorPosition으로 확정되어 커서가 (0,0)에
            // 박혔다. 정상 좌표(431 -> 0x43D78000)는 항상 정규값이므로 이 검사로
            // 두 경우를 확실히 가를 수 있다.
            if (!IsNormalOrZero(x) || !IsNormalOrZero(y)) return false;

            return true;
        }

        IntPtr lastBeatmapPtr = IntPtr.Zero;
        IntPtr lastFolderPtr = IntPtr.Zero;
        IntPtr lastFilenamePtr = IntPtr.Zero;
        IntPtr lastDiffNamePtr = IntPtr.Zero;

        void RefreshBeatmap()
        {
            if (beatmapStaticAddr == IntPtr.Zero) return;

            IntPtr beatmapPtr;
            if (!pm.ReadPointer(beatmapStaticAddr, out beatmapPtr) || beatmapPtr == IntPtr.Zero)
            {
                lastBeatmapPtr = IntPtr.Zero;
                return;
            }

            // 비트맵 포인터가 같으면 AR/CS/HP/OD 및 문자열 재읽기 스킵
            // (고정값 — 곡 선택 시에만 바뀜)
            if (beatmapPtr == lastBeatmapPtr)
                return;
            lastBeatmapPtr = beatmapPtr;

            // 맵이 바뀌었을 때만 AR/CS/HP/OD 읽기
            float ar, cs, hp, od;
            pm.ReadFloat(beatmapPtr + Offsets.Beatmap_AR, out ar);
            pm.ReadFloat(beatmapPtr + Offsets.Beatmap_CS, out cs);
            pm.ReadFloat(beatmapPtr + Offsets.Beatmap_HP, out hp);
            pm.ReadFloat(beatmapPtr + Offsets.Beatmap_OD, out od);
            BeatmapAR = ar;
            BeatmapCS = cs;
            BeatmapHP = hp;
            BeatmapOD = od;

            IntPtr folderPtr;
            if (pm.ReadPointer(beatmapPtr + Offsets.Beatmap_Folder, out folderPtr) && folderPtr != lastFolderPtr)
            {
                BeatmapFolder = pm.ReadSharpString(folderPtr);
                lastFolderPtr = folderPtr;
            }

            IntPtr filenamePtr;
            if (pm.ReadPointer(beatmapPtr + Offsets.Beatmap_OsuFilename, out filenamePtr) && filenamePtr != lastFilenamePtr)
            {
                BeatmapOsuFilename = pm.ReadSharpString(filenamePtr);
                lastFilenamePtr = filenamePtr;
            }

            IntPtr diffNamePtr;
            if (pm.ReadPointer(beatmapPtr + Offsets.Beatmap_DifficultyName, out diffNamePtr) && diffNamePtr != lastDiffNamePtr)
            {
                BeatmapDifficultyName = pm.ReadSharpString(diffNamePtr);
                lastDiffNamePtr = diffNamePtr;
            }
        }

        public bool IsHD { get { return (MenuMods & Offsets.Mod_HD) != 0; } }
        public bool IsHR { get { return (MenuMods & Offsets.Mod_HR) != 0; } }
        public bool IsFL { get { return (MenuMods & Offsets.Mod_FL) != 0; } }
        public bool IsDT { get { return (MenuMods & Offsets.Mod_DT) != 0; } }
        public bool IsHT { get { return (MenuMods & Offsets.Mod_HT) != 0; } }
        public bool IsNC { get { return (MenuMods & Offsets.Mod_NC) != 0; } }
        public bool IsEZ { get { return (MenuMods & Offsets.Mod_EZ) != 0; } }

        // ── HitObject 리스트 읽기 ──
        // Ruleset → HOM → hitObjects List → items 배열 → 각 HitObject

        /// <summary>
        /// HitObject 판정 데이터 — 메모리에서 읽은 값.
        /// </summary>
        public struct HitObjectJudgement
        {
            public int StartTime;
            public int EndTime;
            public int Type;
            public int ScoreValue;  // 300/100/50/0
            public byte IsHit;      // 1=판정됨
            public int HitValue;    // IncreaseScoreType
            public float FloatRotationCount; // 스피너 회전 (float, +0x10C)
            public int ScoringRotationCount;  // 스피너 회전 (int, +0xF4)
            public int RotationRequirement;    // 스피너 요구 회전수 (int, +0xF8)
            public int SpinningState;         // 스피너 상태 (0=NotStarted, 1=Started, 2=Passed)
            public byte IsTracking;           // 슬라이더 tracking 중 (0=아님, 1=tracking)
            public byte StartIsHit;          // 슬라이더 시작원 IsHit (SliderStartCircle+0x84)
        }

        // ── HOM 스캔 (Player.Instance + .osu 파일 검증 방식) ──
        // Player.Instance static → +오프셋 → HOM → +오프셋 → hitObjects List
        // 오프셋은 첫 스캔 시 자동 감지, 이후 고정 오프셋으로 매 프레임 빠르게 읽기

        /// <summary>
        /// 32-bit CLR 힙 포인터처럼 보이는지 검사.
        /// </summary>
        bool LooksLikeHeapPtr(uint v)
        {
            if (v == 0) return false;
            if (v == 0xFFFFFFFF) return false;
            if ((v & 3) != 0) return false;
            return v >= 0x01000000 && v < 0x80000000;
        }

        IntPtr playerInstanceSlot = IntPtr.Zero;

        // 발견된 고정 오프셋 (첫 스캔 시 자동 감지)
        int foundPlayerHomOff = -1;
        int foundHomListOff = -1;

        // HitObject StartTime/EndTime 배열 캐시 (맵 로드 시 1회). StartTime/EndTime은 GC-불변이므로 안전.
        // 포인터(cachedHoPtrs)는 GC compaction으로 이동하므로 캐싱하지 않고 매 프레임 재읽기.
        // EndTime도 캐싱 — 스피너처럼 긴 지속시간 객체는 StartTime이 과거여도 진행 중일 수 있어
        // 시간 창이 StartTime이 아닌 EndTime 기준으로 판단해야 함.
        int[] cachedHoStartTimes = null;
        int[] cachedHoEndTimes = null;
        int cachedHoCount = 0;
        int cachedMaxDuration = 0; // 캐싱된 객체 중 최대 지속시간 (EndTime - StartTime)
        // 맵 로드 시 오프셋 + StartTime 캐싱 완료 여부 (포인터는 매 프레임 읽음)
        bool hoCacheReady = false;
        IntPtr lastBeatmapObj = IntPtr.Zero;

        // .osu 파일 파싱 결과 (검증용)
        class OsuHitObject
        {
            public int StartTime;
            public int Type;
            public int RepeatCount = 1; // 슬라이더만 사용 (기본 1)
        }
        List<OsuHitObject> parsedHitObjects = new List<OsuHitObject>();
        string parsedOsuPath = null;

        // OverlayForm 주입 .osu StartTime 목록 (HOM 교차검증용).
        // reader 자체 파싱(ParseOsuFile)보다 신뢰성 높음 — OverlayForm이 이미 파싱한 결과 재사용.
        // mapKey 변경 시 foundPlayerHomOff를 -1로 무효화하여 오프셋 재감지 트리거.
        List<int> parsedStartTimes = new List<int>();
        List<int> parsedTypes = new List<int>(); // OverlayForm 주입 .osu Type 목록 (type & 0xF)
        string parsedOsuKey = null;

        /// <summary>
        /// OverlayForm이 맵 파싱 후 호출 — .osu 교차검증용 StartTime + Type 목록 주입.
        /// reader 자체 파식(ParseOsuFile)보다 신뢰성 높음 (OverlayForm이 이미 파싱한 결과 재사용).
        /// mapKey가 바뀌면 (맵 전환) 감지된 HOM 오프셋을 무효화하여 재감지 트리거.
        /// </summary>
        public void SetParsedStartTimes(List<int> startTimes, List<int> types, string mapKey)
        {
            parsedStartTimes = startTimes ?? new List<int>();
            parsedTypes = types ?? new List<int>();
            if (mapKey != parsedOsuKey)
            {
                parsedOsuKey = mapKey;
                foundPlayerHomOff = -1; // 맵 변경 → 오프셋 재감지
                foundHomListOff = -1;
            }
        }

        string GetOsuFilePathFromBeatmap(IntPtr beatmapObj)
        {
            if (beatmapObj == IntPtr.Zero) return null;

            IntPtr folderPtr, filenamePtr;
            if (!pm.ReadPointer(beatmapObj + Offsets.Beatmap_Folder, out folderPtr)) return null;
            if (!pm.ReadPointer(beatmapObj + Offsets.Beatmap_OsuFilename, out filenamePtr)) return null;
            if (folderPtr == IntPtr.Zero || filenamePtr == IntPtr.Zero) return null;

            string folder = pm.ReadSharpString(folderPtr);
            string filename = pm.ReadSharpString(filenamePtr);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(filename)) return null;

            string osuDir = OsuInstallDir;
            if (osuDir == null) return null;

            string path = System.IO.Path.Combine(osuDir, "Songs", folder, filename);
            return System.IO.File.Exists(path) ? path : null;
        }

        // .osu 파일 파싱 — [HitObjects] 섹션만 (검증용)
        void ParseOsuFile(string path)
        {
            parsedHitObjects.Clear();
            parsedOsuPath = path;

            try
            {
                string[] lines = System.IO.File.ReadAllLines(path);
                bool inHitObjects = false;

                foreach (string line in lines)
                {
                    if (line.StartsWith("[HitObjects]", StringComparison.OrdinalIgnoreCase))
                    {
                        inHitObjects = true;
                        continue;
                    }
                    if (line.StartsWith("[", StringComparison.Ordinal) && inHitObjects)
                        break;

                    if (!inHitObjects || string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split(',');
                    if (parts.Length < 4) continue;

                    int time, type;
                    if (!int.TryParse(parts[2], out time)) continue;
                    if (!int.TryParse(parts[3], out type)) continue;

                    int ty = type & 0xF;
                    int repeatCount = 1;
                    // 슬라이더(type&0xF==2)면 repeatCount 파싱: parts[6]이 slides
                    // 슬라이더 줄: x,y,time,type,sound,curve|...,slides,length,...
                    if (ty == 2 && parts.Length > 6)
                    {
                        int slides;
                        if (int.TryParse(parts[6], out slides) && slides >= 1)
                            repeatCount = slides;
                    }

                    parsedHitObjects.Add(new OsuHitObject { StartTime = time, Type = ty, RepeatCount = repeatCount });
                }
            }
            catch { }
        }

        // .osu 파싱 결과로부터 예상 HOM count 계산
        // circle: 1개, slider: 1 + repeatCount개, spinner: 1개
        int CalcExpectedHomCount()
        {
            if (parsedHitObjects.Count == 0) return 0;
            int total = 0;
            foreach (var ho in parsedHitObjects)
            {
                if (ho.Type == 2) // slider
                    total += 1 + ho.RepeatCount;
                else // circle, spinner
                    total += 1;
            }
            return total;
        }

        // HOM 오프셋 탐색용 블록 읽기 버퍼 (D2) — 매 프레임 할당 방지
        byte[] homPlayerBuf = new byte[0x200];
        byte[] homCandBuf = new byte[0xA4];

        /// <summary>
        /// 블록 버퍼가 유효하면 거기서, 아니면 개별 syscall로 포인터를 읽는다.
        /// ReadBytes는 범위 안에 못 읽는 페이지가 하나라도 있으면 통째로 실패하므로,
        /// 블록 읽기가 실패한 경우에도 예전과 같은 결과가 나오도록 폴백을 둔다.
        /// </summary>
        bool ReadPtrCached(byte[] buf, bool bufValid, IntPtr baseAddr, int off, out IntPtr val)
        {
            if (bufValid)
            {
                val = ProcessMemory.GetPointer(buf, off);
                return true;
            }
            return pm.ReadPointer(baseAddr + off, out val);
        }

        // HOM 오프셋 자동 감지 (Player.Instance → HOM → hitObjects List)
        bool DetectHomOffsets(IntPtr playerObj)
        {
            if (playerObj == IntPtr.Zero) return false;

            // .osu 검증 데이터 — parsedStartTimes(OverlayForm 주입) 우선, parsedHitObjects(자체 파싱) 폴백
            int osuCount = parsedStartTimes.Count > 0 ? parsedStartTimes.Count : parsedHitObjects.Count;
            int osuSt0 = parsedStartTimes.Count > 0 ? parsedStartTimes[0] : (parsedHitObjects.Count > 0 ? parsedHitObjects[0].StartTime : -1);
            int osuTy0 = parsedTypes.Count > 0 ? parsedTypes[0] : (parsedHitObjects.Count > 0 ? parsedHitObjects[0].Type : -1);

            // Player 객체의 후보 슬롯 범위를 한 번에 읽는다 — 예전에는 오프셋마다 syscall이라
            // 미검출 동안 프레임당 127 + (후보수 × 40)번의 ReadProcessMemory가 돌았다 (D2).
            bool playerBufOk = pm.ReadBytes(playerObj, homPlayerBuf, homPlayerBuf.Length);

            for (int off = 0x04; off <= 0x1FC; off += 4)
            {
                IntPtr homCand;
                if (!ReadPtrCached(homPlayerBuf, playerBufOk, playerObj, off, out homCand)) continue;
                if (!LooksLikeHeapPtr((uint)homCand.ToInt32())) continue;

                // 후보 객체의 리스트 슬롯 범위도 한 번에
                bool candBufOk = pm.ReadBytes(homCand, homCandBuf, homCandBuf.Length);

                for (int listOff = 0x04; listOff <= 0xA0; listOff += 4)
                {
                    IntPtr listCand;
                    if (!ReadPtrCached(homCandBuf, candBufOk, homCand, listOff, out listCand)) continue;
                    if (!LooksLikeHeapPtr((uint)listCand.ToInt32())) continue;

                    IntPtr items;
                    if (!pm.ReadPointer(listCand + 0x04, out items)) continue;
                    if (!LooksLikeHeapPtr((uint)items.ToInt32())) continue;

                    int count;
                    if (!pm.ReadInt32(listCand + 0x10, out count)) continue;
                    if (count < 1) continue;

                    // count 검증: .osu 객체 수와 ±2 허용 (내부 객체 1~2개 차이 가능)
                    if (Math.Abs(count - osuCount) > 2)
                        continue;

                    // 첫 HitObject 검증 — StartTime + Type 일치
                    IntPtr ho0;
                    if (!pm.ReadPointer(items + 0x08, out ho0)) continue;
                    if (!LooksLikeHeapPtr((uint)ho0.ToInt32())) continue;

                    int st0, et0, ty0;
                    if (!pm.ReadInt32(ho0 + 0x10, out st0)) continue;
                    if (!pm.ReadInt32(ho0 + 0x14, out et0)) continue;
                    if (!pm.ReadInt32(ho0 + 0x18, out ty0)) continue;

                    if (st0 < 0 || st0 > 3600000) continue;
                    if (et0 < st0 || et0 > 3600000) continue;
                    if (ty0 == 0) continue;
                    if (osuSt0 >= 0 && st0 != osuSt0) continue;
                    // Type 검증 — NewCombo(4) 비트 제외하고 circle/slider/spinner 비트만 비교
                    // osu!가 HOM에 객체를 넣을 때 NewCombo 비트를 추가할 수 있음
                    if (osuTy0 >= 0 && (ty0 & 0x0B) != (osuTy0 & 0x0B)) continue;

                    // count + 첫 객체 StartTime + Type 일치 → HOM 확정
                    foundPlayerHomOff = off;
                    foundHomListOff = listOff;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// HitObject 리스트에서 판정 데이터 읽기.
        /// 맵 로드 시 1회: HOM → List → 포인터 + StartTime 캐싱.
        /// 매 프레임: 캐싱된 포인터에서 시간 범위 내 객체만 IsHit/ScoreValue 읽기.
        /// </summary>
        public List<HitObjectJudgement> ReadHitObjectJudgements(int maxCount, int timeRangeMs = 0)
        {
            // 재사용 리스트 — Clear만 하고 새 할당 없음
            reusedJudgements.Clear();
            List<HitObjectJudgement> result = reusedJudgements;

            // 맵 변경 감지 — Beatmap 객체 주소만 비교 (매 프레임 ReadPointer 1번)
            IntPtr beatmapObj;
            if (!pm.ReadPointer(beatmapStaticAddr, out beatmapObj) || beatmapObj == IntPtr.Zero)
                return result;

            // Beatmap 객체 주소가 바뀌면 맵 전환 — StartTime 캐시/오프셋 무효화
            if (beatmapObj != lastBeatmapObj)
            {
                lastBeatmapObj = beatmapObj;
                hoCacheReady = false;
                cachedHoStartTimes = null;
                cachedHoCount = 0;
                foundPlayerHomOff = -1;
                foundHomListOff = -1;

                // .osu 파일 파싱 (맵 변경 시 1회) — 주입된 parsedStartTimes의 보조 폴백
                string osuPath = GetOsuFilePathFromBeatmap(beatmapObj);
                if (osuPath != null && osuPath != parsedOsuPath)
                    ParseOsuFile(osuPath);
            }

            // === 단일 흐름: 매 프레임 동일 경로. GC compaction 후에도 자가 복구. ===
            // 핵심: foundPlayerHomOff < 0 일 때마다 DetectHomOffsets 재시도 (hoCacheReady와 무관).

            if (playerInstanceSlot == IntPtr.Zero) return result;

            IntPtr playerObj;
            if (!pm.ReadPointer(playerInstanceSlot, out playerObj) || !LooksLikeHeapPtr((uint)playerObj.ToInt32()))
                return result;

            // 오프셋 미감지 시 매 프레임 재시도 (초기 진입 / GC 후 무효화 / 일시적 실패 후 자가 복구)
            if (foundPlayerHomOff < 0)
            {
                if (!DetectHomOffsets(playerObj))
                    return result; // 다음 프레임 다시 시도
            }

            // 매 프레임 오프셋 체인 재읽기 — CLR이 GC 시 참조를 갱신하므로 항상 fresh 주소 획득.
            // 일시적 실패 시 오프셋을 무효화하고 다음 프레임 DetectHomOffsets로 자가 복구.
            IntPtr hom;
            if (!pm.ReadPointer(playerObj + foundPlayerHomOff, out hom) || !LooksLikeHeapPtr((uint)hom.ToInt32()))
            {
                foundPlayerHomOff = -1;
                return result;
            }

            IntPtr listObj;
            if (!pm.ReadPointer(hom + foundHomListOff, out listObj) || !LooksLikeHeapPtr((uint)listObj.ToInt32()))
            {
                foundPlayerHomOff = -1;
                return result;
            }

            IntPtr itemsArr;
            if (!pm.ReadPointer(listObj + 0x04, out itemsArr) || !LooksLikeHeapPtr((uint)itemsArr.ToInt32()))
            {
                foundPlayerHomOff = -1;
                return result;
            }

            int hitCount;
            if (!pm.ReadInt32(listObj + 0x10, out hitCount) || hitCount <= 0)
            {
                foundPlayerHomOff = -1;
                return result;
            }
            // .osu 객체 수와 ±2 허용 (내부 객체 1~2개 차이 가능)
            int osuCount = parsedStartTimes.Count > 0 ? parsedStartTimes.Count : parsedHitObjects.Count;
            if (osuCount > 0 && Math.Abs(hitCount - osuCount) > 2)
            {
                foundPlayerHomOff = -1;
                return result;
            }

            // StartTime 배열 캐싱 — 맵 로드/재감지 후 1회만 (StartTime은 GC-불변).
            // 포인터는 매 프레임 itemsArr에서 재읽으므로 캐싱하지 않음.
            // 주의: 맵 전체(hitCount)를 캐싱해야 시간이 흘러도 이진 탐색 창이 비지 않음.
            //       (이전에 Math.Min(hitCount, maxCount)로 500개만 캐싱 → 콤보 진행 시 창이 비어 끊김)
            if (!hoCacheReady)
            {
                cachedHoStartTimes = new int[hitCount];
                cachedHoEndTimes = new int[hitCount];
                cachedHoCount = 0;
                int maxDuration = 0;

                for (int i = 0; i < hitCount; i++)
                {
                    IntPtr hoPtr;
                    if (!pm.ReadPointer(itemsArr + 0x08 + i * 4, out hoPtr)) break;
                    if (hoPtr != IntPtr.Zero && LooksLikeHeapPtr((uint)hoPtr.ToInt32()))
                    {
                        int st;
                        pm.ReadInt32(hoPtr + Offsets.HitObject_StartTime, out st);
                        cachedHoStartTimes[i] = st;
                        int et;
                        pm.ReadInt32(hoPtr + Offsets.HitObject_EndTime, out et);
                        cachedHoEndTimes[i] = et;
                        int dur = et - st;
                        if (dur > maxDuration) maxDuration = dur;
                    }
                    else
                    {
                        cachedHoStartTimes[i] = -1;
                        cachedHoEndTimes[i] = -1;
                    }
                    cachedHoCount++;
                }
                cachedMaxDuration = maxDuration;

                hoCacheReady = true;
            }

            // 이진 탐색으로 시간 창 [idxStart, idxEnd) 산출 (cachedHoStartTimes 사용 — GC-불변)
            int idxStart = 0, idxEnd = cachedHoCount;
            if (timeRangeMs > 0)
            {
                // 비대칭 시간 범위 — 과거는 timeRangeMs, 미래는 500ms만
                int timeMin = TimeMs - timeRangeMs;
                int timeMax = TimeMs + 500;

                // 긴 객체(스피너/슬라이더) 예외: StartTime이 과거여도 EndTime이 현재 이후면 진행 중.
                // cachedMaxDuration(맵에서 가장 긴 객체 지속시간)만큼 과거까지 검색해서
                // 진행 중인 긴 객체가 빠지지 않도록 함.
                // 실제 포함 여부는 루프에서 cachedHoEndTimes[i] >= timeMin으로 최종 판단.
                int searchMin = timeMin - cachedMaxDuration;

                int lo = 0, hi = cachedHoCount;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (cachedHoStartTimes[mid] < searchMin)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                idxStart = Math.Max(0, lo - 5);

                lo = 0; hi = cachedHoCount;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (cachedHoStartTimes[mid] <= timeMax)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                idxEnd = Math.Min(cachedHoCount, lo + 5);
            }

            // 배치 읽기 버퍼 — 재사용 (매 프레임 new 방지)
            byte[] hoBatch = reusedHoBatch;

            // 시간 창 내 객체만 — 매 프레임 itemsArr에서 포인터 재읽기 (GC-안전)
            // cachedHoEndTimes로 이미 끝난 객체를 스킵하여 ReadProcessMemory 호출 최소화.
            int timeMinFilter = (timeRangeMs > 0) ? (TimeMs - timeRangeMs) : int.MinValue;
            int readCount = 0;
            for (int i = idxStart; i < idxEnd; i++)
            {
                int startTimeVal = cachedHoStartTimes[i];
                if (startTimeVal < 0) continue;

                // EndTime이 timeMin 이전이면 이미 끝난 객체 — ReadProcessMemory 스킵
                if (cachedHoEndTimes[i] < timeMinFilter) continue;

                // maxCount 안전장치 — 실제 읽는 객체 수 제한
                if (readCount >= maxCount) break;
                readCount++;

                // 매 프레임 fresh 포인터 — CLR이 GC 시 배열 내부 참조를 갱신하므로 항상 최신 위치
                IntPtr hoPtr;
                if (!pm.ReadPointer(itemsArr + 0x08 + i * 4, out hoPtr)) continue;
                if (hoPtr == IntPtr.Zero) continue;
                if (!LooksLikeHeapPtr((uint)hoPtr.ToInt32())) continue;

                HitObjectJudgement j = new HitObjectJudgement(); // struct — 스택 할당, GC 없음
                j.StartTime = startTimeVal;

                // Type을 먼저 읽기 — 0x0C바이트 (StartTime+EndTime+Type)
                if (!pm.ReadBytes(hoPtr + 0x10, hoBatch, 0x0C)) continue;

                j.EndTime = ProcessMemory.GetInt32(hoBatch, 0x04); // hoPtr+0x14
                j.Type = ProcessMemory.GetInt32(hoBatch, 0x08);     // hoPtr+0x18

                if (j.EndTime < j.StartTime || j.EndTime > 3600000) continue;
                if (j.Type == 0) continue;

                // 타입별 필요한 만큼만 추가 읽기
                bool isSlider = (j.Type & 2) != 0;
                bool isSpinner = (j.Type & 8) != 0;
                int readSize;
                if (isSpinner)
                    readSize = 0x100; // hoPtr+0x10 ~ hoPtr+0x110 (FloatRotationCount 0x10C까지)
                else if (isSlider)
                    readSize = 0x118; // hoPtr+0x10 ~ hoPtr+0x128 (IsTracking 0x120까지)
                else
                    readSize = 0x78;  // 원 — HitValue/ScoreValue/IsHit만

                if (!pm.ReadBytes(hoPtr + 0x10, hoBatch, readSize)) continue;

                j.HitValue = ProcessMemory.GetInt32(hoBatch, 0x4C); // hoPtr+0x5C
                j.ScoreValue = ProcessMemory.GetInt32(hoBatch, 0x70); // hoPtr+0x80
                j.IsHit = ProcessMemory.GetByte(hoBatch, 0x74);     // hoPtr+0x84

                if (isSlider)
                {
                    // 슬라이더 — IsTracking(0x120)과 SliderStartCircle(0xD0) 모두 배치 범위 내
                    j.IsTracking = ProcessMemory.GetByte(hoBatch, 0x110); // hoPtr+0x120 - 0x10 = 0x110
                    IntPtr sliderStart = ProcessMemory.GetPointer(hoBatch, 0xC0); // hoPtr+0xD0 - 0x10 = 0xC0
                    if (sliderStart != IntPtr.Zero && LooksLikeHeapPtr((uint)sliderStart.ToInt32()))
                    {
                        byte startIsHit;
                        if (pm.ReadByte(sliderStart + Offsets.HitObject_IsHit, out startIsHit))
                            j.StartIsHit = startIsHit;
                    }
                }

                if (isSpinner)
                {
                    // 스피너 — 모든 필드가 배치 범위 내
                    j.FloatRotationCount = ProcessMemory.GetFloat(hoBatch, 0xFC);  // hoPtr+0x10C - 0x10 = 0xFC
                    j.ScoringRotationCount = ProcessMemory.GetInt32(hoBatch, 0xE4); // hoPtr+0xF4 - 0x10 = 0xE4
                    j.RotationRequirement = ProcessMemory.GetInt32(hoBatch, 0xE8);  // hoPtr+0xF8 - 0x10 = 0xE8
                    j.SpinningState = ProcessMemory.GetInt32(hoBatch, 0xF8);        // hoPtr+0x108 - 0x10 = 0xF8
                }

                result.Add(j);
            }

            return result;
        }

        public void Dispose()
        {
            if (pm != null)
                pm.Dispose();
        }

        /// <summary>
        /// hoCache 무효화 — retry 후 stale 포인터 방지.
        /// ReadHitObjectJudgements가 다음 호출 시 HitObject 배열 재스캔.
        /// </summary>
        public void InvalidateHoCache()
        {
            hoCacheReady = false;
            cachedHoStartTimes = null;
            cachedHoCount = 0;
            lastBeatmapObj = IntPtr.Zero;
        }
    }
}