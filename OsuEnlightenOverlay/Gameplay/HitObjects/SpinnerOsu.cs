using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Helpers;
using OsuEnlightenOverlay.Rendering;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Skinning;

namespace OsuEnlightenOverlay.Gameplay.HitObjects
{
    /// <summary>
    /// ?�피???�더�???osu! stable SpinnerOsu.cs ?�팅.
    /// ?�더링만 (?�전/?�수??osu!가 처리).
    /// newStyle: spinner-top/bottom/middle/middle2/glow
    /// oldStyle: spinner-background/circle/metre
    /// 공통: spinner-approachcircle, spinner-spin, spinner-clear, spinner-rpm
    /// </summary>
    internal class SpinnerOsu
    {
        HitObjectData data;
        public HitObjectData Data { get { return data; } }
        DifficultyValues difficulty;

        // ?�프?�이?�들
        pSprite spriteCircleTop;
        pSprite spriteCircleBottom;
        pSprite spriteMiddleTop;
        pSprite spriteMiddleBottom;
        pSprite spriteGlow;
        pSprite spriteBackground;
        pSprite spriteScoreMetre;
        pSprite spriteApproachCircle;
        pSprite spriteClear;
        pSprite spriteSpin;
        pSprite spriteRpmBackground = null;

        bool newStyleSpinner;

        // ?�치 ??osu! stable: posTopLeftCentre
        Vector2 posTopLeftCentre;
        int spinnerTopOffset = 45;

        // ?�전

        int rotationRequirement;

        // ?�태
        enum SpinningState { NotStarted, Started, Passed }
        SpinningState state = SpinningState.NotStarted;

        // 메모리에서 읽은 실제 EndTime (DT/HT 보정). 0이면 .osu EndTime 사용.
        int effectiveEndTime = 0;

        // Bonus 카운터 — osu-stable SpriteBonusCounter (SpinnerOsu.cs:200-203, 383-399)
        // scoringRotationCount > rotationRequirement + 3 이고 2회마다 1000점 bonus 표시.
        // pSpriteText 대신 숫자 스프라이트 그룹으로 직접 렌더링 (폰트 = FontScore, 기본 "score").
        int lastBonusScoreCount = -1;   // 마지막으로 bonus 표시한 scoringRotationCount
        List<pSprite> bonusSprites = new List<pSprite>();  // 활성 bonus 숫자 스프라이트들
        TextureManager bonusTextureManager;
        Vector2 bonusPosition;
        SpriteManager spriteManagerRef;

        public int StartTime { get { return data.StartTime; } }
        public int EndTime { get { return data.EndTime; } }
        public int RotationRequirement { get { return rotationRequirement; } }
        public bool IsSpriteAdded; // 시간 윈도우 기반 스프라이트 추가 추적

        /// <summary>
        /// LoadBeatmap 시 호출 — state 완전 리셋.
        /// </summary>
        public void ResetState()
        {
            state = SpinningState.NotStarted;
            lastBonusScoreCount = 0;
            effectiveEndTime = 0;
        }

        /// <summary>
        /// difficulty 변경 시 Transformation 재구성 — 객체 재생성 없이 업데이트.
        /// </summary>
        public void UpdateDifficulty(DifficultyValues newDifficulty)
        {
            this.difficulty = newDifficulty;
            UpdateTransformations();
        }

        public SpinnerOsu(HitObjectData data, DifficultyValues difficulty, TextureManager texManager, Rendering.GameField gameField)
        {
            this.data = data;
            this.difficulty = difficulty;
            this.bonusTextureManager = texManager;

            InitializeSprites(texManager, gameField);
            UpdateTransformations();
            InitializeSpritesNoFadeIn(texManager);

            // Bonus 위치 — osu-stable SpinnerOsu.cs:201: (posTopLeftCentre.X, spinnerTopOffset + 299)
            bonusPosition = new Vector2(posTopLeftCentre.X, spinnerTopOffset + 299);
        }

