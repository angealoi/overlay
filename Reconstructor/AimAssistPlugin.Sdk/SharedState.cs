using System.Runtime.InteropServices;

namespace AimAssistPlugin.Sdk;

/// <summary>
/// OsuEnlightenOverlay2 ↔ Reconstructor 간 공유 메모리 스키마.
/// .NET Framework 4.8 (writer) ↔ .NET 8 (reader) 양쪽에서 동일한 메모리 레이아웃을 갖도록
/// Sequential/Pack=8 + 단순 타입(int/float/uint)만 사용. 문자열은 별도 영역에 UTF-8로 저장.
///
/// 이 파일은 양쪽 프로젝트에 동일한 소스로 배치됨 (한쪽을 고치면 반대쪽도 같이 고칠 것).
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

    /// <summary>현재 맵 BeatmapID (매핑 정보용, 디버그/통계 외 용도 없음)</summary>
    public int BeatmapId;

    /// <summary>osu! PlayMode (0=osu!, 1=Taiko, 2=Catch, 3=Mania)</summary>
    public int PlayMode;

    // ── Difficulty Changer 반영된 최종값 ──
    // OsuEnlightenOverlay2의 DifficultyController.Compute() 결과. 맵 원본값 + mod + 사용자 override가
    // 모두 적용된 값. Reconstructor는 이 값을 그대로 쓰면 됨 (변환 공식 중복/불일치 방지).
    // DifficultyReady == 0 이면 currentDifficulty가 아직 계산되지 않은 상태 — Reconstructor는
    // 폴백으로 BeatmapAR/BeatmapCS(raw)를 쓸 것.

    /// <summary>1 = 아래 PreEmpt/HitObjectRadius가 유효함. 0 = 아직 맵 로드/계산 전 — 폴백 필요.</summary>
    public int DifficultyReady;

    /// <summary>AR(mod+override 반영) → PreEmpt (ms, 곡 시간 기준).</summary>
    public int PreEmpt;

    /// <summary>CS(mod+override 반영) → HitObject 반지름 (osu! pixel, 0~64 범위).</summary>
    public float HitObjectRadius;

    // ── GameField 좌표 변환 파라미터 ──
    // OsuEnlightenOverlay2의 GameField.Update()가 계산한 값.
    // Reconstructor는 이 값을 사용해 hitObject.Position을 화면 좌표로 변환 —
    // 양쪽이 동일한 좌표계를 써야 어시스트가 정확히 노트 위치로 향함.
    // GameFieldReady == 0 이면 폴백으로 Reconstructor 자체 공식 사용.

    /// <summary>1 = 아래 필드들이 유효함.</summary>
    public int GameFieldReady;

    /// <summary>화면 좌표 = field * Ratio + Offset. Reconstructor의 GetRatio() 대체.</summary>
    public float GameFieldRatio;

    /// <summary>FieldToDisplay 변환의 X 오프셋 (화면 픽셀).</summary>
    public float GameFieldOffsetX;

    /// <summary>FieldToDisplay 변환의 Y 오프셋 (화면 픽셀).</summary>
    public float GameFieldOffsetY;

    /// <summary>osu! 인게임 커서 X (화면 픽셀, osu! 창 기준).</summary>
    public float CursorX;

    /// <summary>osu! 인게임 커서 Y (화면 픽셀, osu! 창 기준).</summary>
    public float CursorY;

    // ── osu! 게임 필드 좌상단 (화면 좌표) ──
    // "게임 필드 좌상단" = 렌터박싱(native resolution OFF)일 때 검은 여백을 제외한 실제
    // 렌더 영역의 좌상단. native resolution ON이면 osu! 클라이언트 영역 좌상단과 동일.
    // Reconstructor는 이 값을 기준으로 GameField offset + field*ratio 변환을 적용한다.
    // ⚠️ 주의: osu! 클라이언트 영역 좌상단이 아님 — 렌터박싱 시 두 값이 다름.

    /// <summary>osu! 게임 필드 좌상단 X (화면 좌표, 주 모니터 기준).</summary>
    public int OsuWindowX;

    /// <summary>osu! 게임 필드 좌상단 Y (화면 좌표, 주 모니터 기준).</summary>
    public int OsuWindowY;
}

/// <summary>
/// 공유 메모리 영역 오프셋 상수.
/// MMF 전체 크기 = sizeof(SharedState) + 문자열 영역 2개.
/// </summary>
public static class SharedStateLayout
{
    /// <summary>MMF 전체 크기 (바이트).</summary>
    public const int TotalSize = HeaderSize + StringAreaSize;

    /// <summary>구조체 영역 크기.</summary>
    public const int HeaderSize = 128; // SharedState(56) 보다 넉넉히 — 미래 필드 확장 여유

    /// <summary>문자열 저장 영역 — 각 문자열마다 4바이트 길이 + UTF-8 바이트.</summary>
    public const int StringAreaSize = 2048;

    public const int StringAreaOffset = HeaderSize;

    /// <summary>문자열 슬롯 정의. 각 슬롯은 [4바이트 길이][최대 N바이트 UTF-8] 형태.</summary>
    public static readonly StringSlot BeatmapFolder = new StringSlot(0, 512);
    public static readonly StringSlot BeatmapOsuFilename = new StringSlot(512, 512);
    public static readonly StringSlot OsuInstallDir = new StringSlot(1024, 1024);

    /// <summary>
    /// 문자열 슬롯 — MMF 문자열 영역 내의 위치와 최대 길이.
    /// 첫 4바이트는 UTF-8 바이트 길이(int), 이후 지정된 capacity만큼 UTF-8 바이트.
    /// </summary>
    public readonly struct StringSlot
    {
        public readonly int Offset;
        public readonly int Capacity;

        public StringSlot(int offset, int capacity)
        {
            Offset = offset;
            Capacity = capacity;
        }

        /// <summary>문자열 영역 기준 offset이 아닌 MMF 시작 기준 절대 offset.</summary>
        public int AbsoluteOffset => StringAreaOffset + Offset;
    }
}

/// <summary>
/// osu! MenuMods 비트마스크. OsuEnlightenOverlay.Memory.Offsets 와 동일 값.
/// Reconstructor는 이 값으로 비트 검사만 하면 됨 (문자열 "HDHR" 파싱 불필요).
/// </summary>
public static class SharedMods
{
    public const uint None = 0;
    public const uint EZ   = 1 << 1;   // 0x002  Easy
    public const uint HD   = 1 << 3;   // 0x008  Hidden
    public const uint HR   = 1 << 4;   // 0x010  HardRock
    public const uint SD   = 1 << 5;   // 0x020  SuddenDeath
    public const uint DT   = 1 << 6;   // 0x040  DoubleTime
    public const uint RX   = 1 << 7;   // 0x080  Relax
    public const uint HT   = 1 << 8;   // 0x100  HalfTime
    public const uint NC   = 1 << 9;   // 0x200  Nightcore (DT 비트도 같이 설정됨)
    public const uint FL   = 1 << 10;  // 0x400  Flashlight
    public const uint AT   = 1 << 11;  // 0x800  Auto
    public const uint SO   = 1 << 12;  // 0x1000 SpunOut
    public const uint PF   = 1 << 14;  // 0x4000 Perfect
}
