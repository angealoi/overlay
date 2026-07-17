using System;
using System.Collections.Generic;
using System.IO;
using OpenTK;
using System.Drawing;

namespace OsuEnlightenOverlay.Gameplay.Beatmap
{
    /// <summary>
    /// .osu 파일 파서 — osu! stable HitObjectManager_LoadSave.cs parse() 포팅.
    /// </summary>
    internal class BeatmapParser
    {
        /// <summary>
        /// .osu 파일 파싱.
        /// </summary>
        public static BeatmapData Parse(string filepath, bool verticalFlip)
        {
            BeatmapData data = new BeatmapData();

            using (StreamReader reader = new StreamReader(filepath, System.Text.Encoding.UTF8))
            {
                string currentSection = "";

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();

                    if (line == null) break;
                    if (line.Length == 0) continue;
                    if (line.StartsWith(" ") || line.StartsWith("//") || line.StartsWith("_")) continue;

                    // osu file format vN 파싱
                    if (line.StartsWith("osu file format v"))
                    {
                        string verStr = line.Substring("osu file format v".Length);
                        int ver;
                        if (int.TryParse(verStr, out ver))
                        {
                            data.BeatmapVersion = ver;
                            // 구버전 타이밍 보정 — osu! stable: version < 5 → offset += 24
                            if (ver < 5) data.VersionOffset = 24;
                        }
                        continue;
                    }

                    // 섹션 헤더
                    if (line.StartsWith("["))
                    {
                        currentSection = line.Trim('[', ']');
                        continue;
                    }

                    // 섹션별 파싱
                    switch (currentSection)
                    {
                        case "General":
                            ParseGeneral(line, data);
                            break;
                        case "Metadata":
                            ParseMetadata(line, data);
                            break;
                        case "Difficulty":
                            ParseDifficulty(line, data);
                            break;
                        case "TimingPoints":
                            ParseTimingPoint(line, data);
                            break;
                        case "Events":
                            ParseEvent(line, data);
                            break;
                        case "Colours":
                            ParseColour(line, data);
                            break;
                        case "HitObjects":
                            ParseHitObject(line, data, verticalFlip);
                            break;
                    }
                }
            }

            // AR 기본값 처리 — osu! stable: AR이 명시되지 않았으면 OD 사용
            if (!data.HasApproachRate)
                data.ApproachRate = data.OverallDifficulty;

            // 콤보 할당의 브레이크 순회가 오름차순을 전제 — stable은 EventManager.Add마다 Sort
            data.Breaks.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            return data;
        }