        void InitializeSprites(TextureManager texManager, Rendering.GameField gameField)
        {
            // newStyle 감지 — osu-stable: UseNewLayout && (IgnoreBeatmap || spinner-circle==null) && spinner-background==null
            // spinner-circle: SkinSource.Beatmap으로 체크 (비트맵 스킨에 있으면 old-style)
            //   우리는 Beatmap 소스를 지원하지 않으므로 항상 null
            // spinner-background: SkinSource.Skin으로 체크 (사용자 스킨에 있으면 old-style)
            pTexture spinnerBackgroundSkin = texManager.Load("spinner-background", SkinSource.Skin);
            newStyleSpinner = SkinManager.UseNewLayout && spinnerBackgroundSkin == null;

            // CorrectionOffsetActive: Play + Osu 모드 ??-16
            if (true) spinnerTopOffset -= 16;

            // posTopLeftCentre ??osu! stable: WindowManager.WidthScaled / 2, spinnerTopOffset + 219
            // posTopLeftCentre — 화면 중앙에 스피너 정렬
            // Fields.TopLeft에서 screenX = X * ratio + NonWidescreenOffsetX 이므로
            // 화면 중앙(viewportW/2)이 되려면 X = (viewportW/2 - NonWidescreenOffsetX) / ratio
            float ratio = (float)gameField.windowHeight / 480f;
            float nonWidescreenOffsetX = Math.Max(0, (gameField.windowWidth - gameField.windowHeight * 4f / 3f) / 2f);
            float spinnerX = (gameField.windowWidth / 2f - nonWidescreenOffsetX) / ratio;
            posTopLeftCentre = new Vector2(spinnerX, spinnerTopOffset + 219);

            Color initialColour = Color.White;

            if (newStyleSpinner)
            {
                // newStyle ?�프?�이??
                pTexture texTop = texManager.Load("spinner-top");
                pTexture texBottom = texManager.Load("spinner-bottom");
                pTexture texMiddle = texManager.Load("spinner-middle");
                pTexture texMiddle2 = texManager.Load("spinner-middle2");

                if (texTop != null)
                {
                    spriteCircleTop = new pSprite(texTop, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                        posTopLeftCentre, SpriteManager.DrawOrderFwdLowPrio(StartTime + 1), false, initialColour);
                }
                if (texBottom != null)
                {
                    spriteCircleBottom = new pSprite(texBottom, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                        posTopLeftCentre, SpriteManager.DrawOrderFwdLowPrio(StartTime), false, initialColour);
                }
                if (texMiddle != null)
                {
                    spriteMiddleTop = new pSprite(texMiddle, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                        posTopLeftCentre, SpriteManager.DrawOrderFwdLowPrio(StartTime + 3), false, Color.FromArgb(0, 255, 255, 255));
                }
                if (texMiddle2 != null)
                {
                    spriteMiddleBottom = new pSprite(texMiddle2, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                        posTopLeftCentre, SpriteManager.DrawOrderFwdLowPrio(StartTime + 2), false, Color.FromArgb(0, 255, 255, 255));
                }

                // 초기 Scale 0.8
                if (spriteCircleTop != null) spriteCircleTop.Scale = 0.8f;
                if (spriteCircleBottom != null) spriteCircleBottom.Scale = 0.8f;
                if (spriteMiddleTop != null) spriteMiddleTop.Scale = 0.8f;
                if (spriteMiddleBottom != null) spriteMiddleBottom.Scale = 0.8f;
            }
            else
            {
                // oldStyle 스프라이트들
                // osu-stable: spinner-background는 Beatmap|Skin에서만 로드 (Osu fallback 없음)
                // 우리는 Beatmap 미지원이므로 Skin에서만 로드
                pTexture texBg = texManager.Load("spinner-background", SkinSource.Skin);
                pTexture texCircle = texManager.Load("spinner-circle");
                pTexture texMetre = texManager.Load("spinner-metre");

                // osu-stable: bgColour = SkinManager.Colours["SpinnerBackground"] 또는 (100,100,100)
                Color bgColour = Color.FromArgb(100, 100, 100);
                if (SkinManager.Current != null && SkinManager.Current.Colours.ContainsKey("SpinnerBackground"))
                    bgColour = SkinManager.Current.Colours["SpinnerBackground"];

                if (texBg != null)
                {
                    spriteBackground = new pSprite(texBg, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                        posTopLeftCentre, SpriteManager.DrawOrderFwdLowPrio(StartTime - 1), false, bgColour);
                }
                if (texCircle != null)
                {
                    spriteCircleTop = new pSprite(texCircle, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                        posTopLeftCentre, SpriteManager.DrawOrderFwdLowPrio(StartTime), false, initialColour);
                }
                if (texMetre != null)
                {
                    // osu-stable SpinnerOsu.cs:176-178
                    spriteScoreMetre = new pSprite(texMetre, Fields.TopLeft, Origins.TopLeft, Clocks.Audio,
                        new Vector2(posTopLeftCentre.X - 320, spinnerTopOffset),
                        SpriteManager.DrawOrderFwdLowPrio(StartTime + 1), false, initialColour);
                    spriteScoreMetre.DrawHeight = 0;
                }
            }

            // Approach Circle — osu-stable SpinnerOsu.cs:194:
            //   if (SpriteCircleTop.Texture.Source != SkinSource.Osu && !HD) 생성.
            // spinner-top 텍스처가 osu! 내장 기본(SkinSource.Osu)이면 approach circle을 그리지 않음.
            // Default 스킨의 spinner-top은 내장 기본이므로 approach circle 생략.
            // 커스텀 스킨이 자체 spinner-top을 제공할 때만 approach circle 활성화.
            bool isDefaultSkin = SkinManager.IsDefault;
            bool isHidden = HitCircleOsu.HiddenActive;
            bool useApproachCircle = !isDefaultSkin && !isHidden;

            if (useApproachCircle)
            {
                pTexture texApproach = texManager.Load("spinner-approachcircle");
                if (texApproach != null)
                {
                    spriteApproachCircle = new pSprite(texApproach, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                        posTopLeftCentre, SpriteManager.DrawOrderFwdLowPrio(StartTime + 2), false, initialColour);
                }
            }
        }

