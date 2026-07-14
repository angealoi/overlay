using System;
using OsuEnlightenOverlay.Gameplay.Beatmap;

namespace OsuEnlightenOverlay.Gameplay.Difficulty
{
    /// <summary>
    /// 난이도 계산 — osu! stable HitObjectManager.UpdateVariables + MapDifficultyRange 포팅.
    /// AR/CS/OD 기반으로 PreEmpt/HitWindow/SpriteDisplaySize 등 계산.
    /// Difficulty Changer: AR/CS 오버라이드 + HR/EZ mod 적용.
    /// </summary>
    internal static class DifficultyCalculator
    {
        // 정적값 — osu! stable HitObjectManager.cs
        public const int FadeIn = 400;
        public const int FadeOut = 240;
        public const int HitFadeIn = 120;
        public const int HitFadeOut = 600;
        public const int PostEmpt = 500;
        public const int FollowLinePreEmpt = 800;

        /// <summary>
        /// 난이도 값들을 계산 — mod + Difficulty Changer 설정 적용.
        /// </summary>
        /// <param name="ar">최종 AR 값 (이미 mod + 설정 오버라이드 적용됨)</param>
        /// <param name="cs">최종 CS 값 (이미 mod + 설정 오버라이드 적용됨)</param>
        /// <param name="od">최종 OD 값 (이미 mod 적용됨)</param>
        /// <param name="speedMultiplier">DT=1.5, HT=0.75, nomod=1.0 — PreEmpt/HitWindow에 적용</param>
        /// <param name="scalePreEmpt">PreEmpt에 speed multiplier 적용 여부 (AR override 시 false)</param>
        public static DifficultyValues CalculateWithValues(double ar, double cs, double od,
            float gamefieldWidth, float gamefieldRatio,
            float speedMultiplier = 1.0f, bool scalePreEmpt = true)
        {
            DifficultyValues dv = new DifficultyValues();

            // AR → PreEmpt (MapDifficultyRange with hardRockFactor=1.4 already applied to ar)
            double preempt = MapDifficultyRangeRaw(ar, 1800, 1200, 450);
            if (scalePreEmpt)
                preempt = ApplySpeedMultiplierToTime(preempt, speedMultiplier);
            dv.PreEmpt = (int)preempt;

            // FadeIn — osu! stable: 400 * min(1, PreEmpt/450)
            dv.FadeIn = (int)(400.0 * Math.Min(1.0, preempt / 450.0));

            // OD → HitWindow (speed multiplier 항상 적용 — AR override와 무관)
            double hw50 = MapDifficultyRangeRaw(od, 200, 150, 100);
            double hw100 = MapDifficultyRangeRaw(od, 140, 100, 60);
            double hw300 = MapDifficultyRangeRaw(od, 80, 50, 20);
            hw50 = ApplySpeedMultiplierToTime(hw50, speedMultiplier);
            hw100 = ApplySpeedMultiplierToTime(hw100, speedMultiplier);
            hw300 = ApplySpeedMultiplierToTime(hw300, speedMultiplier);
            dv.HitWindow50 = Math.Max(1, (int)hw50);
            dv.HitWindow100 = Math.Max(1, (int)hw100);
            dv.HitWindow300 = Math.Max(1, (int)hw300);

            // OD → SpinnerRotationRatio (speed 미적용)
            dv.SpinnerRotationRatio = MapDifficultyRangeRaw(od, 3, 5, 7.5);

            // CS → SpriteDisplaySize (AdjustDifficulty with hardRockFactor=1.3 already applied to cs)
            dv.SpriteDisplaySize = (float)(gamefieldWidth / 8f * (1f - 0.7f * AdjustDifficultyRaw(cs)));

            // CS → HitObjectRadius
            dv.HitObjectRadius = dv.SpriteDisplaySize / 2f / gamefieldRatio * 1.00041f;

            // PreEmptSliderComplete
            dv.PreEmptSliderComplete = (int)(dv.PreEmpt * 2f / 3f);

            // StackOffset
            dv.StackOffset = dv.HitObjectRadius / 10f;

            return dv;
        }

