using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Rendering;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Skinning;

namespace OsuEnlightenOverlay.Gameplay.HitObjects
{
    /// <summary>
    /// HitCircle 렌더링 — ref/osu-stable HitCircleOsu.cs 포팅.
    /// enlighten 핵심: 항상 nomod 타이밍 사용 (hidden 타이밍 무시).
    /// </summary>
    internal class HitCircleOsu
    {
        // HD mod 활성 여부 — MmSliderRenderer 등에서 Fade Out 타이밍 결정용
        public static bool HiddenActive = false;

        // 첫 번째 객체 여부 — HD mod에서 첫 번째 approach circle 예외 (sHiddenShowFirstApproach)
        protected bool isFirstObject = false;

        protected HitObjectData data;
        public HitObjectData Data { get { return data; } }
        protected DifficultyValues difficulty;

        // 스프라이트들
        protected pSprite spriteApproachCircle;
        protected pSprite spriteHitCircle;
        protected pAnimation spriteHitCircleOverlay;
        protected List<pSprite> spriteHitCircleText = new List<pSprite>(); // 콤보 번호 (폰트 이미지)

        public bool IsArmed { get; protected set; }
        public bool IsHit { get; protected set; }
        public int ArmTime { get; protected set; }
        public bool IsSpriteAdded; // 시간 윈도우 기반 스프라이트 추가 추적

        // ARMED tag — osu-stable HitObject.ARMED = 148
        protected const byte ARMED = 148;
        public int ComboNumber
        {
            get { return _comboNumber; }
            set
            {
                _comboNumber = value;
                // 콤보 번호 변경 시 텍스트 스프라이트 재생성
                if (ShowCircleText && texManagerRef != null && SkinManager.Current != null)
                    CreateComboNumberSprites(texManagerRef, SkinManager.Current.FontHitCircle, SkinManager.Current.FontHitCircleOverlap);
            }
        }
        int _comboNumber;
        TextureManager texManagerRef;
        float gamefieldSpriteRatio = 1;
        float gameFieldRatio = 1;

        /// <summary>
        /// 콤보 번호 위치 계산용 스케일 비율 설정.
        /// </summary>
        public void SetScaleRatios(float gsr, float gfr)
        {
            gamefieldSpriteRatio = gsr;
            gameFieldRatio = gfr;
        }
        int startTimeRef;
        int preEmptRef;
        bool overlayAboveNumber = true;

        /// <summary>
        /// combo 색상 설정 — osu! stable HitCircleOsu.SetColour 포팅.
        /// hitcircle + approach circle에 combo 색상 적용, overlay는 White 유지.
        /// </summary>
        public void SetColour(Color colour)
        {
            if (spriteHitCircle != null)
            {
                spriteHitCircle.Colour = colour;
                spriteHitCircle.CurrentColour = colour;
            }
            // approach circle에도 combo 색상 적용 — osu! stable과 동일
            if (spriteApproachCircle != null)
            {
                spriteApproachCircle.Colour = colour;
                spriteApproachCircle.CurrentColour = colour;
            }
            // overlay는 White 유지 — osu! stable과 동일
        }

        /// <summary>
        /// ShowCircleText — osu! stable HitCircleOsu.ShowCircleText.
        /// HitCircleSliderEnd에서 override하여 false 반환.
        /// </summary>
        protected virtual bool ShowCircleText
        {
            get { return true; }
        }

        /// <summary>
        /// 콤보 번호 스프라이트 생성 — osu! stable pSpriteText 포팅.
        /// FontHitCircle prefix로 각 숫자 이미지 로드 (default-0.png 등).
        /// </summary>
        void CreateComboNumberSprites(TextureManager texManager, string fontPrefix, float overlap)
        {
            spriteHitCircleText.Clear();

            string text = ComboNumber > 0 ? ComboNumber.ToString() : "";
            if (text.Length == 0) return;

            const float TEXT_SIZE = 0.8f;
            string prefix = fontPrefix + "-";

            // 각 숫자의 DisplayWidth 측정
            List<pTexture> digitTextures = new List<pTexture>();
            List<float> displayWidths = new List<float>();
            for (int i = 0; i < text.Length; i++)
            {
                pTexture tex = texManager.Load(prefix + text[i]);
                digitTextures.Add(tex);
                displayWidths.Add(tex != null ? tex.Width / tex.DpiScale : 0);
            }

            // osu! stable renderCoordinates 계산
            List<float> renderX = new List<float>();
            float cx = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0) cx -= overlap;
                renderX.Add(cx);
                cx += displayWidths[i];
            }
            float totalWidth = cx;