        void InitializeSpritesNoFadeIn(TextureManager texManager)
        {
            // spinner-spin
            pTexture texSpin = texManager.Load("spinner-spin");
            if (texSpin != null)
            {
                spriteSpin = new pSprite(texSpin, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                    new Vector2(posTopLeftCentre.X, spinnerTopOffset + 335),
                    SpriteManager.DrawOrderFwdLowPrio(StartTime + 6), false, Color.White);
                spriteSpin.Transformations.Add(new Transformation(
                    TransformationType.Fade, 0f, 1f, StartTime - difficulty.FadeIn / 2, StartTime, EasingTypes.None));
                spriteSpin.Transformations.Add(new Transformation(
                    TransformationType.Fade, 1f, 0f, EndTime - Math.Min(400, EndTime - StartTime), EndTime, EasingTypes.None));
            }

            // spinner-clear
            pTexture texClear = texManager.Load("spinner-clear");
            if (texClear != null)
            {
                spriteClear = new pSprite(texClear, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                    new Vector2(posTopLeftCentre.X, spinnerTopOffset + 115),
                    SpriteManager.DrawOrderFwdLowPrio(StartTime + 7), false, Color.White);
                // ?�리?�는 EndTime?�만 ?��?????초기 alpha 0
                spriteClear.Transformations.Add(new Transformation(
                    TransformationType.Fade, 0f, 0f, StartTime, EndTime, EasingTypes.None));
            }

            // spinner-glow (newStyle�?
            if (newStyleSpinner)
            {
                pTexture texGlow = texManager.Load("spinner-glow");
                if (texGlow != null)
                {
                    spriteGlow = new pSprite(texGlow, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                        posTopLeftCentre, SpriteManager.DrawOrderFwdLowPrio(StartTime - 1), false, Color.FromArgb(0, 255, 255, 255));
                    spriteGlow.Additive = true;
                    spriteGlow.Scale = 0.8f;
                    // glow??StartTime~EndTime ?�안 alpha 0 ?��? (updateCompletion?�서 변�?
                    spriteGlow.Transformations.Add(new Transformation(
                        TransformationType.Fade, 0f, 0f, StartTime, EndTime, EasingTypes.None));
                }
            }
        }