        /// <summary>
        /// 기존 호환용 — nomod 비트맵 값으로 계산.
        /// </summary>
        public static DifficultyValues Calculate(BeatmapData beatmap, float gamefieldWidth, float gamefieldRatio)
        {
            return CalculateWithValues(beatmap.ApproachRate, beatmap.CircleSize, beatmap.OverallDifficulty,
                gamefieldWidth, gamefieldRatio);
        }

        /// <summary>
        /// osu! stable ApplyModsToDifficulty — EZ/HR mod를 난이도 값에 적용.
        /// EZ: difficulty / 2 (min 0)
        /// HR: difficulty * hardRockFactor (max 10)
        /// </summary>
        public static double ApplyModsToDifficulty(double difficulty, double hardRockFactor, bool isEZ, bool isHR)
        {
            if (isEZ)
                difficulty = Math.Max(0, difficulty / 2);
            if (isHR)
                difficulty = Math.Min(10, difficulty * hardRockFactor);
            return difficulty;
        }

        /// <summary>
        /// DT/HT speed multiplier — osu! stable ModSpeedChange.
        /// DT/NC: 1.5, HT: 0.75, nomod: 1.0
        /// </summary>
        public static float GetSpeedMultiplier(bool isDT, bool isHT)
        {
            if (isDT) return 1.5f;
            if (isHT) return 0.75f;
            return 1.0f;
        }

        /// <summary>
        /// 시간값에 speed multiplier 적용 — osu! stable ApplySpeedMultiplierToTime.
        /// DT: time / 1.5 (빠르게 나타남), HT: time / 0.75 (느리게 나타남)
        /// </summary>
        public static double ApplySpeedMultiplierToTime(double timeMs, float speedMultiplier)
        {
            return timeMs / Math.Max(0.5f, speedMultiplier);
        }

        /// <summary>
        /// CS에 mods 적용 — osu! stable AdjustDifficulty.
        /// AdjustDifficulty = (ApplyModsToDifficulty(CS, 1.3) - 5) / 5
        /// 여기서는 이미 mod가 적용된 CS 값을 받아서 (CS-5)/5만 계산.
        /// </summary>
        public static double AdjustDifficultyRaw(double difficulty)
        {
            return (difficulty - 5) / 5;
        }

        /// <summary>
        /// 난이도→수치 매핑 — osu! stable MapDifficultyRange (mod 적용 후).
        /// difficulty > 5: mid+(max-mid)*(difficulty-5)/5
        /// difficulty < 5: mid-(mid-min)*(5-difficulty)/5
        /// </summary>
        public static double MapDifficultyRangeRaw(double difficulty, double min, double mid, double max)
        {
            if (difficulty > 5)
                return mid + (max - mid) * (difficulty - 5) / 5;
            if (difficulty < 5)
                return mid - (mid - min) * (5 - difficulty) / 5;
            return mid;
        }

        /// <summary>
        /// 수치→난이도 역매핑 — MapDifficultyRange의 역함수.
        /// DT/HT 적용 후 preempt로부터 effective AR 역산용.
        /// </summary>
        public static double MapDifficultyRangeInv(double value, double min, double mid, double max)
        {
            if (value < mid)   // d > 5
                return 5 + 5 * (value - mid) / (max - mid);
            if (value > mid)   // d < 5
                return 5 - 5 * (value - mid) / (min - mid);
            return 5;
        }

        // 기존 호환용 (nomod)
        public static double AdjustDifficulty(double difficulty)
        {
            return AdjustDifficultyRaw(difficulty);
        }

        public static double MapDifficultyRange(double difficulty, double min, double mid, double max)
        {
            return MapDifficultyRangeRaw(difficulty, min, mid, max);
        }
    }

    /// <summary>
    /// 계산된 난이도 값들.
    /// </summary>
    public class DifficultyValues
    {
        public int PreEmpt;              // AR 기반 — approach circle 표시 시간
        public int PreEmptSliderComplete; // PreEmpt * 2/3
        public int FadeIn;              // AR 기반 — fade in 시간 (400 * min(1, PreEmpt/450))
        public int HitWindow50;
        public int HitWindow100;
        public int HitWindow300;
        public double SpinnerRotationRatio;
        public float SpriteDisplaySize;   // CS 기반 — 스프라이트 표시 크기
        public float HitObjectRadius;     // CS 기반 — 히트오브젝트 반지름
        public float StackOffset;        // HitObjectRadius / 10
    }
}