            // 각 숫자 생성 — Fields.Gamefield, Origins.Centre
            // osu! stable: 위치 = (renderX[i] + displayWidth/2 - totalWidth/2) * drawScale + Position
            //   drawScale = TEXT_SIZE * GamefieldSpriteRatio
            // 우리: 위치 = FieldToDisplay(Position + xOffset)
            //   = (Position + xOffset) * Ratio + offset
            //   = Position * Ratio + offset + xOffset * Ratio
            //   = FieldToDisplay(Position) + xOffset * Ratio
            // osu! stable: = FieldToDisplay(Position) + (digitCentre - totalWidth/2) * TEXT_SIZE * GSR
            // → xOffset * Ratio = (digitCentre - totalWidth/2) * TEXT_SIZE * GSR
            // → xOffset = (digitCentre - totalWidth/2) * TEXT_SIZE * GSR / Ratio
            // GSR = GamefieldSpriteRatio, Ratio = GameField.Ratio
            // 이 값들은 HitObjectManagerOsu에서 알 수 있음

            for (int i = 0; i < text.Length; i++)
            {
                pTexture tex = digitTextures[i];
                if (tex == null) continue;

                float digitCentre = renderX[i] + displayWidths[i] / 2;
                // osu! stable: 위치 = (digitCentre - totalWidth/2) * TEXT_SIZE * GSR
                // 우리: FieldToDisplay가 Ratio를 곱하므로
                //   xOffset = (digitCentre - totalWidth/2) * TEXT_SIZE * GSR / Ratio
                float scaleRatio = gameFieldRatio > 0 ? gamefieldSpriteRatio / gameFieldRatio : 1;
                float xOffset = (digitCentre - totalWidth / 2) * TEXT_SIZE * scaleRatio;

                float textDepth = SpriteManager.DrawOrderBwd(startTimeRef - (this.overlayAboveNumber ? 1 : 2));
                pSprite digitSprite = new pSprite(tex, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                    data.Position + new Vector2(xOffset, 0), textDepth, false, Color.White);
                digitSprite.Scale = TEXT_SIZE;

                // Fade In/Out — hitcircle과 동일 (HD 지원)
                Transformation fadeIn = HiddenActive ?
                    new Transformation(TransformationType.Fade, 0f, 1f, startTimeRef - preEmptRef, startTimeRef - (int)(preEmptRef * 0.6), EasingTypes.None) :
                    new Transformation(TransformationType.Fade, 0f, 1f, startTimeRef - preEmptRef, startTimeRef - preEmptRef + difficulty.FadeIn, EasingTypes.None);
                digitSprite.Transformations.Add(fadeIn);

                Transformation fadeOut = HiddenActive ?
                    new Transformation(TransformationType.Fade, 1f, 0f, startTimeRef - (int)(preEmptRef * 0.6), startTimeRef - (int)(preEmptRef * 0.3), EasingTypes.None) :
                    new Transformation(TransformationType.Fade, 1f, 0f, startTimeRef + difficulty.HitWindow100, startTimeRef + difficulty.HitWindow50, EasingTypes.None);
                digitSprite.Transformations.Add(fadeOut);

                spriteHitCircleText.Add(digitSprite);
            }
        }

        /// <summary>
        /// 히트서클 텍스처 이름 — osu! stable SpriteNameHitCircle.
        /// HitCircleSliderStart → "sliderstartcircle", HitCircleSliderEnd → "sliderendcircle".
        /// </summary>
        protected virtual string SpriteNameHitCircle { get { return "hitcircle"; } }

        public HitCircleOsu(HitObjectData data, DifficultyValues difficulty, TextureManager texManager)
            : this(data, difficulty, texManager, Color.White, false)
        {
        }

        public HitCircleOsu(HitObjectData data, DifficultyValues difficulty, TextureManager texManager, Color comboColour)
            : this(data, difficulty, texManager, comboColour, false)
        {
        }

        public HitCircleOsu(HitObjectData data, DifficultyValues difficulty, TextureManager texManager, Color comboColour, bool isFirstObject)
        {
            this.data = data;
            this.difficulty = difficulty;
            this.texManagerRef = texManager;
            this.isFirstObject = isFirstObject;

            int startTime = data.StartTime;
            int p = difficulty.PreEmpt;
            startTimeRef = startTime;
            preEmptRef = p;

            // 텍스처 로드 — osu! stable HitCircleOsu.cs 정확 포팅
            // t_hit1 = LoadFirstAvailable({ SpriteNameHitCircle, "hitcircle" })
            // t_hit2 = LoadAll(t_hit1.AssetName + "overlay") — hit1이 어디서 왔는지 기반
            pTexture texHitCircle = texManager.LoadFirstAvailable(new string[] { SpriteNameHitCircle, "hitcircle" });
            pTexture texApproach = texManager.Load("approachcircle");

            // overlay는 hit1의 AssetName 기반으로 LoadAll (osu-stable: pAnimation)
            pTexture[] texOverlayArr = null;
            if (texHitCircle != null)
                texOverlayArr = texManager.LoadAll(texHitCircle.AssetName + "overlay");
            else
                texOverlayArr = texManager.LoadAll("hitcircleoverlay");

            if (texHitCircle == null || texApproach == null)
                return;

            // Approach Circle — HD mod일 때는 첫 번째 객체만 생성 (sHiddenShowFirstApproach)
            if (!HiddenActive || isFirstObject)
                CreateApproachCircle(texApproach, startTime, p, comboColour);

            // Hit Circle (drawOrderBwd)
            // Depth: drawOrderBwd(StartTime)
            spriteHitCircle = new pSprite(texHitCircle, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                data.Position, SpriteManager.DrawOrderBwd(startTime), false, comboColour);
            spriteHitCircle.TagNumeric = 1;

            // Hit Circle Overlay — osu! stable: overlay는 White (combo 색상 적용 안 됨)
            // Depth: drawOrderBwd(StartTime - (ShowOverlayAboveNumber ? 2 : 1))
            bool overlayAboveNumber = SkinManager.Current != null ? SkinManager.Current.OverlayAboveNumber : true;
            this.overlayAboveNumber = overlayAboveNumber;
            if (texOverlayArr != null && texOverlayArr.Length > 0)
            {
                spriteHitCircleOverlay = new pAnimation(texOverlayArr, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                    data.Position, SpriteManager.DrawOrderBwd(startTime - (overlayAboveNumber ? 2 : 1)), false, Color.White);
                spriteHitCircleOverlay.FrameDelay = 1000f / 2f;
            }

            // Hit Circle Text (콤보 번호) — osu! stable pSpriteText 포팅
            // FontHitCircle = "default", TextFontPrefix = "default-"
            // 텍스처: default-0.png ~ default-9.png
            // Scale = TEXT_SIZE = 0.8f
            // ShowCircleText = true일 때만 추가
            if (ShowCircleText && SkinManager.Current != null)
            {
                CreateComboNumberSprites(texManager, SkinManager.Current.FontHitCircle, SkinManager.Current.FontHitCircleOverlap);
            }

            // Fade In — osu-stable HitCircleOsu.cs:105-109
            // nomod: 0→1 (startTime-p → startTime-p+FadeIn) — stable은 클램프 없음(:108)
            // HD:    0→1 (startTime-p → startTime-p*0.6)
            Transformation fadeIn = HiddenActive ?
                new Transformation(TransformationType.Fade, 0f, 1f, startTime - p, startTime - (int)(p * 0.6), EasingTypes.None) :
                new Transformation(TransformationType.Fade, 0f, 1f, startTime - p, startTime - p + difficulty.FadeIn, EasingTypes.None);

            spriteHitCircle.Transformations.Add(fadeIn.Clone());
            if (spriteHitCircleOverlay != null)
                spriteHitCircleOverlay.Transformations.Add(fadeIn.Clone());
            foreach (pSprite textSprite in spriteHitCircleText)
                textSprite.Transformations.Add(fadeIn.Clone());

            // Disarm — osu! stable HitCircleOsu.Disarm()
            // nomod: Fade 1→0 (StartTime+HitWindow100 → StartTime+HitWindow50)
            Disarm();
        }

        void CreateApproachCircle(pTexture texApproach, int startTime, int p, Color comboColour)
        {
            if (texApproach == null) return;
            // Approach Circle: Fade 0→0.9, Scale 4→1
            spriteApproachCircle = new pSprite(texApproach, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                data.Position, SpriteManager.DrawOrderFwdPrio(startTime - p), false, comboColour);
            int fadeIn2 = Math.Min(difficulty.FadeIn * 2, p);
            spriteApproachCircle.Transformations.Add(new Transformation(
                TransformationType.Fade, 0f, 0.9f,
                startTime - p, Math.Min(startTime, startTime - p + fadeIn2),
                EasingTypes.None));
            spriteApproachCircle.Transformations.Add(new Transformation(
                TransformationType.Scale, 4f, 1f,
                startTime - p, startTime, EasingTypes.None));
            // Approach Circle Fade Out — osu-stable Arm(isHit=false): Fade 0.9→0 (startTime → startTime+60)
            spriteApproachCircle.Transformations.Add(new Transformation(
                TransformationType.Fade, 0.9f, 0f,
                startTime, startTime + 60, EasingTypes.None));
        }

        /// <summary>
        /// Disarm — osu! stable HitCircleOsu.Disarm() 포팅.
        /// 기존 ARMED transformation 제거 후 fade out 추가.
        /// </summary>
        public void Disarm()
        {
            RemoveArmedTransformations(spriteHitCircle);
            RemoveArmedTransformations(spriteHitCircleOverlay);
            foreach (pSprite textSprite in spriteHitCircleText)
                RemoveArmedTransformations(textSprite);
            RemoveArmedTransformations(spriteApproachCircle);

            // Fade Out — osu-stable HitCircleOsu.cs:206-224 (Disarm)
            // nomod: 1→0 (StartTime+HitWindow100 → StartTime+HitWindow50) — after hit time
            // HD:    1→0 (StartTime-PreEmpt*0.6 → StartTime-PreEmpt*0.3) — before hit time
            Transformation fadeOut = HiddenActive ?
                new Transformation(TransformationType.Fade, 1f, 0f, data.StartTime - (int)(preEmptRef * 0.6), data.StartTime - (int)(preEmptRef * 0.3), EasingTypes.None) :
                new Transformation(TransformationType.Fade, 1f, 0f, data.StartTime + difficulty.HitWindow100, data.StartTime + difficulty.HitWindow50, EasingTypes.None);
            fadeOut.TagNumeric = ARMED;

            spriteHitCircle?.Transformations.Add(fadeOut.Clone());
            if (spriteHitCircleOverlay != null)
                spriteHitCircleOverlay.Transformations.Add(fadeOut.Clone());
            foreach (pSprite textSprite in spriteHitCircleText)
                textSprite.Transformations.Add(fadeOut.Clone());

            // HD mod에서 첫 번째 객체의 approach circle에도 HD fade-out 추가
            if (spriteApproachCircle != null && HiddenActive)
                spriteApproachCircle.Transformations.Add(fadeOut.Clone());

            IsHit = false;
        }

        /// <summary>
        /// difficulty 변경 시 Transformation 재구성 — 객체 재생성 없이 업데이트.
        /// AR/CS/FadeIn/HitWindow 변경 시 호출.
        /// </summary>
        public virtual void UpdateDifficulty(DifficultyValues newDifficulty)
        {
            this.difficulty = newDifficulty;
            int startTime = data.StartTime;
            int p = newDifficulty.PreEmpt;
            startTimeRef = startTime;
            preEmptRef = p;

            // 콤보 넘버 스프라이트 재생성 — 스케일 비율이 변경되었을 수 있으므로
            if (ShowCircleText && texManagerRef != null && SkinManager.Current != null)
                CreateComboNumberSprites(texManagerRef, SkinManager.Current.FontHitCircle, SkinManager.Current.FontHitCircleOverlap);

            // Approach Circle Transformations 재구성
            if (spriteApproachCircle != null)
            {
                int fadeIn2 = Math.Min(newDifficulty.FadeIn * 2, p);
                spriteApproachCircle.Transformations.Clear();
                spriteApproachCircle.Transformations.Add(new Transformation(
                    TransformationType.Fade, 0f, 0.9f,
                    startTime - p, Math.Min(startTime, startTime - p + fadeIn2),
                    EasingTypes.None));
                spriteApproachCircle.Transformations.Add(new Transformation(
                    TransformationType.Scale, 4f, 1f,
                    startTime - p, startTime, EasingTypes.None));
                spriteApproachCircle.Transformations.Add(new Transformation(
                    TransformationType.Fade, 0.9f, 0f,
                    startTime, startTime + 60, EasingTypes.None));
            }

            // Hit Circle / Overlay / Text Fade In 재구성
            // nomod: 0→1 (startTime-p → startTime-p+FadeIn) — stable은 클램프 없음(:108)
            // HD:    0→1 (startTime-p → startTime-p*0.6)
            Transformation fadeIn = HiddenActive ?
                new Transformation(TransformationType.Fade, 0f, 1f, startTime - p, startTime - (int)(p * 0.6), EasingTypes.None) :
                new Transformation(TransformationType.Fade, 0f, 1f, startTime - p, startTime - p + newDifficulty.FadeIn, EasingTypes.None);
            if (spriteHitCircle != null)
            {
                spriteHitCircle.Transformations.Clear();
                spriteHitCircle.Transformations.Add(fadeIn.Clone());
            }
            if (spriteHitCircleOverlay != null)
            {
                spriteHitCircleOverlay.Transformations.Clear();
                spriteHitCircleOverlay.Transformations.Add(fadeIn.Clone());
            }
            foreach (pSprite textSprite in spriteHitCircleText)
            {
                textSprite.Transformations.Clear();
                textSprite.Transformations.Add(fadeIn.Clone());
            }

            // Disarm (fade out) 재구성
            Disarm();

            // Transformations 재구성 후 TimeRange 재계산 — Draw 컬링 정확도
            if (spriteApproachCircle != null) spriteApproachCircle.ComputeTimeRange();
            if (spriteHitCircle != null) spriteHitCircle.ComputeTimeRange();
            if (spriteHitCircleOverlay != null) spriteHitCircleOverlay.ComputeTimeRange();
            foreach (pSprite textSprite in spriteHitCircleText)
                textSprite.ComputeTimeRange();
        }

        /// <summary>
        /// ARMED tag가 붙은 transformation 제거 — osu-stable HitObject.Arm/Disarm base.
        /// </summary>
        protected static void RemoveArmedTransformations(pSprite sprite)
        {
            if (sprite == null || sprite.Transformations == null) return;
            for (int i = sprite.Transformations.Count - 1; i >= 0; i--)
            {
                if (sprite.Transformations[i].TagNumeric == ARMED)
                    sprite.Transformations.RemoveAt(i);
            }
        }

        /// <summary>
        /// 히트/미스 시 Arm — osu! stable HitCircleOsu.Arm(bool isHit) 포팅.
        /// isHit=true: Scale 1.0→1.4 + Fade 1→0 (pop animation)
        /// isHit=false: Fade 1→0 (60ms) + Scale 리셋 (miss animation)
        /// </summary>
        public void Arm(bool isHit, int armTime)
        {
            if (IsArmed) return;
            IsArmed = true;

            // 과거 객체 — 판정 윈도우를 넘었으면 이미 판정된 과거 객체
            if (armTime > data.StartTime + difficulty.HitWindow50)
            {
                IsHit = isHit;
                ArmTime = armTime;
                return;
            }

            IsHit = isHit;
            ArmTime = armTime;

            // 기존 ARMED transformation 제거
            RemoveArmedTransformations(spriteHitCircle);
            RemoveArmedTransformations(spriteHitCircleOverlay);
            foreach (pSprite textSprite in spriteHitCircleText)
                RemoveArmedTransformations(textSprite);
            RemoveArmedTransformations(spriteApproachCircle);

            if (isHit)
            {
                // HD mod에서는 hit animation이 안 보임 — fade 시작값을 0으로 (객체가 이미 안 보이는 상태)
                float hitFadeStart = HiddenActive ? 0f : 1f;

                Transformation scaleOut = new Transformation(
                    TransformationType.Scale, 1.0f, 1.4f,
                    armTime, armTime + DifficultyCalculator.FadeOut,
                    EasingTypes.Out);
                scaleOut.TagNumeric = ARMED;

                Transformation fadeOut = new Transformation(
                    TransformationType.Fade, hitFadeStart, 0f,
                    armTime, armTime + DifficultyCalculator.FadeOut,
                    EasingTypes.None);
                fadeOut.TagNumeric = ARMED;

                spriteHitCircle.Transformations.Add(scaleOut);
                spriteHitCircle.Transformations.Add(fadeOut.Clone());
                if (spriteHitCircleOverlay != null)
                {
                    spriteHitCircleOverlay.Transformations.Add(scaleOut.Clone());
                    spriteHitCircleOverlay.Transformations.Add(fadeOut.Clone());
                }

                if (spriteApproachCircle != null)
                {
                    Transformation acFade = new Transformation(
                        TransformationType.Fade, spriteApproachCircle.Alpha, 0f,
                        armTime, armTime, EasingTypes.None);
                    acFade.TagNumeric = ARMED;
                    spriteApproachCircle.Transformations.Add(acFade);
                }

                // Combo number — new layout: Fade 1→0 (60ms), old layout: Scale + Fade
                if (SkinManager.UseNewLayout)
                {
                    Transformation textFade = new Transformation(
                        TransformationType.Fade, 1f, 0f,
                        armTime, armTime + 60, EasingTypes.None);
                    textFade.TagNumeric = ARMED;
                    foreach (pSprite textSprite in spriteHitCircleText)
                        textSprite.Transformations.Add(textFade.Clone());
                }
                else
                {
                    Transformation textScale = new Transformation(
                        TransformationType.Scale, 0.8f, 1.4f * 0.8f,
                        armTime, armTime + DifficultyCalculator.FadeOut,
                        EasingTypes.Out);
                    textScale.TagNumeric = ARMED;
                    Transformation textFade = new Transformation(
                        TransformationType.Fade, 1f, 0f,
                        armTime, armTime + DifficultyCalculator.FadeOut,
                        EasingTypes.None);
                    textFade.TagNumeric = ARMED;
                    foreach (pSprite textSprite in spriteHitCircleText)
                    {
                        textSprite.Transformations.Add(textScale.Clone());
                        textSprite.Transformations.Add(textFade.Clone());
                    }
                }
            }
            else
            {
                // Miss — Fade (currentAlpha→0, 60ms), Scale 리셋
                // HD mod에서는 이미 alpha가 0이므로 0에서 시작 (osu-stable: !hidden ? Alpha : 0)
                float missStartAlpha = HiddenActive ? 0f : spriteHitCircle.Alpha;
                Transformation missFade = new Transformation(
                    TransformationType.Fade, missStartAlpha, 0f,
                    armTime, armTime + 60, EasingTypes.None);
                missFade.TagNumeric = ARMED;

                spriteHitCircle.Transformations.Add(missFade.Clone());
                if (spriteHitCircleOverlay != null)
                    spriteHitCircleOverlay.Transformations.Add(missFade.Clone());
                foreach (pSprite textSprite in spriteHitCircleText)
                {
                    float textMissStartAlpha = HiddenActive ? 0f : textSprite.Alpha;
                    Transformation textMissFade = new Transformation(
                        TransformationType.Fade, textMissStartAlpha, 0f,
                        armTime, armTime + 60, EasingTypes.None);
                    textMissFade.TagNumeric = ARMED;
                    textSprite.Transformations.Add(textMissFade);
                }

                if (spriteApproachCircle != null)
                {
                    float acMissStartAlpha = HiddenActive ? 0f : spriteApproachCircle.Alpha;
                    Transformation acFade = new Transformation(
                        TransformationType.Fade, acMissStartAlpha, 0f,
                        armTime, armTime + 60, EasingTypes.None);
                    acFade.TagNumeric = ARMED;
                    spriteApproachCircle.Transformations.Add(acFade);
                }

                spriteHitCircle.Scale = 1f;
                if (spriteHitCircleOverlay != null)
                    spriteHitCircleOverlay.Scale = 1f;
                foreach (pSprite textSprite in spriteHitCircleText)
                    textSprite.Scale = 0.8f;
            }

            // Arm 후 TimeRange 재계산 — 새 transformation 추가됨
            if (spriteHitCircle != null) spriteHitCircle.ComputeTimeRange();
            if (spriteHitCircleOverlay != null) spriteHitCircleOverlay.ComputeTimeRange();
            if (spriteApproachCircle != null) spriteApproachCircle.ComputeTimeRange();
            foreach (pSprite textSprite in spriteHitCircleText)
                textSprite.ComputeTimeRange();
        }

        /// <summary>
        /// 스택 적용 후 위치 업데이트 — UpdateStacking 호출 후.
        /// </summary>
        public void UpdateStackedPosition()
        {
            Vector2 pos = data.Position;
            if (spriteApproachCircle != null) spriteApproachCircle.Position = pos;
            if (spriteHitCircle != null) spriteHitCircle.Position = pos;
            if (spriteHitCircleOverlay != null) spriteHitCircleOverlay.Position = pos;
            // 텍스트 스프라이트는 스택 오프셋만큼 같은 방향으로 이동
            // 생성 시 data.BasePosition + xOffset 기준, 스택 후 data.Position + xOffset로 이동
            Vector2 stackOffset = data.Position - data.BasePosition;
            foreach (pSprite textSprite in spriteHitCircleText)
                textSprite.Position = textSprite.Position + stackOffset;
        }

        /// <summary>
        /// 현재 시간에서 보이는지.
        /// IsVisible: StartTime-PreEmpt ≤ Time ≤ EndTime+FadeOut
        /// </summary>
        public virtual bool IsVisibleAt(int time)
        {
            int startTime = data.StartTime;
            return time >= startTime - difficulty.PreEmpt &&
                   time <= startTime + DifficultyCalculator.FadeOut;
        }

        /// <summary>
        /// SpriteManager에 스프라이트 추가.
        /// </summary>
        public virtual void AddToSpriteManager(SpriteManager sm)
        {
            if (spriteApproachCircle != null && !sm.Contains(spriteApproachCircle)) sm.Add(spriteApproachCircle);
            if (spriteHitCircle != null && !sm.Contains(spriteHitCircle)) sm.Add(spriteHitCircle);
            if (spriteHitCircleOverlay != null && !sm.Contains(spriteHitCircleOverlay)) sm.Add(spriteHitCircleOverlay);
            foreach (pSprite textSprite in spriteHitCircleText)
                if (!sm.Contains(textSprite)) sm.Add(textSprite);
        }

        /// <summary>
        /// SpriteManager에서 스프라이트 제거.
        /// </summary>
        public void RemoveFromSpriteManager(SpriteManager sm)
        {
            if (spriteApproachCircle != null && sm.Contains(spriteApproachCircle)) sm.Remove(spriteApproachCircle);
            if (spriteHitCircle != null && sm.Contains(spriteHitCircle)) sm.Remove(spriteHitCircle);
            if (spriteHitCircleOverlay != null && sm.Contains(spriteHitCircleOverlay)) sm.Remove(spriteHitCircleOverlay);
            foreach (pSprite textSprite in spriteHitCircleText)
                if (sm.Contains(textSprite)) sm.Remove(textSprite);
        }
    }

    /// <summary>
    /// 슬라이더 시작 원 — osu! stable HitCircleSliderStart 포팅.
    /// SpriteNameHitCircle = "sliderstartcircle" (fallback: hitcircle).
    /// </summary>
    internal class HitCircleSliderStart : HitCircleOsu
    {
        protected override string SpriteNameHitCircle { get { return "sliderstartcircle"; } }

        public HitCircleSliderStart(HitObjectData data, DifficultyValues difficulty, TextureManager texManager, Color comboColour, bool isFirstObject)
            : base(data, difficulty, texManager, comboColour, isFirstObject)
        {
        }
    }

    /// <summary>
    /// 슬라이더 끝 원 — osu! stable HitCircleSliderEnd 포팅.
    /// SpriteNameHitCircle = "sliderendcircle" (fallback: hitcircle).
    /// ApproachCircle 제거. Fade In 타이밍 appearTime 기반. Disarm 즉시 Fade Out.
    /// </summary>
    internal class HitCircleSliderEnd : HitCircleOsu
    {
        protected override string SpriteNameHitCircle { get { return "sliderendcircle"; } }
        protected override bool ShowCircleText { get { return false; } } // osu! stable: 끝 원은 콤보 번호 없음

        int appearTimeRef;
        pSprite spriteReverseArrow; // osu! stable: SpriteHitCircleText = reversearrow

        public HitCircleSliderEnd(HitObjectData data, DifficultyValues difficulty, TextureManager texManager,
            int appearTime, bool reverse, float angle, int startTime, Color comboColour,
            bool firstRun, int parentStartTime, double segmentDuration)
            : base(data, difficulty, texManager, comboColour)
        {
            this.appearTimeRef = appearTime;

            // osu! stable: SpriteCollection.Remove(SpriteApproachCircle)
            spriteApproachCircle = null;

            // Alpha 0으로 설정 — appearTime 전에는 보이지 않아야 함
            if (spriteHitCircle != null) spriteHitCircle.Alpha = 0f;
            if (spriteHitCircleOverlay != null) spriteHitCircleOverlay.Alpha = 0f;

            // osu! stable: reverse가 true면 SpriteHitCircleText = reversearrow
            if (reverse)
            {
                pTexture texReverseArrow = texManager.Load("reversearrow");
                if (texReverseArrow != null)
                {
                    // osu! stable: drawOrderBwd(sortTime)
                    // sortTime = firstRun ? parent.StartTime - 1 : startTime - (int)(1000 * parent.SpatialLength / parent.Velocity) - 1
                    int sortTime = firstRun
                        ? parentStartTime - 1
                        : startTime - (int)segmentDuration - 1;
                    float arrowDepth = SpriteManager.DrawOrderBwd(sortTime);

                    spriteReverseArrow = new pSprite(texReverseArrow, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                        data.Position, arrowDepth, false, Color.White);
                    spriteReverseArrow.Alpha = 0f;
                    spriteReverseArrow.Rotation = angle;

                    // Fade 0→1 (appearTime → appearTime+150) — osu! stable과 동일
                    spriteReverseArrow.Transformations.Add(new Transformation(
                        TransformationType.Fade, 0f, 1f, appearTime, appearTime + 150, EasingTypes.None));
                    // Fade 1→0 (startTime → startTime+1) — 볼 도착 시 사라짐
                    spriteReverseArrow.Transformations.Add(new Transformation(
                        TransformationType.Fade, 1f, 0f, startTime, startTime + 1, EasingTypes.None));

                    // Scale 1.3→1 반복 (300ms 간격, EasingTypes.Out) — osu! stable 공식
                    for (int pulseStart = appearTime; pulseStart < startTime; pulseStart += 300)
                    {
                        int pulseEnd = Math.Min(pulseStart + 300, startTime);
                        spriteReverseArrow.Transformations.Add(new Transformation(
                            TransformationType.Scale, 1.3f, 1f, pulseStart, pulseEnd, EasingTypes.Out));
                    }
                }
            }

            // osu! stable: SpriteCollection.Remove(SpriteHitCircleText)
            // HitCircleText(콤보 번호)는 우리 구현에 없으므로 제거 불필요
            // overlay(SpriteHitCircle2)는 제거하지 않음 — osu! stable과 동일

            // Fade In 타이밍을 appearTime 기반으로 수정
            // osu! stable: tr.Time1 = appearTime; tr.Time2 = Math.Min(startTime, appearTime + FadeIn)
            // (nomod — hidden 아님)
            if (spriteHitCircle != null)
            {
                // 기존 Fade In transformation의 시간을 appearTime으로 수정
                bool foundFadeIn = false;
                foreach (Transformation t in spriteHitCircle.Transformations)
                {
                    if (t.Type == TransformationType.Fade && t.StartFloat == 0f)
                    {
                        t.Time1 = appearTime;
                        t.Time2 = Math.Min(startTime, appearTime + DifficultyCalculator.FadeIn);
                        foundFadeIn = true;
                        break;
                    }
                }
                if (!foundFadeIn)
                {
                    spriteHitCircle.Transformations.Add(new Transformation(
                        TransformationType.Fade, 0f, 1f,
                        appearTime, Math.Min(startTime, appearTime + DifficultyCalculator.FadeIn), EasingTypes.None));
                }
            }

            if (spriteHitCircleOverlay != null)
            {
                bool foundFadeIn = false;
                foreach (Transformation t in spriteHitCircleOverlay.Transformations)
                {
                    if (t.Type == TransformationType.Fade && t.StartFloat == 0f)
                    {
                        t.Time1 = appearTime;
                        t.Time2 = Math.Min(startTime, appearTime + DifficultyCalculator.FadeIn);
                        foundFadeIn = true;
                        break;
                    }
                }
                if (!foundFadeIn)
                {
                    spriteHitCircleOverlay.Transformations.Add(new Transformation(
                        TransformationType.Fade, 0f, 1f,
                        appearTime, Math.Min(startTime, appearTime + DifficultyCalculator.FadeIn), EasingTypes.None));
                }
            }

            // Disarm — osu! stable HitCircleSliderEnd.Disarm
            // nomod: Fade 1→0 (StartTime → StartTime) 즉시
            // HD: 첫 번째 end circle은 HD fade-out, 나머지는 제거
            if (HitCircleOsu.HiddenActive)
            {
                if (firstRun)
                {
                    // HD fade-out — osu-stable: parent.StartTime - PreEmpt*0.6 → parent.StartTime - PreEmpt*0.3
                    int hiddenFadedInTime = parentStartTime - (int)(difficulty.PreEmpt * 0.6);
                    int hiddenFadedOutTime = parentStartTime - (int)(difficulty.PreEmpt * 0.3);
                    if (spriteHitCircle != null)
                        spriteHitCircle.Transformations.Add(new Transformation(
                            TransformationType.Fade, 1f, 0f, hiddenFadedInTime, hiddenFadedOutTime, EasingTypes.None));
                    if (spriteHitCircleOverlay != null)
                        spriteHitCircleOverlay.Transformations.Add(new Transformation(
                            TransformationType.Fade, 1f, 0f, hiddenFadedInTime, hiddenFadedOutTime, EasingTypes.None));
                }
                else
                {
                    // HD에서 나머지 end circle은 제거 — 스프라이트를 null로 설정
                    spriteHitCircle = null;
                    spriteHitCircleOverlay = null;
                }
            }
            else
            {
                if (spriteHitCircle != null)
                {
                    spriteHitCircle.Transformations.Add(new Transformation(
                        TransformationType.Fade, 1f, 0f, startTime, startTime, EasingTypes.None));
                }
                if (spriteHitCircleOverlay != null)
                {
                    spriteHitCircleOverlay.Transformations.Add(new Transformation(
                        TransformationType.Fade, 1f, 0f, startTime, startTime, EasingTypes.None));
                }
            }
        }

        // osu! stable: end circle은 appearTime 기준으로 가시성 판단
        public override bool IsVisibleAt(int time)
        {
            return time >= appearTimeRef &&
                   time <= data.StartTime + DifficultyCalculator.FadeOut;
        }

        // reverse arrow도 함께 추가
        public override void AddToSpriteManager(SpriteManager sm)
        {
            base.AddToSpriteManager(sm);
            if (spriteReverseArrow != null && !sm.Contains(spriteReverseArrow))
                sm.Add(spriteReverseArrow);
        }
    }
}