        void UpdateTransformations()
        {
            // 모든 ?�프?�이?�에 Fade In/Out ?�용
            Transformation fadeIn = new Transformation(
                TransformationType.Fade, 0f, 1f, StartTime - difficulty.FadeIn, StartTime, EasingTypes.None);
            Transformation fadeOut = new Transformation(
                TransformationType.Fade, 1f, 0f, EndTime, EndTime + DifficultyCalculator.FadeOut, EasingTypes.None);

            foreach (pSprite p in GetAllSprites())
            {
                if (p == null) continue;
                p.Transformations.Clear();
                p.Transformations.Add(fadeIn.Clone());
                p.Transformations.Add(fadeOut.Clone());
            }

            // Approach Circle: Scale 1.86 → 0.1
            if (spriteApproachCircle != null)
            {
                spriteApproachCircle.Transformations.Add(new Transformation(
                    TransformationType.Scale, 1.86f, 0.1f, StartTime, EndTime, EasingTypes.None));
            }

            // 모든 스프라이트의 TimeRange 재계산 — Transformations 변경 후
            foreach (pSprite p in GetAllSprites())
            {
                if (p != null) p.ComputeTimeRange();
            }
            if (spriteApproachCircle != null) spriteApproachCircle.ComputeTimeRange();

            // rotationRequirement — osu! stable: Length / 1000 * SpinnerRotationRatio
            int length = EndTime - StartTime;
            rotationRequirement = (int)(length / 1000.0 * difficulty.SpinnerRotationRatio);
        }

        List<pSprite> GetAllSprites()
        {
            List<pSprite> list = new List<pSprite>();
            if (spriteCircleTop != null) list.Add(spriteCircleTop);
            if (spriteCircleBottom != null) list.Add(spriteCircleBottom);
            if (spriteMiddleTop != null) list.Add(spriteMiddleTop);
            if (spriteMiddleBottom != null) list.Add(spriteMiddleBottom);
            if (spriteGlow != null) list.Add(spriteGlow);
            if (spriteBackground != null) list.Add(spriteBackground);
            if (spriteScoreMetre != null) list.Add(spriteScoreMetre);
            if (spriteApproachCircle != null) list.Add(spriteApproachCircle);
            if (spriteSpin != null) list.Add(spriteSpin);
            if (spriteClear != null) list.Add(spriteClear);
            if (spriteRpmBackground != null) list.Add(spriteRpmBackground);
            return list;
        }

        /// <summary>
        /// ?�재 ?�간?�서 보이?��?.
        /// </summary>
        public bool IsVisibleAt(int time)
        {
            int end = (effectiveEndTime > 0) ? effectiveEndTime : EndTime;
            return time >= StartTime - DifficultyCalculator.FadeIn &&
                   time <= end + DifficultyCalculator.FadeOut;
        }

        /// <summary>
        /// SpriteManager에 스프라이트 한 번 추가 — LoadBeatmap 시 호출.
        /// </summary>
        public void AddToSpriteManager(SpriteManager sm, int timeMs, float floatRotation, int spinningState, int memEndTime, int scoringRot, int memReq)
        {
            spriteManagerRef = sm;
            foreach (pSprite p in GetAllSprites())
            {
                if (p == null) continue;
                if (!sm.Contains(p))
                    sm.Add(p);
            }
            // bonus 스프라이트도 추가 (동적으로 생성됨)
            foreach (pSprite bp in bonusSprites)
            {
                if (!sm.Contains(bp))
                    sm.Add(bp);
            }
        }

        /// <summary>
        /// SpriteManager에서 스프라이트 제거.
        /// </summary>
        public void RemoveFromSpriteManager(SpriteManager sm)
        {
            foreach (pSprite p in GetAllSprites())
            {
                if (p == null) continue;
                if (sm.Contains(p))
                    sm.Remove(p);
            }
            foreach (pSprite bp in bonusSprites)
            {
                if (sm.Contains(bp))
                    sm.Remove(bp);
            }
        }

