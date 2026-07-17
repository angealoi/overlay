using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace OsuEnlightenOverlay.Skinning
{
    /// <summary>
    /// 스킨 소스 — osu! stable SkinSource enum.
    /// 우선순위: Beatmap > Skin > Osu
    /// </summary>
    [Flags]
    public enum SkinSource
    {
        None = 0,
        Osu = 1,
        Skin = 2,
        Beatmap = 4,
        Temporal = 8,
        ExceptBeatmap = Osu | Skin,
        All = Osu | Skin | Beatmap
    }

    /// <summary>
    /// 슬라이더 스타일 — osu! stable SliderStyle enum.
    /// </summary>
    public enum SliderStyle
    {
        MmSliders = 2
    }

    /// <summary>
    /// 스킨 설정 — osu! stable SkinOsu.cs + Skin.cs + Section.cs 정확 포팅.
    /// skin.ini [General], [Colours], [Fonts] 섹션.
    /// </summary>
    public class SkinOsu
    {
        // ── originalColours — osu! stable SkinOsu.originalColours ──
        // skin.ini에 없으면 이 기본값 사용. Combo5는 투명(0,0,0,0).
        // stable은 Combo1~5만 담는다(SkinOsu.cs:14-29). Combo6~8은 stable에 없는 잉여값이라 제거함(I-감사 #27).
        private static Dictionary<string, Color> originalColours = new Dictionary<string, Color>()
        {
            { "Combo1",                Color.FromArgb(255, 192, 0) },
            { "Combo2",                Color.FromArgb(0, 202, 0) },
            { "Combo3",                Color.FromArgb(18, 124, 255) },
            { "Combo4",                Color.FromArgb(242, 24, 57) },
            { "Combo5",                Color.FromArgb(0, 0, 0, 0) },
            { "MenuGlow",              Color.FromArgb(128, 128, 160) },
            { "SliderBall",            Color.FromArgb(2, 170, 255) },
            { "SliderBorder",          Color.FromArgb(255, 255, 255) },
            { "SpinnerApproachCircle", Color.FromArgb(77, 139, 217) },
            { "SongSelectActiveText",  Color.Black },
            { "SongSelectInactiveText",Color.White },
            { "StarBreakAdditive",     Color.LightPink },
            { "InputOverlayText",      Color.Black },
        };

        // Colours — originalColours로 초기화, skin.ini에서 override
        public Dictionary<string, Color> Colours = new Dictionary<string, Color>(originalColours);

        // ── [General] 필드 — 기본값은 osu! stable과 정확히 일치 ──
        public string SkinName = "Unknown";
        public string SkinAuthor = "";
        public bool CursorCentre = true;
        public bool CursorExpand = true;
        public bool CursorRotate = true;
        public bool SliderBallFlip = false;
        public int SliderBallFrames = 10;
        public bool OverlayAboveNumber = true;
        public bool SpinnerFrequencyModulate = true;
        public bool LayeredHitSounds = true;
        public bool SpinnerFadePlayfield = false; // !UseNewLayout → false (UseNewLayout=true이므로)
        public bool SpinnerNoBlink = false;
        public int AnimationFramerate = -1;
        public bool CursorTrailRotate = false;
        public bool AllowSliderBallTint = false;
        public bool ComboBurstRandom = false;
        public SliderStyle SliderStyle = SliderStyle.MmSliders;
        public double Version = 1;

        // ── [Fonts] 필드 ──
        public string FontHitCircle = "default";
        public int FontHitCircleOverlap = -2;
        public string FontScore = "score";
        public string FontCombo = "score";
        public int FontScoreOverlap = 0;
        public int FontComboOverlap = 0;

        public string RawName;
        public string FullPath;

        public const double SKIN_VERSION = 2.7;

        /// <summary>
        /// UseNewLayout — Version > 1 또는 Default/User 스킨이면 true.
        /// </summary>
        public bool UseNewLayout
        {
            get { return Version > 1 || RawName == "Default" || RawName == "User"; }
        }

        // ── skin.ini 파싱 상태 ──
        private Dictionary<string, Dictionary<string, string>> sections = new Dictionary<string, Dictionary<string, string>>();
        private Dictionary<string, string> currentSection;

        /// <summary>
        /// skin.ini 로드 — osu! stable Skin.Load + SkinOsu.Read 정확 포팅.
        /// </summary>
        public void Load(string iniFilename)
        {
            if (!File.Exists(iniFilename))
            {
                Version = SKIN_VERSION;
                return;
            }

            // 파일 읽기 — 섹션별로 분류
            string sectionName = "General";
            sections.Clear();

            using (StreamReader sr = new StreamReader(iniFilename))
            {
                while (sr.Peek() != -1)
                {
                    string line = sr.ReadLine();
                    if (line == null) break;
                    line = line.Trim();

                    if (line.Length == 0 || line.StartsWith("//")) continue;

                    if (line.StartsWith("["))
                    {
                        sectionName = line.Trim('[', ']');
                        if (!sections.ContainsKey(sectionName))
                            sections[sectionName] = new Dictionary<string, string>();
                        continue;
                    }

                    if (!sections.ContainsKey(sectionName))
                        sections[sectionName] = new Dictionary<string, string>();

                    // key:value 분리
                    int colonIdx = line.IndexOf(':');
                    if (colonIdx < 0) continue;
                    string key = line.Substring(0, colonIdx).Trim();
                    string val = colonIdx + 1 < line.Length ? line.Substring(colonIdx + 1).Trim() : "";

                    // osu! stable: 인라인 주석 제거 — "255,255,255 // #ffffff" → "255,255,255"
                    int commentIdx = val.IndexOf("//");
                    if (commentIdx >= 0)
                        val = val.Substring(0, commentIdx).Trim();

                    sections[sectionName][key] = val;
                }
            }

            Read();
        }

        /// <summary>
        /// Read — osu! stable SkinOsu.Read 정확 포팅.
        /// </summary>
        private void Read()
        {
            // ── [General] ──
            if (sections.TryGetValue("General", out currentSection))
            {
                ReadString("Name", ref SkinName);
                ReadString("Author", ref SkinAuthor);
                ReadBool("SliderBallFlip", ref SliderBallFlip);
                ReadBool("SliderBallDontRotate", ref SliderBallFlip); // 오타 대응
                ReadBool("CursorRotate", ref CursorRotate);
                ReadBool("CursorExpand", ref CursorExpand);
                ReadBool("CursorCentre", ref CursorCentre);
                ReadInt("SliderBallFrames", ref SliderBallFrames);
                ReadBool("HitCircleOverlayAboveNumber", ref OverlayAboveNumber);
                ReadBool("HitCircleOverlayAboveNumer", ref OverlayAboveNumber); // 오타 대응
                ReadBool("SpinnerFrequencyModulate", ref SpinnerFrequencyModulate);
                ReadBool("LayeredHitSounds", ref LayeredHitSounds);
                ReadBool("SpinnerFadePlayfield", ref SpinnerFadePlayfield);
                ReadBool("SpinnerNoBlink", ref SpinnerNoBlink);
                ReadBool("AllowSliderBallTint", ref AllowSliderBallTint);
                ReadInt("AnimationFramerate", ref AnimationFramerate);
                ReadBool("CursorTrailRotate", ref CursorTrailRotate);
                ReadBool("ComboBurstRandom", ref ComboBurstRandom);

                // SliderStyle — enum 파싱
                string sliderStyleStr = "";
                if (ReadString("SliderStyle", ref sliderStyleStr))
                {
                    int styleVal;
                    if (int.TryParse(sliderStyleStr, out styleVal))
                        SliderStyle = (SliderStyle)styleVal;
                }

                // Version — "latest" → SKIN_VERSION, 숫자 → 해당 버전
                string versionStr = "";
                if (ReadString("Version", ref versionStr))
                {
                    if (versionStr == "latest")
                        Version = SKIN_VERSION;
                    else
                        double.TryParse(versionStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out Version);
                }
            }

            // ── [Colours] ──
            // osu! stable: Colours = FindAll<Color>()
            // 그 후 누락된 기본값 채우기 (skinHasComboColours 체크)
            if (sections.TryGetValue("Colours", out currentSection))
            {
                Dictionary<string, Color> skinColours = new Dictionary<string, Color>();

                // FindAll<Color> — 현재 섹션의 모든 속성을 Color로 변환
                // osu! stable: AllowTransparentColours = false → alpha 강제 255
                foreach (var kv in currentSection)
                {
                    Color c = ParseColor(kv.Value, allowAlpha: false);
                    if (c != Color.Empty)
                        skinColours[kv.Key] = c;
                }

                // 디버그: 파싱된 색상 키 출력
                // Console.WriteLine("[Skin] [Colours] parsed " + skinColours.Count + " colours:");
                // foreach (var kv in skinColours)
                //     Console.WriteLine("[Skin]   " + kv.Key + " = " + kv.Value);

                // skinHasComboColours — Combo1이 있으면 true
                bool skinHasComboColours = skinColours.ContainsKey("Combo1");

                // 누락된 기본값 채우기
                // osu! stable: skinHasComboColours가 true이면 누락된 Combo는 채우지 않음
                foreach (var kvp in originalColours)
                {
                    if (skinColours.ContainsKey(kvp.Key)) continue;
                    if (skinHasComboColours && kvp.Key.StartsWith("Combo")) continue;
                    skinColours[kvp.Key] = kvp.Value;
                }

                Colours = skinColours;
            }

            // ── [Fonts] ──
            if (sections.TryGetValue("Fonts", out currentSection))
            {
                ReadString("HitCirclePrefix", ref FontHitCircle);
                ReadInt("HitCircleOverlap", ref FontHitCircleOverlap);
                ReadString("ScorePrefix", ref FontScore);
                ReadString("ComboPrefix", ref FontCombo);
                ReadInt("ScoreOverlap", ref FontScoreOverlap);
                ReadInt("ComboOverlap", ref FontComboOverlap);
            }
        }

        // ── ReadValue 헬퍼 — osu! stable Section.TryGetValue + ConvertString 포팅 ──

        private bool ReadBool(string key, ref bool dest)
        {
            string val;
            if (currentSection == null || !currentSection.TryGetValue(key, out val)) return false;

            // osu! stable: bool.TryParse 실패 시 Convert.ToInt32(value) → 0=false, 1=true
            bool boolTemp;
            if (bool.TryParse(val, out boolTemp))
            {
                dest = boolTemp;
                return true;
            }
            int intTemp;
            if (int.TryParse(val, out intTemp))
            {
                dest = intTemp != 0;
                return true;
            }
            return false;
        }

        private bool ReadInt(string key, ref int dest)
        {
            string val;
            if (currentSection == null || !currentSection.TryGetValue(key, out val)) return false;
            return int.TryParse(val, out dest);
        }

        private bool ReadString(string key, ref string dest)
        {
            string val;
            if (currentSection == null || !currentSection.TryGetValue(key, out val)) return false;
            dest = val;
            return true;
        }

        /// <summary>
        /// Color 변환 — osu! stable Section.ConvertString 포팅.
        /// split.Length == 3 → RGB (alpha=255)
        /// split.Length == 4 → RGBA (allowAlpha일 때만 alpha 적용, 아니면 255)
        /// </summary>
        private static Color ParseColor(string value, bool allowAlpha)
        {
            string[] split = value.Split(',');
            try
            {
                if (split.Length == 3)
                    return Color.FromArgb(
                        int.Parse(split[0].Trim()),
                        int.Parse(split[1].Trim()),
                        int.Parse(split[2].Trim()));
                if (split.Length == 4)
                    return Color.FromArgb(
                        allowAlpha ? int.Parse(split[3].Trim()) : 255,
                        int.Parse(split[0].Trim()),
                        int.Parse(split[1].Trim()),
                        int.Parse(split[2].Trim()));
            }
            catch { }
            return Color.Empty;
        }
    }

    /// <summary>
    /// 스킨 관리자 — osu! stable SkinManager.cs 포팅.
    /// </summary>
    public static class SkinManager
    {
        public static SkinOsu Current;
        public static SkinOsu CurrentUserSkin;
        public static bool IsDefault { get { return Current != null && Current.RawName == "Default"; } }

        // 기본 combo 색상 — osu! stable SkinManager.DefaultColours
        public static Color[] DefaultColours = new Color[]
        {
            Color.FromArgb(255, 192, 0),
            Color.FromArgb(0, 202, 0),
            Color.FromArgb(18, 124, 255),
            Color.FromArgb(242, 24, 57),
            Color.FromArgb(0, 0, 0, 0),
            Color.FromArgb(0, 0, 0, 0),
            Color.FromArgb(0, 0, 0, 0),
            Color.FromArgb(0, 0, 0, 0),
        };

        public const int MAX_COLOUR_COUNT = 8;

        /// <summary>
        /// 스킨 로드 — osu! stable SkinManager.LoadSkinRaw 포팅.
        /// </summary>
        public static void LoadSkin(string skinName, string skinsFolder)
        {
            SkinOsu skin = new SkinOsu();
            skin.RawName = skinName;

            // skinsFolder가 null이면(osu! 설치 경로 미해결) 디스크 스킨을 못 읽는다 —
            // Path.Combine(null, ...)이 ArgumentNullException을 던져 기동이 막히던 것을 막고
            // 임베디드 기본 스킨으로 진행한다 (A1: OverlayForm.OnLoad가 Show() 안에서 돈다).
            skin.FullPath = skinsFolder != null ? Path.Combine(skinsFolder, skinName) : null;

            string iniFilename = skin.FullPath != null ? Path.Combine(skin.FullPath, "skin.ini") : null;
            bool hasIni = iniFilename != null && File.Exists(iniFilename);
            bool isDefault = skinName == "Default";

            Console.WriteLine("[Skin] LoadSkin: name=" + skinName + " folder=" + skin.FullPath + " ini=" + hasIni);

            // osu! stable: isDefault || skinName == "User" || !hasIni → Version = SKIN_VERSION
            if (isDefault || skinName == "User" || !hasIni)
                skin.Version = SkinOsu.SKIN_VERSION;

            if (isDefault)
            {
                skin.SkinName = "osu!";
                skin.SkinAuthor = "peppy";
            }
            else if (hasIni)
            {
                skin.Load(iniFilename);
            }

            Current = skin;
            if (CurrentUserSkin == null)
                CurrentUserSkin = Current;
        }

        /// <summary>
        /// 기본 스킨 로드.
        /// </summary>
        public static void LoadDefaultSkin(string defaultSkinFolder)
        {
            LoadSkin("Default", defaultSkinFolder);
        }

        /// <summary>
        /// 색상 조회 — osu! stable SkinManager.LoadColour 포팅.
        /// 우선순위: Skin Colours > TransparentWhite
        /// </summary>
        public static Color LoadColour(string name)
        {
            Color ret;
            if (Current != null && Current.Colours.TryGetValue(name, out ret) && ret.A > 0)
                return ret;
            return Color.FromArgb(0, 255, 255, 255); // TransparentWhite
        }

        /// <summary>
        /// Combo 색상 리스트 조회.
        /// 우선순위: Skin Colours > DefaultColours
        /// </summary>
        public static List<Color> GetComboColours()
        {
            List<Color> result = new List<Color>();

            if (Current != null)
            {
                for (int i = 1; i <= MAX_COLOUR_COUNT; i++)
                {
                    Color c;
                    if (Current.Colours.TryGetValue("Combo" + i, out c) && c.A > 0)
                        result.Add(c);
                }
            }

            if (result.Count == 0)
            {
                for (int i = 0; i < 4; i++)
                    result.Add(DefaultColours[i]);
            }

            return result;
        }

        /// <summary>
        /// UseNewLayout — Version > 1 또는 Default/User 스킨이면 true.
        /// </summary>
        public static bool UseNewLayout
        {
            get { return Current == null || Current.UseNewLayout; }
        }
    }
}