using System;
using System.Collections.Generic;
using OpenTK;

namespace OsuEnlightenOverlay.Gameplay.Beatmap
{
    /// <summary>
    /// HitObject 타입 bitmask — osu! stable HitObjectType.
    /// </summary>
    [Flags]
    public enum HitObjectType
    {
        Normal = 1,           // bit 0 = circle
        Slider = 2,           // bit 1 = slider
        NewCombo = 4,         // bit 2 = new combo
        Spinner = 8,          // bit 3 = spinner
        ColourHax = 0x70,     // bits 4-6 (combo offset)
        Hold = 128,            // bit 7 = hold (mania)
    }

    /// <summary>
    /// 커브 타입 — osu! stable CurveTypes.
    /// </summary>
    public enum CurveTypes
    {
        Catmull,
        Bezier,
        Linear,
        PerfectCurve
    }

    /// <summary>
    /// 타이밍 포인트 — osu! stable ControlPoint.
    /// </summary>
    public class TimingPoint
    {
        public int Offset;           // ms
        public double BeatLength;    // 양수=박자간격(ms), 음수=슬라이더 속도 배율
        public int TimeSignature;    // 4=4/4박자
        public int SampleSet;        // 0=Normal, 1=Soft, 2=Drum
        public int CustomSampleSet;  // 0=Default
        public int Volume;           // 0~100
        public bool TimingChange;    // true=새 BPM 타이밍 포인트
        public bool Kiai;             // 키아이
        public bool OmitFirstBarline;

        public double BPM
        {
            get { return BeatLength > 0 ? 60000.0 / BeatLength : 0; }
        }
    }

    /// <summary>
    /// 비트맵 데이터 — .osu 파일에서 파싱된 정보.
    /// </summary>
    public class BeatmapData
    {
        // [General]
        public string AudioFilename;
        public int AudioLeadIn;
        public int PreviewTime;
        public int Mode;              // 0=Osu, 1=Taiko, 2=Catch, 3=Mania
        public float StackLeniency = 0.4f;

        // [Metadata]
        public string Title;
        public string Artist;
        public string Creator;
        public string Version;        // 난이도명
        public int BeatmapID;
        public int BeatmapSetID;

        // [Difficulty]
        public float HPDrainRate = 5;
        public float CircleSize = 5;
        public float OverallDifficulty = 5;
        public float ApproachRate = 5;
        public float SliderMultiplier = 1.4f;
        public float SliderTickRate = 1;

        // [TimingPoints]
        public List<TimingPoint> TimingPoints = new List<TimingPoint>();

        // [Colours]
        public List<System.Drawing.Color> ComboColours = new List<System.Drawing.Color>();

        // [HitObjects]
        public List<HitObjectData> HitObjects = new List<HitObjectData>();

        // VersionOffset (구버전 맵 보정)
        public int VersionOffset;

        // BeatmapVersion — osu file format vN
        public int BeatmapVersion = 14;

        // ApproachRate가 명시적으로 설정되었는지
        public bool HasApproachRate = false;
    }

    /// <summary>
    /// HitObject 파싱 데이터 — .osu 파일에서 파싱된 정보.
    /// </summary>
    public class HitObjectData
    {
        public Vector2 Position;
        public Vector2 BasePosition;
        public int StartTime;
        public int EndTime;
        public HitObjectType Type;
        public int SoundType;
        public bool NewCombo;
        public int ComboOffset;

        // Slider 전용
        public CurveTypes CurveType;
        public List<Vector2> CurvePoints = new List<Vector2>();
        public int RepeatCount;
        public double Length;          // SpatialLength (픽셀)

        // Combo 정보 (런타임)
        public int ComboNumber;
        public int ComboColourIndex;
        public System.Drawing.Color Colour;

        // 스택 계산 — osu! stable HitObject.StackCount
        public int StackCount;

        // 스택 계산용 끝 위치 — osu! stable HitObject.BaseEndPosition
        // Circle: Position, Slider: EndPosition (스택 적용 전)
        public Vector2 BaseEndPosition;
    }
}