        /// <summary>
        /// 매 프레임 상태 업데이트 — HOM.Update에서 호출.
        /// </summary>
        public void UpdateState(int timeMs, float floatRotation, int spinningState, int memEndTime, int scoringRot, int memReq)
        {
            int req = (memReq > 0) ? memReq : rotationRequirement;
            if (memEndTime > 0) effectiveEndTime = memEndTime;
            int effectiveEnd = (effectiveEndTime > 0) ? effectiveEndTime : EndTime;

            // 메모리 값이 리셋되면(새 플레이/retry) state도 리셋
            if (floatRotation == 0 && scoringRot == 0 && spinningState == 0)
            {
                state = SpinningState.NotStarted;
                lastBonusScoreCount = 0;
            }

            // 메모리 EndTime이 .osu EndTime과 다르면 FadeOut/ApproachCircle transformation 동적 업데이트.
            // (UpdateTransformations는 생성 시점에 .osu EndTime으로 설정됨)
            if (memEndTime > 0 && memEndTime != EndTime)
            {
                int fadeOut = DifficultyCalculator.FadeOut;
                foreach (pSprite p in GetAllSprites())
                {
                    if (p == null) continue;
                    // FadeOut transformation(Time1=oldEnd, Time2=oldEnd+FadeOut)을 effectiveEnd로 교체
                    for (int ti = 0; ti < p.Transformations.Count; ti++)
                    {
                        Transformation t = p.Transformations[ti];
                        if (t.Type == TransformationType.Fade && t.Time1 == EndTime)
                        {
                            p.Transformations[ti] = new Transformation(
                                TransformationType.Fade, t.StartFloat, t.EndFloat,
                                effectiveEnd, effectiveEnd + fadeOut, t.Easing);
                        }
                    }
                }
                // approach circle scale도 effectiveEnd로
                if (spriteApproachCircle != null)
                {
                    for (int ti = 0; ti < spriteApproachCircle.Transformations.Count; ti++)
                    {
                        Transformation t = spriteApproachCircle.Transformations[ti];
                        if (t.Type == TransformationType.Scale && t.Time1 == StartTime && t.Time2 == EndTime)
                        {
                            spriteApproachCircle.Transformations[ti] = new Transformation(
                                TransformationType.Scale, t.StartFloat, t.EndFloat,
                                StartTime, effectiveEnd, t.Easing);
                        }
                    }
                }

                // Transformations 수정 후 TimeRange 재계산 — Draw 컬링 정확도
                foreach (pSprite p in GetAllSprites())
                {
                    if (p != null) p.ComputeTimeRange();
                }
                if (spriteApproachCircle != null) spriteApproachCircle.ComputeTimeRange();
            }

            if (timeMs >= StartTime && timeMs <= effectiveEnd)
            {
                // 회전 변환 — 메모리 FloatRotationCount는 "반바퀴 단위" (osu-stable line 287).
                float frc = Math.Abs(floatRotation);
                float topRotation = frc * (float)Math.PI * 0.5f;
                float middleRotation = frc * (float)Math.PI;

                if (spriteCircleTop != null)
                    spriteCircleTop.Rotation = topRotation;
                if (spriteCircleBottom != null)
                    spriteCircleBottom.Rotation = topRotation / 3f;
                if (spriteMiddleBottom != null)
                    spriteMiddleBottom.Rotation = middleRotation;

                // 진행도 — osu-stable updateCompletion: |floatRotationCount| / rotationRequirement
                float percent = Math.Min(100, frc / Math.Max(1, req) * 100);
                float progress = percent / 100f;

                // old-style metre bar — osu-stable SpinnerOsu.cs:451-463
                // spriteGlow == null (oldStyle)일 때만 metre 작동
                if (spriteGlow == null && spriteScoreMetre != null)
                {
                    // osu-stable: percent = Math.Min(99, percent)
                    int metrePercent = (int)Math.Min(99, percent);
                    int barCount = metrePercent / 10;
                    // osu-stable: SpinnerNoBlink || RNG.NextBool(((int)percent % 10) / 10f)
                    bool spinnerNoBlink = SkinManager.Current != null ? SkinManager.Current.SpinnerNoBlink : false;
                    if (spinnerNoBlink || (metrePercent % 10) / 10f >= 0.5f)
                        barCount++;

                    spriteScoreMetre.DrawTop = (int)(69.2 * (10 - barCount));
                    spriteScoreMetre.DrawHeight = (int)(69.2 * barCount);
                    spriteScoreMetre.Position = new Vector2(spriteScoreMetre.Position.X, (float)(spinnerTopOffset + 43.25 * (10 - barCount)));
                }

                // glow + scale — osu-stable updateCompletion
                if (spriteGlow != null)
                {
                    if (state < SpinningState.Passed)
                    {
                        float glowScale = 0.8f + (1f - (float)Math.Cos(progress * Math.PI / 2)) * 0.2f;
                        spriteGlow.Scale = glowScale;
                        spriteGlow.Alpha = Math.Min(1, progress);
                        if (spriteCircleTop != null) spriteCircleTop.Scale = glowScale;
                        if (spriteCircleBottom != null) spriteCircleBottom.Scale = glowScale;
                        if (spriteMiddleTop != null) spriteMiddleTop.Scale = glowScale;
                        if (spriteMiddleBottom != null) spriteMiddleBottom.Scale = glowScale;
                    }
                }

                // middleTop 색상 변화 — osu-stable: ColourLerp(White, Red, (time-StartTime)/Length)
                if (spriteMiddleTop != null)
                {
                    float timeProgress = (float)(timeMs - StartTime) / Math.Max(1, effectiveEnd - StartTime);
                    spriteMiddleTop.Colour = ColourHelper.ColourLerp(Color.White, Color.Red, Math.Min(1, timeProgress));
                    spriteMiddleTop.CurrentColour = spriteMiddleTop.Colour;
                }

                // spin 사라짐 — spinningState >= 1 (Started) — osu-stable line 295-300
                if (state == SpinningState.NotStarted && spinningState >= 1)
                {
                    if (spriteSpin != null)
                        spriteSpin.Transformations.Add(new Transformation(
                            TransformationType.Fade, spriteSpin.CurrentAlpha, 0f, timeMs, timeMs + 300, EasingTypes.None));
                    state = SpinningState.Started;
                }

                // Clear — osu-stable: SpinningState == 2 (Passed) = scoringRotationCount >= rotationRequirement
                if (state == SpinningState.Started && spinningState >= 2)
                {
                    if (spriteGlow != null)
                    {
                        spriteGlow.Colour = Color.FromArgb(255, 3, 151, 255);
                        spriteGlow.CurrentColour = spriteGlow.Colour;
                        spriteGlow.Alpha = 1f;
                    }

                    if (spriteClear != null)
                    {
                        spriteClear.Transformations.Clear();
                        spriteClear.Transformations.Add(new Transformation(
                            TransformationType.Fade, 0f, 1f, timeMs, Math.Min(effectiveEnd, timeMs + 400), EasingTypes.Out));
                        spriteClear.Transformations.Add(new Transformation(
                            TransformationType.Scale, 2f, 0.8f, timeMs, Math.Min(effectiveEnd, timeMs + 240), EasingTypes.Out));
                        spriteClear.Transformations.Add(new Transformation(
                            TransformationType.Scale, 0.8f, 1f, Math.Min(effectiveEnd, timeMs + 240), Math.Min(effectiveEnd, timeMs + 400), EasingTypes.None));
                        spriteClear.Transformations.Add(new Transformation(
                            TransformationType.Fade, 1f, 0f, effectiveEnd - 50, effectiveEnd, EasingTypes.None));
                    }

                    state = SpinningState.Passed;
                }

                // Bonus — osu-stable SpinnerOsu.cs:383-399.
                // scoringRotationCount > req + 3 이고 1바퀴(2 scoringRot)마다 bonus 표시.
                // bonus 점수 = 1000 * (scoringRot - (req + 3)) / 2
                // 단일 SpriteBonusCounter를 덮어쓰기 (osu-stable 방식).
                if (scoringRot > req + 3 && scoringRot != lastBonusScoreCount)
                {
                    int excess = scoringRot - (req + 3);
                    if (excess % 2 == 0)
                    {
                        lastBonusScoreCount = scoringRot;
                        int bonusScore = 1000 * excess / 2;
                        ShowBonus(timeMs, effectiveEnd, bonusScore);

                        // glow flash — osu-stable: spriteGlow.FlashColour(White, 200)
                        if (spriteGlow != null)
                        {
                            spriteGlow.Colour = Color.White;
                            spriteGlow.CurrentColour = Color.White;
                        }
                    }
                }
            }

            // bonus 숫자 스프라이트들도 업데이트
            foreach (pSprite bp in bonusSprites)
            {
                bp.Update(timeMs);
            }
        }