        static void ParseGeneral(string line, BeatmapData data)
        {
            string[] parts = line.Split(new[] { ':' }, 2);
            if (parts.Length < 2) return;
            string key = parts[0].Trim();
            string val = parts[1].Trim();
            int ival; float fval;

            switch (key)
            {
                case "AudioFilename": data.AudioFilename = val; break;
                case "AudioLeadIn": if (int.TryParse(val, out ival)) data.AudioLeadIn = ival; break;
                case "PreviewTime": if (int.TryParse(val, out ival)) data.PreviewTime = ival; break;
                case "Mode": if (int.TryParse(val, out ival)) data.Mode = ival; break;
                case "StackLeniency": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fval)) data.StackLeniency = fval; break;
            }
        }

        static void ParseMetadata(string line, BeatmapData data)
        {
            string[] parts = line.Split(new[] { ':' }, 2);
            if (parts.Length < 2) return;
            string key = parts[0].Trim();
            string val = parts[1].Trim();
            int ival;

            switch (key)
            {
                case "Title": data.Title = val; break;
                case "Artist": data.Artist = val; break;
                case "Creator": data.Creator = val; break;
                case "Version": data.Version = val; break;
                case "BeatmapID": if (int.TryParse(val, out ival)) data.BeatmapID = ival; break;
                case "BeatmapSetID": if (int.TryParse(val, out ival)) data.BeatmapSetID = ival; break;
            }
        }

        static void ParseDifficulty(string line, BeatmapData data)
        {
            string[] parts = line.Split(new[] { ':' }, 2);
            if (parts.Length < 2) return;
            string key = parts[0].Trim();
            string val = parts[1].Trim();
            float fval;

            switch (key)
            {
                case "HPDrainRate": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fval)) data.HPDrainRate = fval; break;
                case "CircleSize": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fval)) data.CircleSize = fval; break;
                case "OverallDifficulty": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fval)) data.OverallDifficulty = fval; break;
                case "ApproachRate": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fval)) { data.ApproachRate = fval; data.HasApproachRate = true; } break;
                case "SliderMultiplier": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fval)) data.SliderMultiplier = fval; break;
                case "SliderTickRate": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fval)) data.SliderTickRate = fval; break;
            }
        }

        static void ParseTimingPoint(string line, BeatmapData data)
        {
            string[] parts = line.Split(',');
            if (parts.Length < 2) return;

            TimingPoint tp = new TimingPoint();
            int ival; double dval;

            // Offset — 소수점 가능 (osu! stable: double)
            double offsetVal;
            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out offsetVal)) return;
            tp.Offset = (int)offsetVal + data.VersionOffset;

            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dval)) return;
            tp.BeatLength = dval;

            if (parts.Length > 2 && int.TryParse(parts[2], out ival)) tp.TimeSignature = ival;
            else tp.TimeSignature = 4;

            if (parts.Length > 3 && int.TryParse(parts[3], out ival)) tp.SampleSet = ival;
            if (parts.Length > 4 && int.TryParse(parts[4], out ival)) tp.CustomSampleSet = ival;
            if (parts.Length > 5 && int.TryParse(parts[5], out ival)) tp.Volume = ival;
            if (parts.Length > 6) tp.TimingChange = parts[6] == "1";

            if (parts.Length > 7 && int.TryParse(parts[7], out ival))
            {
                tp.Kiai = (ival & 1) != 0;
                tp.OmitFirstBarline = (ival & 8) != 0;
            }

            // 구버전 (2필드): offset,beatLength 만 있으면 기본값
            if (parts.Length <= 2)
            {
                tp.TimeSignature = 4;
                tp.TimingChange = tp.BeatLength > 0;
            }

            data.TimingPoints.Add(tp);
        }

        /// <summary>
        /// [Events] 파싱 — 브레이크만. 스토리보드/배경/샘플은 오버레이가 쓰지 않으므로 무시.
        /// osu! stable HitObjectManager_LoadSave.cs:467-478.
        /// </summary>
        static void ParseEvent(string line, BeatmapData data)
        {
            string[] split = line.Trim().Split(',');
            if (split.Length < 3) return;

            // stable은 Enum.Parse라 숫자("2")와 이름("Break")을 모두 받는다. 실제 맵은 "2"만 쓴다.
            string type = split[0].Trim();
            if (type != "2" && type != "Break") return;

            int start, end;
            if (!int.TryParse(split[1].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out start)) return;
            if (!int.TryParse(split[2].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out end)) return;

            EventBreak br = new EventBreak();
            br.StartTime = start + data.VersionOffset;
            br.EndTime = end + data.VersionOffset;

            // 너무 짧은 브레이크는 stable이 아예 등록하지 않는다 (EventManager.Add 호출 전 필터)
            if (br.Length < EventBreak.MIN_BREAK_LENGTH) return;

            data.Breaks.Add(br);
        }

        static void ParseColour(string line, BeatmapData data)
        {
            string[] parts = line.Split(new[] { ':' }, 2);
            if (parts.Length < 2) return;
            string key = parts[0].Trim();
            string val = parts[1].Trim();

            if (key.StartsWith("Combo"))
            {
                string[] rgb = val.Split(',');
                if (rgb.Length >= 3)
                {
                    int r, g, b;
                    if (int.TryParse(rgb[0].Trim(), out r) &&
                        int.TryParse(rgb[1].Trim(), out g) &&
                        int.TryParse(rgb[2].Trim(), out b))
                    {
                        // 범위 밖 값(예: `Combo1: 300,0,0`)은 Color.FromArgb에서 ArgumentException을
                        // 던져 파싱 Task가 통째로 죽고 그 맵이 조용히 로드 실패한다 (A5). [0,255]로 클램프.
                        data.ComboColours.Add(Color.FromArgb(
                            Math.Max(0, Math.Min(255, r)),
                            Math.Max(0, Math.Min(255, g)),
                            Math.Max(0, Math.Min(255, b))));
                    }
                }
            }
        }

        static void ParseHitObject(string line, BeatmapData data, bool verticalFlip)
        {
            string[] split = line.Trim().Split(',');
            if (split.Length < 4) return;

            double dval; int ival;

            if (!double.TryParse(split[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dval)) return;
            int x = (int)Math.Max(0, Math.Min(512, dval));

            if (!double.TryParse(split[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dval)) return;
            int y = (int)Math.Max(0, Math.Min(512, dval));
            if (verticalFlip) y = 384 - y;

            Vector2 pos = new Vector2(x, y);

            if (!double.TryParse(split[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dval)) return;
            int time = (int)dval + data.VersionOffset;

            if (!int.TryParse(split[3], out ival)) return;
            int typeRaw = ival;
            HitObjectType type = (HitObjectType)(typeRaw & ~(int)HitObjectType.ColourHax);

            int comboOffset = (typeRaw >> 4) & 7;

            int soundType = 0;
            if (split.Length > 4 && int.TryParse(split[4], out ival))
                soundType = ival;

            HitObjectData h = new HitObjectData();
            h.Position = pos;
            h.BasePosition = pos;
            h.StartTime = time;
            h.EndTime = time;
            h.Type = type;
            h.SoundType = soundType;
            h.ComboOffset = comboOffset;

            if ((type & HitObjectType.Normal) != 0)
            {
                // Circle — 추가 필드 없음 (sample info만)
                h.EndTime = time;
            }
            else if ((type & HitObjectType.Slider) != 0)
            {
                // Slider
                if (split.Length > 5)
                {
                    string[] pointsplit = split[5].Split('|');
                    for (int i = 0; i < pointsplit.Length; i++)
                    {
                        if (pointsplit[i].Length == 1)
                        {
                            switch (pointsplit[i])
                            {
                                case "C": h.CurveType = CurveTypes.Catmull; break;
                                case "B": h.CurveType = CurveTypes.Bezier; break;
                                case "L": h.CurveType = CurveTypes.Linear; break;
                                case "P": h.CurveType = CurveTypes.PerfectCurve; break;
                            }
                            continue;
                        }

                        string[] temp = pointsplit[i].Split(':');
                        if (temp.Length < 2) continue;
                        double px, py;
                        if (!double.TryParse(temp[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out px)) continue;
                        if (!double.TryParse(temp[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out py)) continue;
                        if (verticalFlip) py = 384 - py;
                        h.CurvePoints.Add(new Vector2((int)px, (int)py));
                    }
                }

                if (split.Length > 6 && int.TryParse(split[6], out ival))
                    h.RepeatCount = ival;

                if (split.Length > 7 && double.TryParse(split[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dval))
                    h.Length = dval;
            }
            else if ((type & HitObjectType.Spinner) != 0)
            {
                // Spinner
                if (split.Length > 5 && int.TryParse(split[5], out ival))
                    h.EndTime = ival + data.VersionOffset;
            }

            data.HitObjects.Add(h);
        }

        /// <summary>
        /// 특정 시간의 박자 간격 조회 (슬라이더 길이 계산용).
        /// </summary>
        public static double BeatLengthAt(BeatmapData data, int time)
        {
            // osu! stable Beatmap.BeatLengthAt 정확 포팅
            // ControlPoints[i].Offset <= time인 모든 포인트 순회
            // 마지막 TimingChange 포인트의 BeatLength 사용
            // 음수 BeatLength (상속 포인트)는 BpmMultiplier 적용
            if (data.TimingPoints == null || data.TimingPoints.Count == 0)
                return 0;

            int point = 0;
            int samplePoint = 0;

            for (int i = 0; i < data.TimingPoints.Count; i++)
            {
                if (data.TimingPoints[i].Offset <= time)
                {
                    if (data.TimingPoints[i].TimingChange)
                        point = i;
                    else
                        samplePoint = i;
                }
            }

            double mult = 1;

            // 상속 포인트가 타이밍 포인트보다 뒤에 있고 BeatLength < 0이면 multiplier 적용
            if (samplePoint > point && data.TimingPoints[samplePoint].BeatLength < 0)
            {
                // osu! stable: ControlPoint.BpmMultiplier
                // BpmMultiplier = Math.Max(0.1, BeatLength / -100)
                double beatLength = data.TimingPoints[samplePoint].BeatLength;
                mult = Math.Max(0.1, beatLength / -100.0);
            }

            return data.TimingPoints[point].BeatLength * mult;
        }

        /// <summary>
        /// BpmMultiplierAt — osu! stable Beatmap.BpmMultiplierAt 포팅.
        /// 해당 시간의 상속 포인트 BpmMultiplier 반환.
        /// </summary>
        public static double BpmMultiplierAt(BeatmapData data, int time)
        {
            if (data.TimingPoints == null || data.TimingPoints.Count == 0)
                return 1.0;

            int point = 0;
            int samplePoint = 0;

            for (int i = 0; i < data.TimingPoints.Count; i++)
            {
                if (data.TimingPoints[i].Offset <= time)
                {
                    if (data.TimingPoints[i].TimingChange)
                        point = i;
                    else
                        samplePoint = i;
                }
            }

            if (samplePoint > point && data.TimingPoints[samplePoint].BeatLength < 0)
            {
                double beatLength = data.TimingPoints[samplePoint].BeatLength;
                return Math.Max(0.1, beatLength / -100.0);
            }

            return 1.0;
        }
    }
}