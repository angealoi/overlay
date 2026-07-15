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
        ///
        /// DT/HT는 여기서 아무것도 하지 않는다. PreEmpt와 HitWindow는 "곡 시간" 단위이고,
        /// 오버레이는 osu!의 오디오 시간(= 곡 시간)으로 렌더링한다. DT면 그 시계 자체가
        /// 실시간 대비 1.5배로 흐르므로 체감 속도는 저절로 빨라진다. 여기서 또 나누면
        /// 이중 적용이다.
        ///
        /// osu! stable도 동일하다 (HitObjectManager.UpdateVariables:468):
        ///   PreEmpt = (int)MapDifficultyRange(Beatmap.DifficultyApproachRate, 1800, 1200, 450);
        /// MapDifficultyRange 안의 ApplyModsToDifficulty는 Easy와 HardRock만 본다.
        ///
        /// osu!에 ApplyModsToTime(time/1.5)이 있긴 하지만 PreEmpt에 쓰이는 곳은 단 한 군데,
        /// 곡 선택 화면 툴팁(Beatmap.cs:1153, SongSelection_DifficultyInfo_Tooltip_Osu)뿐이다.
        /// 플레이어에게 "이 mod면 체감이 이 정도"라고 보여주는 표시용 변환이지 게임플레이
        /// 로직이 아니다. 이 둘을 혼동한 것이 DT에서 모든 게 1.5배속으로 보이던 원인이었다.
        /// </summary>
        /// <param name="ar">최종 AR 값 (이미 mod + 설정 오버라이드 적용됨)</param>
        /// <param name="cs">최종 CS 값 (이미 mod + 설정 오버라이드 적용됨)</param>
        /// <param name="od">최종 OD 값 (이미 mod 적용됨)</param>
        /// <param name="preemptScale">
        /// PreEmpt를 곡 시간으로 환산하는 배수. Auto AR은 맵 값이라 이미 곡 시간이므로 1.0.
        /// 사용자 override AR은 "DT 켜고 봤을 때 보이는 AR"(실시간 기준)이므로 곡 시간으로
        /// 바꾸려면 speedMultiplier를 곱한다 — DT 1.5, HT 0.75.
        /// 예: override AR9 + DT → 실시간 600ms를 원함 → 곡 시간 900ms → 시계가 1.5배로
        /// 흐르니 실제로 600ms에 보인다.
        /// </param>
        public static DifficultyValues CalculateWithValues(double ar, double cs, double od,
            float gamefieldWidth, float gamefieldRatio, double preemptScale = 1.0)
        {
            DifficultyValues dv = new DifficultyValues();

            // AR → PreEmpt (MapDifficultyRange with hardRockFactor=1.4 already applied to ar)
            double preempt = MapDifficultyRangeRaw(ar, 1800, 1200, 450) * preemptScale;
            dv.PreEmpt = (int)preempt;

            // FadeIn — osu! stable: 400 * min(1, PreEmpt/450)
            dv.FadeIn = (int)(400.0 * Math.Min(1.0, preempt / 450.0));

            // OD → HitWindow
            dv.HitWindow50 = Math.Max(1, (int)MapDifficultyRangeRaw(od, 200, 150, 100));
            dv.HitWindow100 = Math.Max(1, (int)MapDifficultyRangeRaw(od, 140, 100, 60));
            dv.HitWindow300 = Math.Max(1, (int)MapDifficultyRangeRaw(od, 80, 50, 20));

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
        /// 곡 시간을 실시간으로 환산 — osu! stable HitObjectManager.ApplyModsToTime.
        /// DT: time / 1.5, HT: time / 0.75
        ///
        /// 표시 전용이다. 게임플레이 계산에 쓰면 안 된다 — PreEmpt/HitWindow는 곡 시간
        /// 단위인데 오디오 시계가 이미 DT면 1.5배로 흐르므로 이중 적용이 된다.
        /// osu!도 이 함수를 PreEmpt에 쓰는 곳은 곡 선택 툴팁 한 군데뿐이다
        /// (Beatmap.cs:1153, SongSelection_DifficultyInfo_Tooltip_Osu).
        /// 여기서는 컨트롤 패널의 DT/HT AR 표시값 역산(GetMapDtAR/GetMapHtAR)에만 쓴다.
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