        /// <summary>
        /// Bonus 점수 숫자 스프라이트 표시 — osu-stable SpriteBonusCounter 포팅.
        /// osu-stable에서는 단일 SpriteBonusCounter 객체의 Text/Transformations를 덮어쓰기.
        /// 여기서는 이전 bonus 스프라이트를 제거하고 새 세트로 교체 (동일 효과).
        /// FontScore 폰트(score-0~9), Fade 1→0 (800ms, Out), Scale 2→1.28 (800ms, Out).
        /// </summary>
        void ShowBonus(int timeMs, int effectiveEnd, int bonusScore)
        {
            // 이전 bonus 스프라이트를 SpriteManager에서 제거
            if (spriteManagerRef != null)
            {
                foreach (pSprite bp in bonusSprites)
                    spriteManagerRef.Remove(bp);
            }
            bonusSprites.Clear();

            string fontScore = SkinManager.Current != null ? SkinManager.Current.FontScore : "score";
            int overlap = SkinManager.Current != null ? SkinManager.Current.FontScoreOverlap : 0;

            string text = bonusScore.ToString();
            string prefix = fontScore + "-";

            // 전체 너비 계산
            float totalWidth = 0;
            List<pTexture> digitTextures = new List<pTexture>();
            for (int i = 0; i < text.Length; i++)
            {
                pTexture tex = bonusTextureManager.Load(prefix + text[i]);
                if (tex == null) continue;
                digitTextures.Add(tex);
                totalWidth += tex.Width / tex.DpiScale;
                if (i > 0) totalWidth -= overlap;
            }
            if (digitTextures.Count == 0) return;

            // 중앙 정렬 — osu-stable Origins.Centre
            float startX = bonusPosition.X - totalWidth / 2f;

            float xOffset = 0;
            for (int i = 0; i < digitTextures.Count; i++)
            {
                pTexture tex = digitTextures[i];
                float digitW = tex.Width / tex.DpiScale;
                Vector2 pos = new Vector2(startX + xOffset + digitW / 2f, bonusPosition.Y);

                pSprite digit = new pSprite(tex, Fields.TopLeft, Origins.Centre, Clocks.Audio,
                    pos, SpriteManager.DrawOrderFwdLowPrio(StartTime + 3), false, Color.White);

                // osu-stable SpinnerOsu.cs:392-398 — 매번 덮어쓰기 (clear + 새 transformation)
                digit.Transformations.Add(new Transformation(
                    TransformationType.Fade, 1f, 0f, timeMs, timeMs + 800, EasingTypes.Out));
                digit.Transformations.Add(new Transformation(
                    TransformationType.Scale, 2f, 1.28f, timeMs, timeMs + 800, EasingTypes.Out));
                // Ensure we don't recycle this too early — osu-stable
                digit.Transformations.Add(new Transformation(
                    TransformationType.Fade, 0f, 0f, effectiveEnd + 800, effectiveEnd + 800, EasingTypes.None));

                bonusSprites.Add(digit);
                if (spriteManagerRef != null)
                    spriteManagerRef.Add(digit);
                xOffset += digitW - overlap;
            }
        }
    }
}
