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
        ///   Auto AR: 맵 AR + HR/EZ 적용, PreEmpt 환산 없음 (맵 값은 이미 곡 시간)
        ///   Override AR: HR/EZ 미적용. 사용자 값은 "그 mod로 플레이할 때 보이는 AR"이므로
        ///                실시간 기준 → PreEmpt에 speedMultiplier를 곱해 곡 시간으로 환산
        ///   Auto CS: 맵 CS + HR/EZ 적용
        ///   Override CS: 사용자 값, 단 맵 CS(mod 적용)보다 작아질 수 없음 (overdrive)
        ///   FadeIn: 400 * min(1, PreEmpt/450)
        ///
        /// HitWindow에는 DT/HT를 적용하지 않는다 — osu! stable과 동일하며, 곡 시간 단위인
        /// HitWindow에 또 배수를 걸면 이중 적용이 된다. DifficultyCalculator 주석 참고.
        /// </summary>
        public DifficultyValues Compute(BeatmapData beatmap, float gamefieldWidth, float gamefieldRatio)
        {
            if (beatmap == null || settings == null || reader == null)
                return null;

            bool isHR = reader.IsHR;
            bool isEZ = reader.IsEZ;
            bool isDT = reader.IsDT || reader.IsNC; // NC = DT 파생
            bool isHT = reader.IsHT;

            // ── AR 결정 ──
            // 현재 mod에 해당하는 슬롯 값을 그대로 쓴다. Auto 모드는 없다 — Auto 버튼은
            // "이 맵의 값을 슬라이더에 채워넣는" 일회성 동작이고, 이후엔 슬라이더가 곧 값이다.
            double arValue = isDT ? settings.ArDtValue
                           : isHT ? settings.ArHtValue
                           : settings.ArValue;
            double ar = Math.Max(0, Math.Min(ArOverrideMax, arValue));

            // 슬라이더 값은 "그 mod로 플레이할 때 보이는 AR"(실시간 기준)이므로
            // 곡 시간으로 환산하려면 speedMultiplier를 곱한다.
            double preemptScale = isDT ? 1.5 : isHT ? 0.75 : 1.0;

            // ── CS 결정 ──
            // 맵 CS(mod 적용)가 하한 — 그 아래로는 내려갈 수 없다(overdrive 전용).
            double cs = Math.Min(10, Math.Max(settings.CsValue, MapCS(beatmap, isEZ, isHR)));

            // ── OD 결정 ── (항상 맵 OD + HR/EZ)
            double od = DifficultyCalculator.ApplyModsToDifficulty(
                beatmap.OverallDifficulty, 1.4, isEZ, isHR);

            return DifficultyCalculator.CalculateWithValues(ar, cs, od,
                gamefieldWidth, gamefieldRatio, preemptScale);
        }

        /// <summary>UI 슬라이더 상한과 동일 — DT 체감 AR은 10을 넘으므로 10에서 자르면 안 된다.</summary>
        public const double ArOverrideMax = 12;

        /// <summary>맵 CS + 현재 HR/EZ 적용 (hardRockFactor=1.3).</summary>
        static double MapCS(BeatmapData beatmap, bool isEZ, bool isHR)
        {
            return Math.Max(0, Math.Min(10,
                DifficultyCalculator.ApplyModsToDifficulty(beatmap.CircleSize, 1.3, isEZ, isHR)));
        }

        /// <summary>맵 AR + 현재 HR/EZ 적용 (hardRockFactor=1.4).</summary>
        double MapAR(BeatmapData beatmap)
        {
            return DifficultyCalculator.ApplyModsToDifficulty(
                beatmap.ApproachRate, 1.4, reader.IsEZ, reader.IsHR);
        }

        /// <summary>
        /// 이 맵을 speedMult 배속으로 플레이할 때 "보이는" AR — Auto 버튼 채움값.
        /// 맵 AR(+HR/EZ) → PreEmpt(곡시간) → ÷배속 → 실시간 PreEmpt → 역산 AR
        ///
        /// Compute가 슬라이더 값을 체감 AR로 해석해 ×배속 하므로, 여기서 채운 값을 그대로
        /// 두면 맵 원래 화면이 재현된다. 10으로 클램프하면 안 된다 — AR9+DT의 체감은
        /// 10.33이고, 10으로 깎으면 채운 직후부터 맵과 다른 화면이 나온다.
        /// </summary>
        float EffectiveAR(BeatmapData beatmap, float speedMult, float fallback)
        {
            if (beatmap == null || reader == null) return fallback;
            double preempt = DifficultyCalculator.MapDifficultyRangeRaw(MapAR(beatmap), 1800, 1200, 450);
            preempt = DifficultyCalculator.ApplySpeedMultiplierToTime(preempt, speedMult);
            return (float)Math.Max(0, Math.Min(ArOverrideMax,
                DifficultyCalculator.MapDifficultyRangeInv(preempt, 1800, 1200, 450)));
        }

        /// <summary>nomod AR 슬롯의 Auto 버튼 채움값 — 맵 AR + HR/EZ.</summary>
        public float GetMapAR(BeatmapData beatmap)
        {
            if (beatmap == null || reader == null) return 9.0f;
            return (float)Math.Max(0, Math.Min(ArOverrideMax, MapAR(beatmap)));
        }

        /// <summary>DT AR 슬롯의 Auto 버튼 채움값 — DT로 플레이할 때 보이는 AR.</summary>
        public float GetMapDtAR(BeatmapData beatmap)
        {
            return EffectiveAR(beatmap, 1.5f, 10.0f);
        }

        /// <summary>HT AR 슬롯의 Auto 버튼 채움값 — HT로 플레이할 때 보이는 AR.</summary>
        public float GetMapHtAR(BeatmapData beatmap)
        {
            return EffectiveAR(beatmap, 0.75f, 8.0f);
        }

        /// <summary>
        /// CS 하한 — 맵 CS + 현재 HR/EZ. CS 슬라이더가 이 값 아래로 못 내려가게 하는 기준이자
        /// CS Auto 버튼 채움값. 맵 미로드 시 0(하한 없음).
        /// </summary>
        public float GetLiveCS(BeatmapData beatmap)
        {
            if (reader == null || beatmap == null) return 0f;
            return (float)MapCS(beatmap, reader.IsEZ, reader.IsHR);
        }

        /// <summary>CS Auto 버튼 채움값 — 맵 CS + HR/EZ. 맵 미로드 시 4.0.</summary>
        public float GetAutoCS(BeatmapData beatmap)
        {
            if (beatmap == null || reader == null) return 4.0f;
            return GetLiveCS(beatmap);
        }
    }
}
