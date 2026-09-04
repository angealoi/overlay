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

            // lazer LegacyBeatmapDecoder.applyDifficultyRestrictions — 난이도 값 클램프.
            // Aspire 맵은 SliderMultiplier/SliderTickRate에 극단값을 넣는데, 클램프 없이 쓰면
            // velocity/tickDistance가 발산해 슬라이더가 깨진다.
            data.HPDrainRate = Math.Max(0, Math.Min(10, data.HPDrainRate));
            data.CircleSize = Math.Max(0, Math.Min(10, data.CircleSize));
            data.OverallDifficulty = Math.Max(0, Math.Min(10, data.OverallDifficulty));
            data.ApproachRate = Math.Max(0, Math.Min(10, data.ApproachRate));
            data.SliderMultiplier = Math.Max(0.4f, Math.Min(3.6f, data.SliderMultiplier));
            data.SliderTickRate = Math.Max(0.5f, Math.Min(8f, data.SliderTickRate));

            // 콤보 할당의 브레이크 순회가 오름차순을 전제 — stable은 EventManager.Add마다 Sort
            data.Breaks.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            // lazer LegacyBeatmapDecoder: 랭크 맵 중 HitObjects가 시간순이 아닌 경우가 있다
            // (이 Aspire 맵 https://osu.ppy.sh/s/594828 이 그 예). 파일 앞쪽에 4:10 슬라이더가
            // 있으면 콤보/스택/HOM 교차검증이 전부 어긋난다. 같은 StartTime은 파일 순서를 유지.
            SortHitObjectsStable(data.HitObjects);

            // osu! stable AudioEngine.UpdateActiveTimingPoint → ControlPoints.Sort().
            // ControlPoint.CompareTo: Offset 오름차순, 같은 Offset이면 uninherited(TimingChange) 먼저.
            // 이 맵은 타이밍 포인트도 시간순이 아니다. 정렬하지 않으면 파일 뒤쪽의 1E-298/-1638400이
            // 04:13 이후 BeatLengthAt을 덮어 슬라이더가 실제 osu!보다 훨씬 길어진다.
            SortTimingPointsStable(data.TimingPoints);

            return data;
        }

        static void SortHitObjectsStable(List<HitObjectData> list)
        {
            int n = list.Count;
            if (n <= 1) return;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (i, j) =>
            {
                int c = list[i].StartTime.CompareTo(list[j].StartTime);
                return c != 0 ? c : i.CompareTo(j);
            });
            HitObjectData[] copy = new HitObjectData[n];
            for (int i = 0; i < n; i++) copy[i] = list[order[i]];
            for (int i = 0; i < n; i++) list[i] = copy[i];
        }

        static void SortTimingPointsStable(List<TimingPoint> list)
        {
            int n = list.Count;
            if (n <= 1) return;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (i, j) =>
            {
                int c = list[i].Offset.CompareTo(list[j].Offset);
                if (c != 0) return c;
                // other.TimingChange.CompareTo(this.TimingChange) → uninherited 먼저
                c = list[j].TimingChange.CompareTo(list[i].TimingChange);
                if (c != 0) return c;
                return i.CompareTo(j);
            });
            TimingPoint[] copy = new TimingPoint[n];
            for (int i = 0; i < n; i++) copy[i] = list[order[i]];
            for (int i = 0; i < n; i++) list[i] = copy[i];
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

                // 길이는 NaN/Infinity/음수를 거부한다 (C6 후속). double.TryParse는 "NaN"·"Infinity"
                // 문자열도 성공으로 파싱하는데, 그 값이 velocity/커브 길이 계산을 타고 슬라이더 볼·틱을
                // NaN 좌표로 만든다(GetBallPosition/PositionAtLength의 나눗셈 가드는 값이 NaN이면 못 거른다).
                // 유한·비음수만 받아들이고 그 외엔 기본값 0으로 둔다(zero-length는 하위 가드가 안전 처리).
                if (split.Length > 7 && double.TryParse(split[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dval)
                    && !double.IsNaN(dval) && !double.IsInfinity(dval) && dval >= 0)
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
                mult = BpmMultiplierOf(data.TimingPoints[samplePoint].BeatLength);

            return data.TimingPoints[point].BeatLength * mult;
        }

        /// <summary>
        /// osu! stable ControlPoint.BpmMultiplier 정확 포팅:
        ///   BeatLength >= 0 → 1;  아니면 Clamp(-BeatLength, 10, 1000) / 100  → [0.1, 10].
        /// 상한 클램프가 필수다. Aspire 맵은 상속 포인트에 -2147483648 같은 값을 넣는데(SV 트릭),
        /// 상한 없이 쓰면 배율이 2천만 배가 되어 tickDistance가 0에 수렴해 틱 while이 수백만 번 돌고
        /// (한 슬라이더에 7.8s, 스프라이트 274만 개, 힙 1.3GB — 실측), virtualEndTime이 수 시간 뒤로
        /// 밀려 짧은 슬라이더의 볼이 영영 사라지지 않는다. stable/lazer 모두 SV를 [0.1,10]으로 제한한다.
        /// </summary>
        static double BpmMultiplierOf(double inheritedBeatLength)
        {
            if (inheritedBeatLength >= 0) return 1.0;
            double v = -inheritedBeatLength;
            if (v < 10) v = 10;
            if (v > 1000) v = 1000;
            return v / 100.0;
        }

        /// <summary>
        /// lazer식 SV 배율 — LegacyBeatmapDecoder: speedMultiplier = beatLength &lt; 0 ? 100/-beatLength : 1,
        /// Slider.SliderVelocityMultiplierBindable [0.1, 10] 클램프.
        /// stable BpmMultiplierOf와 수학적으로 동치(역수 관계)지만 lazer가 쓰는 정규 형태다.
        /// </summary>
        public static double SliderVelocityOf(double inheritedBeatLength)
        {
            if (inheritedBeatLength >= 0) return 1.0;
            if (double.IsNaN(inheritedBeatLength)) return 1.0;
            double sv = 100.0 / -inheritedBeatLength;
            if (sv < 0.1) sv = 0.1;
            if (sv > 10) sv = 10;
            return sv;
        }

        /// <summary>
        /// 해당 시간에 적용되는 lazer식 SV 배율. 활성 상속 포인트가 없으면 1.0.
        /// </summary>
        public static double SliderVelocityAt(BeatmapData data, int time)
        {
            if (data.TimingPoints == null || data.TimingPoints.Count == 0) return 1.0;

            int point = 0, samplePoint = 0;
            for (int i = 0; i < data.TimingPoints.Count; i++)
            {
                if (data.TimingPoints[i].Offset <= time)
                {
                    if (data.TimingPoints[i].TimingChange) point = i;
                    else samplePoint = i;
                }
            }

            if (samplePoint > point && data.TimingPoints[samplePoint].BeatLength < 0)
                return SliderVelocityOf(data.TimingPoints[samplePoint].BeatLength);
            return 1.0;
        }

        /// <summary>
        /// 타이밍 포인트의 기본 BeatLength (SV 미적용). lazer TimingPointAt.
        /// </summary>
        public static double TimingBeatLengthAt(BeatmapData data, int time)
        {
            if (data.TimingPoints == null || data.TimingPoints.Count == 0) return 0;
            int point = 0;
            for (int i = 0; i < data.TimingPoints.Count; i++)
                if (data.TimingPoints[i].Offset <= time && data.TimingPoints[i].TimingChange)
                    point = i;
            return data.TimingPoints[point].BeatLength;
        }

        /// <summary>
        /// osu! stable HitObjectManager.SliderScoringPointDistance:
        ///   (100 * SliderMultiplier) / SliderTickRate
        /// </summary>
        public static double SliderScoringPointDistance(BeatmapData data)
        {
            float tickRate = data.SliderTickRate;
            if (tickRate <= 0) tickRate = 1;
            return (100.0 * data.SliderMultiplier) / tickRate;
        }

        /// <summary>
        /// osu! stable HitObjectManager.SliderVelocityAt — 픽셀/초.
        /// BeatLengthAt(SV 포함)으로 나눈다. lazer Velocity(px/ms)의 1000배.
        /// </summary>
        public static double SliderVelocityPxPerSecond(BeatmapData data, int time)
        {
            double beatLength = BeatLengthAt(data, time);
            double scoringPointDistance = SliderScoringPointDistance(data);
            float tickRate = data.SliderTickRate;
            if (tickRate <= 0) tickRate = 1;
            if (beatLength > 0)
                return scoringPointDistance * tickRate * (1000.0 / beatLength);
            return scoringPointDistance * tickRate;
        }

        /// <summary>
        /// osu! stable SliderOsu.VirtualEndTime:
        ///   Floor(SpatialLength * BeatLengthAt * SegmentCount * 0.01 / SliderMultiplier + StartTime)
        /// </summary>
        public static int SliderVirtualEndTime(HitObjectData h, BeatmapData beatmap)
        {
            int segmentCount = Math.Max(1, h.RepeatCount);
            double sm = beatmap.SliderMultiplier;
            if (sm <= 0) sm = 1.4;
            double beatLength = BeatLengthAt(beatmap, h.StartTime);
            // Aspire 타이밍 1E-298 → EndTime≈StartTime. 1E+298 → overflow.
            // Inf/NaN을 1px/s로 바꾸면 슬라이더가 수 시간 동안 남는다.
            if (!(beatLength > 0) || double.IsInfinity(beatLength) || double.IsNaN(beatLength))
                return h.StartTime;
            double raw = h.Length * beatLength * segmentCount * 0.01 / sm + h.StartTime;
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < h.StartTime)
                return h.StartTime;
            if (raw > int.MaxValue) return int.MaxValue;
            return (int)Math.Floor(raw);
        }

        /// <summary>
        /// osu! stable SliderOsu.UpdateCalculations tickDistance.
        /// v≥8: SliderScoringPointDistance / BpmMultiplierAt (시간 상수 틱).
        /// </summary>
        public static double SliderTickDistance(BeatmapData beatmap, int startTime, double spatialLength)
        {
            double tickDistance = SliderScoringPointDistance(beatmap);
            if (beatmap.BeatmapVersion >= 8)
            {
                double bpmMult = BpmMultiplierAt(beatmap, startTime);
                if (bpmMult > 0)
                    tickDistance /= bpmMult;
            }
            if (spatialLength > 0 && tickDistance > spatialLength)
                tickDistance = spatialLength;
            if (!(tickDistance > 0) || double.IsNaN(tickDistance) || double.IsInfinity(tickDistance))
                tickDistance = SliderScoringPointDistance(beatmap);
            return tickDistance;
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
                return BpmMultiplierOf(data.TimingPoints[samplePoint].BeatLength);

            return 1.0;
        }
    }
}