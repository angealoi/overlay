using System;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// SIG.MD 4~8절 — 필드 오프셋 상수.
    /// 모든 오프셋은 32-bit CLR 기준, 4중 검증 완료.
    /// </summary>
    internal static class Offsets
    {
        // ── SIG.MD 2절: AudioState (Pause Detection) ──
        // time_slot + 0x30 = AudioState (0=Stopped, 1=Playing, 2=Seeking)
        public const int AudioState_FromTimeSlot = 0x30;

        // ── SIG.MD 3절: Score Walk Chain ──
        // Ruleset → gameplayBase → scoreBase
        public const int Ruleset_GameplayBase = 0x64;   // Ruleset + 0x64 → gameplayBase
        public const int GameplayBase_ScoreBase = 0x38; // gameplayBase + 0x38 → scoreBase
        public const int GameplayBase_HpBar = 0x40;     // gameplayBase + 0x40 → hpBar
        public const int GameplayBase_Accuracy = 0x48;  // gameplayBase + 0x48 → accuracyObj
        public const int Accuracy_Value = 0x0C;         // accuracyObj + 0x0C → double (0.0~1.0)

        // ── SIG.MD 4절: Beatmap Field Offsets ──
        // Beatmap 객체 기준
        public const int Beatmap_AR = 0x2C;              // float (ApproachRate)
        public const int Beatmap_CS = 0x30;              // float (CircleSize)
        public const int Beatmap_HP = 0x34;              // float (HpDrainRate)
        public const int Beatmap_OD = 0x38;              // float (OverallDifficulty)
        public const int Beatmap_Artist = 0x18;          // string ptr
        public const int Beatmap_Title = 0x24;            // string ptr
        public const int Beatmap_Folder = 0x78;          // string ptr
        public const int Beatmap_OsuFilename = 0x90;     // string ptr
        public const int Beatmap_DifficultyName = 0xAC;  // string ptr
        public const int Beatmap_MD5 = 0x6C;             // string ptr

        // ── SIG.MD 5절: HitObject Field Offsets ──
        // HitObject 객체 기준
        public const int HitObject_StartTime = 0x10;     // int (ms)
        public const int HitObject_EndTime = 0x14;       // int (ms)
        public const int HitObject_Type = 0x18;           // bitmask (bit0=circle, bit1=slider, bit3=spinner)
        public const int HitObject_HitValue = 0x5C;       // IncreaseScoreType (256=50, 512=100, 1024=300, -131072=Miss)
        public const int HitObject_ScoreValue = 0x80;     // 실제 판정 (300/100/50/0)
        public const int HitObject_IsHit = 0x84;          // byte; 판정 시 1로 set
        public const int HitObject_SliderStartCircle = 0xD0; // ref (슬라이더만)
        public const int HitObject_IsTracking = 0x120;   // byte; slider-hold flag

        // ── Ruleset → HitObjectManager 체인 ──
        // 고정 오프셋 상수는 두지 않음: Eazfuscator가 빌드마다 필드를 재배열하므로
        // 참조 구현들의 값(Ruleset+0x18 / +0x48 / +0x68→+0x3C)이 이 빌드에서 맞다는 보장이 없음.
        // OsuMemoryReader.DetectHomOffsets()가 .osu 파싱 결과와 교차검증해 런타임에 감지함.

        // ── .NET List<T> 내부 레이아웃 ──
        public const int List_Items = 0x04;    // _items 배열 참조
        public const int List_Size = 0x0C;     // _size (LO 빌드에서는 0일 수 있음)
        public const int Array_Length = 0x04;  // 배열 capacity
        public const int Array_Data = 0x08;   // element[0] 시작

        // ── SIG.MD 6절: Score Object Field Offsets ──
        // scoreBase 기준
        public const int Score_Mods = 0x1C;              // mods wrapper ref
        public const int Score_PlayerName = 0x28;        // string ref
        public const int Score_HitErrors = 0x38;         // List<int> ref (판정 오차 ms)
        public const int Score_Mode = 0x64;               // PlayModes enum
        public const int Score_MaxCombo = 0x68;           // int
        public const int Score_TotalScore = 0x78;         // int
        public const int Score_Count100 = 0x88;           // ushort
        public const int Score_Count300 = 0x8A;           // ushort
        public const int Score_Count50 = 0x8C;            // ushort
        public const int Score_CountGeki = 0x8E;          // ushort
        public const int Score_CountKatu = 0x90;          // ushort
        public const int Score_CountMiss = 0x92;          // ushort
        public const int Score_CurrentCombo = 0x94;       // ushort

        // ── SIG.MD 7절: Slider Scoring Structures ──
        // 동적 식별 (Eazfuscator가 필드 순서 바꿈)
        public const int Slider_RepeatPoints = 0xD8;      // List<int> ref (동적 스캔 권장)
        public const int Slider_ScoreTimingPoints = 0xDC; // List<int> ref (동적 스캔 권장)

        // ── SIG.MD 8절: Spinner Offsets ──
        // Spinner 객체 기준
        public const int Spinner_ScoringRotationCount = 0xF4;  // int
        public const int Spinner_RotationRequirement = 0xF8;   // int
        public const int Spinner_SpinningState = 0x108;         // int (0=NotStarted, 1=Started, 2=Passed)
        public const int Spinner_FloatRotationCount = 0x10C;  // float

        // ── SIG.MD 9절: Cursor (Vector2 copy function) ──
        // source 객체 기준
        public const int Cursor_X = 0x04;                  // float
        public const int Cursor_Y = 0x08;                  // float

        // ── Mod bits (SIG.MD 1절 MenuMods) ──
        public const uint Mod_None    = 0;
        public const uint Mod_EZ      = 1 << 1;   // Easy
        public const uint Mod_HD      = 1 << 3;   // Hidden
        public const uint Mod_HR      = 1 << 4;   // HardRock
        public const uint Mod_SD      = 1 << 5;   // SuddenDeath
        public const uint Mod_DT      = 1 << 6;   // DoubleTime
        public const uint Mod_RX      = 1 << 7;   // Relax
        public const uint Mod_HT      = 1 << 8;   // HalfTime
        public const uint Mod_NC      = 1 << 9;   // Nightcore
        public const uint Mod_FL      = 1 << 10;  // Flashlight
        public const uint Mod_AT      = 1 << 11;  // Auto
        public const uint Mod_SO      = 1 << 12;  // SpunOut
        public const uint Mod_PF      = 1 << 14;  // Perfect

        // ── OsuModes enum (SIG.MD 1절 GameBase.Mode) ──
        public const int Mode_Menu          = 0;
        public const int Mode_Edit          = 1;
        public const int Mode_Play          = 2;
        public const int Mode_Exit          = 3;
        public const int Mode_SelectEdit    = 4;
        public const int Mode_SelectPlay    = 5;
        public const int Mode_Rank          = 7;

        // ── AudioStates enum (SIG.MD 2절) ──
        public const int AudioState_Stopped = 0;   // paused
        public const int AudioState_Playing = 1;
        public const int AudioState_Seeking = 2;

        // ── Render at Native Resolution 관련 ──
        // WindowManager 객체 필드 오프셋 (int 필드)
        public const int WindowManager_Width     = 0x04;  // int (실제 렌더링 너비)
        public const int WindowManager_Height    = 0x08;  // int (실제 렌더링 높이)
        public const int WindowManager_SpriteRes = 0x0C;  // int (기준 해상도, 보통 768)

        // BindableInt 필드 오프셋 (double Value)
        public const int BindableInt_Value = 0x04;  // double (설정값)

        // BindableBool 필드 오프셋 (byte Value)
        public const int BindableBool_Value = 0x0C;  // byte (0=false, 1=true)

        // ── ConfigManager Dictionary (tosu 방식) ──
        // Dictionary<string, Bindable> 구조
        public const int Dict_Entries = 0x08;  // entries 배열 포인터
        public const int Dict_Count   = 0x1C;  // int (entry 개수)
        public const int Dict_EntryStride = 0x10;  // 각 entry 크기 (16바이트)
        public const int Dict_EntryKey   = 0x00;   // string ptr
        public const int Dict_EntryValue = 0x04;   // Bindable 객체 ptr

        // WindowManager 기본 상수
        public const int WindowManager_DefaultWidth  = 640;
        public const int WindowManager_DefaultHeight = 480;
        public const int WindowManager_DefaultSpriteRes = 768;
        public const int GameField_DefaultWidth  = 512;
        public const int GameField_DefaultHeight = 384;
    }
}