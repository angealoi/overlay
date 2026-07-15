using System;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// SIG.MD 1절 — AOB 시그니처 정의.
    /// 각 sig는 매치 후 특정 오프셋의 4바이트 absolute address operand를 읽어
    /// static field slot을 해석.
    /// </summary>
    internal struct AobSignature
    {
        public string Name;
        public string Pattern;       // "5E 5F 5D C3 A1 ?? ?? ?? ?? 89 ?? 04"
        public int OperandSkip;      // 매치 위치에서 operand까지의 오프셋
        public int PostAdd;          // operand 읽은 후 추가할 값
    }

    internal static class Signatures
    {
        // SIG.MD 1절: AudioEngine.Time (ms ticker)
        // Real name: AudioEngine.Time / OsuMain.Time
        // Pattern: 5E 5F 5D C3 A1 ?? ?? ?? ?? 89 ?? 04
        // Operand skip: +5 → 4바이트 abs addr = time_slot
        // Read: int32 [slot] = 현재 오디오 시간 (ms)
        public static readonly AobSignature AudioEngineTime = new AobSignature
        {
            Name = "AudioEngine.Time",
            Pattern = "5E 5F 5D C3 A1 ?? ?? ?? ?? 89 ?? 04",
            OperandSkip = 5,
            PostAdd = 0
        };

        // SIG.MD 1절: GameBase.Mode (OsuModes enum)
        // Pattern: 48 83 F8 04 73 1E
        // Operand skip: -4 → static slot
        // Read: int32 [slot] = OsuModes enum (0=Menu, 2=Play, ...)
        public static readonly AobSignature GameBaseMode = new AobSignature
        {
            Name = "GameBase.Mode",
            Pattern = "48 83 F8 04 73 1E",
            OperandSkip = -4,
            PostAdd = 0
        };

        // SIG.MD 1절: MenuMods (mods bitmask)
        // Pattern: C8 FF ?? ?? ?? ?? ?? 81 0D ?? ?? ?? ?? ?? 08 00 00
        // Operand skip: +9 → mods slot
        // Read: uint32 [slot] = mods bitmask (HD=bit3, HR=bit4, FL=bit10, DT=bit6, HT=bit8, NC=bit9)
        public static readonly AobSignature MenuMods = new AobSignature
        {
            Name = "MenuMods",
            Pattern = "C8 FF ?? ?? ?? ?? ?? 81 0D ?? ?? ?? ?? ?? 08 00 00",
            OperandSkip = 9,
            PostAdd = 0
        };

        // SIG.MD 1절: Ruleset (Player walk entry)
        // Pattern: 7D 15 A1 ?? ?? ?? ?? 85 C0
        // Operand skip: -0xB, PostAdd: +4
        // Chain: readU32(match - 0xB) = sibling static → +4 = Ruleset slot → deref = Ruleset object
        public static readonly AobSignature Ruleset = new AobSignature
        {
            Name = "Ruleset",
            Pattern = "7D 15 A1 ?? ?? ?? ?? 85 C0",
            OperandSkip = -0xB,
            PostAdd = 4
        };

        // SIG.MD 1절: CurrentBeatmap (AR/CS/HP/OD + metadata)
        // Pattern: F8 01 74 04 83 65
        // Operand skip: -0xC → static addr → deref = beatmap_ptr
        public static readonly AobSignature CurrentBeatmap = new AobSignature
        {
            Name = "CurrentBeatmap",
            Pattern = "F8 01 74 04 83 65",
            OperandSkip = -0xC,
            PostAdd = 0
        };

        // SIG.MD 1절: PlayMode (lazer-style mode selector)
        // 동일 패턴, 다른 오프셋: -0x33
        // Read: int32 [match - 0x33] = PlayModes enum (0=Osu, 1=Taiko, 2=Catch, 3=Mania)
        public static readonly AobSignature PlayMode = new AobSignature
        {
            Name = "PlayMode",
            Pattern = "F8 01 74 04 83 65",
            OperandSkip = -0x33,
            PostAdd = 0
        };

        // SIG.MD 1절: Cursor X/Y v2 (always-alive screen-space)
        // Pattern: 8B 35 ?? ?? ?? ?? 83 C6 04 F3 0F 7E 06 66 0F D6 07 5E 5F C3
        // Operand skip: +2 → slot → source → X=readF32(source+0x04), Y=readF32(source+0x08)
        // 다중 매치 (JIT가 ~7개 소스에 대해 동일 코드 방출)
        public static readonly AobSignature CursorXY = new AobSignature
        {
            Name = "Cursor X/Y v2",
            Pattern = "8B 35 ?? ?? ?? ?? 83 C6 04 F3 0F 7E 06 66 0F D6 07 5E 5F C3",
            OperandSkip = 2,
            PostAdd = 0
        };

        // hom_walk.md §2/§3: Player::Instance (canonical HOM 진입점)
        // Player 객체는 per-play 컨트롤러이며 +0x3C에 HitObjectManagerOsu를 소유.
        // Pattern: 80 3D ?? ?? ?? ?? 00 75 26 A1 ?? ?? ?? ?? 85 C0 74 0C
        //          0  1  2  3  4  5  6  7  8  9  10 11 12 13 14 15 16 17
        // 컨텍스트: cmp byte [Player.Retrying], 0; jnz ...; mov eax, [Player.Instance_slot]; test eax, eax
        // Operand skip: +10 → A1 opcode(인덱스9) 다음의 imm32 = Player::Instance static slot
        //   (OperandSkip=9는 A1 opcode 자체를 읽어 0x..A1 꼴의 쓰레기 값이 나옴 — 검증됨)
        // Read: readU32(slot) = 현재 Player 객체 (Play 중이 아니면 0)
        public static readonly AobSignature PlayerInstance = new AobSignature
        {
            Name = "Player::Instance",
            Pattern = "80 3D ?? ?? ?? ?? 00 75 26 A1 ?? ?? ?? ?? 85 C0 74 0C",
            OperandSkip = 10,
            PostAdd = 0
        };

        // ── Render at Native Resolution 관련 ──
        // 여기 있던 WindowManager 시그니처는 제거했다.
        //
        // 패턴("A1 ?? ?? ?? ?? 8B 50 04 A1 ?? ?? ?? ?? 8B 40 08 89 95")이 SetScreenSize()
        // 내부 코드였는데, 이 메서드는 사용자가 해상도를 "바꿀 때"만 호출된다. 그냥 켜기만
        // 한 osu!에서는 JIT되지 않아 그 바이트열이 메모리에 아예 존재하지 않는다 —
        // 실측 85/85 스캔 실패. 재스캔을 붙여도 없는 패턴은 못 찾으므로 소용없다.
        // 렌더 해상도는 ConfigDictionary의 Width/Height + WidthFullscreen/HeightFullscreen을
        // Fullscreen 값으로 골라 읽는다(ResolutionReader 참고). 실측 96/96 성공.

        // ── ConfigManager Dictionary (tosu 방식) ──
        // ConfigManager의 Dictionary<string, Bindable> 에 접근하는 코드 패턴.
        // tosu stable.ts: configurationAddr pattern
        //   8D 45 EC 50 8B 0D ?? ?? ?? ?? 8B D7 39 09 E8 ?? ?? ?? ?? 85 C0 74 ?? 8B 4D EC
        //   offset: +0x6 → mov ecx, [ConfigDict_static] 의 imm32 = Dictionary 객체 포인터
        //
        // Dictionary 구조 (.NET Dictionary<string, Bindable>):
        //   +0x08 = entries 배열 (Entry 구조체 배열)
        //   +0x1C = count (int)
        // 각 Entry는 0x10 바이트:
        //   +0x00 = key string ptr (C# string)
        //   +0x04 = value ptr (Bindable 객체)
        //
        // BindableInt:  +0x04 = double Value
        // BindableBool: +0x0C = byte Value (0/1)
        // Bindable<enum>: +0x0C = int Value
        public static readonly AobSignature ConfigDictionary = new AobSignature
        {
            Name = "ConfigDictionary",
            Pattern = "8D 45 EC 50 8B 0D ?? ?? ?? ?? 8B D7 39 09 E8 ?? ?? ?? ?? 85 C0 74 ?? 8B 4D EC",
            OperandSkip = 6,
            PostAdd = 0
        };
    }
}