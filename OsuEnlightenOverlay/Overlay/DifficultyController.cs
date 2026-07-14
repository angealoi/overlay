using System;
using OsuEnlightenOverlay.ControlPanel;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Memory;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// Difficulty Changer 수학 — 맵 AR/CS/OD + 현재 mod + 사용자 오버라이드 설정을
    /// 조합해 최종 DifficultyValues를 산출.
    ///
    /// 순수 계산만 담당한다. "언제 재계산할지"(mod 변경 감지)와 "결과를 누구에게
    /// 전달할지"(HitObjectManager 재로드)는 OverlayForm의 프레임 루프 책임 —
    /// 여기로 끌어오면 상태 동기화 API가 늘어 오히려 결합이 커진다.
    ///
    /// 게임필드 크기를 필드로 잡지 않고 호출마다 받는 이유: 창 리사이즈 때마다
    /// 값이 바뀌므로 캐시하면 stale 위험만 생긴다.
    /// </summary>
    internal class DifficultyController
    {
        readonly OverlaySettings settings;
        readonly OsuMemoryReader reader;

        public DifficultyController(OverlaySettings settings, OsuMemoryReader reader)
        {
            this.settings = settings;
            this.reader = reader;
        }

        /// <summary>
        /// Difficulty Changer + mod 적용하여 최종 AR/CS/OD 계산.
        /// Osu-external-overlay-main 참조 구현 기반:
        ///   Auto AR: HR/EZ 적용 → PreEmpt 계산 → speed multiplier로 나눔 (DT÷1.5, HT÷0.75)
        ///   Override AR: HR/EZ 미적용, speed scaling 없음 — 사용자 값이 최종 AR
        ///   Auto CS: 맵 CS + HR/EZ 적용
        ///   Override CS: 사용자 값, 단 맵 CS(mod 적용)보다 작아질 수 없음 (overdrive)
        ///   FadeIn: 400 * min(1, PreEmpt/450)
        ///   HitWindow: speed multiplier로 나눔 (Auto만, Override는 미적용)
        /// </summary>
        public DifficultyValues Compute(BeatmapData beatmap, float gamefieldWidth, float gamefieldRatio)
        {
            if (beatmap == null || settings == null || reader == null)
                return null;

            bool isHR = reader.IsHR;
            bool isEZ = reader.IsEZ;
            bool isDT = reader.IsDT || reader.IsNC; // NC = DT 파생
            bool isHT = reader.IsHT;
            float speedMultiplier = DifficultyCalculator.GetSpeedMultiplier(isDT, isHT);

            // ── AR 결정 ──
            // DT/HT 모드 시 DT/HT AR 슬롯 사용, 아니면 일반 AR 슬롯
            bool arOverride;
            double arOverrideValue = 0;
            if (isDT && !settings.ArDtAuto)
            {
                arOverride = true;
                arOverrideValue = settings.ArDtValue;
            }
            else if (isHT && !settings.ArHtAuto)
            {
                arOverride = true;
                arOverrideValue = settings.ArHtValue;
            }
            else if (!settings.ArAuto)
            {
                arOverride = true;
                arOverrideValue = settings.ArValue;
            }
            else
            {
                arOverride = false;
            }

            double ar;
            bool scalePreEmpt;
            if (arOverride)
            {
                // Override AR: HR/EZ 미적용, PreEmpt speed scaling 없음
                // (사용자가 이미 DT/HT를 고려한 AR값을 설정하므로 PreEmpt는 그대로)
                ar = Math.Max(0, Math.Min(10, arOverrideValue));
                scalePreEmpt = false;
            }
            else
            {
                // Auto AR: 맵 AR + HR/EZ 적용 (hardRockFactor=1.4)
                ar = DifficultyCalculator.ApplyModsToDifficulty(
                    beatmap.ApproachRate, 1.4, isEZ, isHR);
                scalePreEmpt = true; // PreEmpt에 speed multiplier 적용
            }

            // ── CS 결정 ──
            // Auto CS: 맵 CS + HR/EZ 적용 (hardRockFactor=1.3)
            double autoCs = DifficultyCalculator.ApplyModsToDifficulty(
                beatmap.CircleSize, 1.3, isEZ, isHR);
            autoCs = Math.Max(0, Math.Min(10, autoCs));

            double cs;
            if (!settings.CsAuto)
            {
                // Override CS: 사용자 값, 단 맵 CS(mod 적용)보다 작아질 수 없음 (overdrive)
                cs = Math.Max(settings.CsValue, autoCs);
                cs = Math.Min(10, cs);
            }
            else
            {
                cs = autoCs;
            }

            // ── OD 결정 ── (항상 맵 OD + HR/EZ, speed multiplier는 HitWindow에만)
            double od = DifficultyCalculator.ApplyModsToDifficulty(
                beatmap.OverallDifficulty, 1.4, isEZ, isHR);

            return DifficultyCalculator.CalculateWithValues(ar, cs, od,
                gamefieldWidth, gamefieldRatio,
                speedMultiplier, scalePreEmpt);
        }

        /// <summary>
        /// 현재 맵의 파싱된 AR 값 반환 (mod 미적용).
        /// ControlPanel Auto 버튼에서 사용.
        /// </summary>
        public float GetMapAR(BeatmapData beatmap)
        {
            if (beatmap == null) return 9.0f;
            return (float)beatmap.ApproachRate;
        }

        /// <summary>
        /// 현재 맵의 파싱된 CS 값 반환 (mod 미적용).
        /// </summary>
        public float GetMapCS(BeatmapData beatmap)
        {
            if (beatmap == null) return 4.0f;
            return (float)beatmap.CircleSize;
        }

        /// <summary>
        /// DT 적용 시 표시 AR 반환 — PreEmpt 기반 역산.
        /// AR → PreEmpt → ÷1.5 → 역산 AR
        /// </summary>
        public float GetMapDtAR(BeatmapData beatmap)
        {
            if (beatmap == null) return 10.0f;
            double preempt = DifficultyCalculator.MapDifficultyRangeRaw(beatmap.ApproachRate, 1800, 1200, 450);
            preempt = DifficultyCalculator.ApplySpeedMultiplierToTime(preempt, 1.5f);
            return (float)Math.Min(10, DifficultyCalculator.MapDifficultyRangeInv(preempt, 1800, 1200, 450));
        }

        /// <summary>
        /// HT 적용 시 표시 AR 반환 — PreEmpt 기반 역산.
        /// AR → PreEmpt → ÷0.75 → 역산 AR
        /// </summary>
        public float GetMapHtAR(BeatmapData beatmap)
        {
            if (beatmap == null) return 8.0f;
            double preempt = DifficultyCalculator.MapDifficultyRangeRaw(beatmap.ApproachRate, 1800, 1200, 450);
            preempt = DifficultyCalculator.ApplySpeedMultiplierToTime(preempt, 0.75f);
            return (float)Math.Min(10, DifficultyCalculator.MapDifficultyRangeInv(preempt, 1800, 1200, 450));
        }

        /// <summary>
        /// 현재 mod가 적용된 effective CS — ControlPanelForm CS override 기준값.
        /// HR/EZ mod가 반영된 CS.
        /// </summary>
        public float GetLiveCS(BeatmapData beatmap)
        {
            if (reader == null || beatmap == null) return 0f;
            bool isHR = reader.IsHR;
            bool isEZ = reader.IsEZ;
            double cs = DifficultyCalculator.ApplyModsToDifficulty(
                beatmap.CircleSize, 1.3, isEZ, isHR);
            return (float)Math.Max(0, Math.Min(10, cs));
        }
    }
}
