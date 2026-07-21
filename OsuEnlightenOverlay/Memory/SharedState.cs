using System.Runtime.InteropServices;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// OsuEnlightenOverlay2 ↔ Reconstructor 간 공유 메모리 스키마.
    /// .NET Framework 4.8 (writer, 이쪽) ↔ .NET 8 (reader, Reconstructor) 양쪽에서
    /// 동일한 메모리 레이아웃을 갖도록 Sequential/Pack=8 + 단순 타입(int/float/uint)만 사용.
    /// 문자열은 별도 영역에 UTF-8로 저장.
    ///
    /// 이 파일은 양쪽 프로젝트에 동일한 소스로 배치됨 (한쪽을 고치면 반대쪽도 같이 고칠 것).
    /// 구조체 필드 순서/타입은 절대 임의로 바꾸지 말 것 — 양쪽 레이아웃이 어긋나면 torn data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct SharedState
    {
        /// <summary>
        /// torn read 방지용 시퀀스 번호. writer는 write 시작/끝에 같은 값으로 증가시키고,
        /// reader는 시작과 끝에 읽은 값이 같으면 스냅샷이 온전한 것으로 간주.
        /// </summary>
        public int Sequence;

        /// <summary>1 = Mode==Play &amp;&amp; AudioState==Playing, 아니면 0</summary>
        public int IsPlaying;

        /// <summary>오디오 재생 시간 (ms) — reader.TimeMs</summary>
        public int TimeMs;

        /// <summary>0=Stopped, 1=Playing, 2=Seeking</summary>
        public int AudioState;

        /// <summary>OsuModes enum — 0=Menu, 2=Play, 5=SelectPlay, ...</summary>
        public int Mode;

        /// <summary>osu! MenuMods 비트마스크. <see cref="SharedMods"/> 참조.</summary>
        public uint MenuMods;

        /// <summary>현재 맵 ApproachRate (mod 변환값 — HR/EZ/DT/HT 반영됨)</summary>
        public float BeatmapAR;

        /// <summary>현재 맵 CircleSize (원본 — mod 변환 전)</summary>
        public float BeatmapCS;

        /// <summary>현재 맵 HpDrainRate</summary>
        public float BeatmapHP;

        /// <summary>현재 맵 OverallDifficulty</summary>
        public float BeatmapOD;

        /// <summary>현재 맵 BeatmapID (디버그/통계 외 용도 없음)</summary>
        public int BeatmapId;

        /// <summary>osu! PlayMode (0=osu!, 1=Taiko, 2=Catch, 3=Mania)</summary>
        public int PlayMode;

        // ── Difficulty Changer 반영된 최종값 ──
        // DifficultyController.Compute() 결과. 맵 원본값 + mod + 사용자 override가 모두 적용됨.
        // Reconstructor는 이 값을 그대로 쓰면 됨 (변환 공식 중복/불일치 방지).
        // DifficultyReady == 0 이면 currentDifficulty가 아직 계산되지 않은 상태.

        /// <summary>1 = 아래 PreEmpt/HitObjectRadius가 유효함. 0 = 아직 맵 로드/계산 전.</summary>
        public int DifficultyReady;

        /// <summary>AR(mod+override 반영) → PreEmpt (ms, 곡 시간 기준).</summary>
        public int PreEmpt;

        /// <summary>CS(mod+override 반영) → HitObject 반지름 (osu! pixel).</summary>
        public float HitObjectRadius;

        // ── GameField 좌표 변환 파라미터 ──
        // OsuEnlightenOverlay2의 GameField.Update()가 계산한 값.
        // Reconstructor는 이 값을 사용해 hitObject.Position을 화면 좌표로 변환.
        // GameFieldReady == 0 이면 Reconstructor는 자체 폴백 공식 사용.

        /// <summary>1 = 아래 필드들이 유효함.</summary>
        public int GameFieldReady;

        /// <summary>화면 좌표 = field * Ratio + Offset.</summary>
        public float GameFieldRatio;

        /// <summary>FieldToDisplay 변환의 X 오프셋 (화면 픽셀).</summary>
        public float GameFieldOffsetX;

        /// <summary>FieldToDisplay 변환의 Y 오프셋 (화면 픽셀).</summary>
        public float GameFieldOffsetY;

        /// <summary>osu! 인게임 커서 X (field 좌표계).</summary>
        public float CursorX;

        /// <summary>osu! 인게임 커서 Y (field 좌표계).</summary>
        public float CursorY;

        /// <summary>osu! 창 왼쪽 위 X (화면 좌표). OTD 좌표계 변환용.</summary>
        public int OsuWindowX;

        /// <summary>osu! 창 왼쪽 위 Y (화면 좌표). OTD 좌표계 변환용.</summary>
        public int OsuWindowY;
    }

    /// <summary>
    /// 공유 메모리 영역 오프셋 상수.
    /// MMF 전체 크기 = HeaderSize + StringAreaSize.
    /// </summary>
    public static class SharedStateLayout
    {
        public const int TotalSize = HeaderSize + StringAreaSize;

        /// <summary>구조체 영역 크기 — SharedState(56) 보다 넉넉히 (필드 확장 여유).</summary>
        public const int HeaderSize = 128;

        /// <summary>문자열 저장 영역 — 각 문자열마다 4바이트 길이 + UTF-8 바이트.</summary>
        public const int StringAreaSize = 2048;

        public const int StringAreaOffset = HeaderSize;

        public static readonly StringSlot BeatmapFolder     = new StringSlot(0,    512);
        public static readonly StringSlot BeatmapOsuFilename= new StringSlot(512,  512);
        public static readonly StringSlot OsuInstallDir     = new StringSlot(1024, 1024);

        /// <summary>
        /// 문자열 슬롯 — MMF 문자열 영역 내의 위치와 최대 길이.
        /// 첫 4바이트는 UTF-8 바이트 길이(int), 이후 지정된 capacity만큼 UTF-8 바이트.
        /// </summary>
        public struct StringSlot
        {
            public readonly int Offset;
            public readonly int Capacity;

            public StringSlot(int offset, int capacity)
            {
                Offset = offset;
                Capacity = capacity;
            }

            /// <summary>MMF 시작 기준 절대 offset.</summary>
            public int AbsoluteOffset => StringAreaOffset + Offset;
        }
    }

    /// <summary>
    /// osu! MenuMods 비트마스크. Offsets.Mod_* 와 동일 값.
    /// Reconstructor는 이 값으로 비트 검사만 하면 됨 (문자열 "HDHR" 파싱 불필요).
    /// </summary>
    public static class SharedMods
    {
        public const uint None = 0;
        public const uint EZ   = 1 << 1;
        public const uint HD   = 1 << 3;
        public const uint HR   = 1 << 4;
        public const uint SD   = 1 << 5;
        public const uint DT   = 1 << 6;
        public const uint RX   = 1 << 7;
        public const uint HT   = 1 << 8;
        public const uint NC   = 1 << 9;
        public const uint FL   = 1 << 10;
        public const uint AT   = 1 << 11;
        public const uint SO   = 1 << 12;
        public const uint PF   = 1 << 14;
    }
}
