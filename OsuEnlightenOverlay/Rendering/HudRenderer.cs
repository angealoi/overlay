using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.ControlPanel;
using OsuEnlightenOverlay.Gameplay;
using OsuEnlightenOverlay.Memory;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;

namespace OsuEnlightenOverlay.Rendering
{
    /// <summary>
    /// HUD 렌더링 — NEWNEWOVERLAY HUD 100% 포팅.
    /// 4개 요소: FPS (0), Accuracy (1), Combo (2), Hit Error Bar (3).
    /// 텍스트는 FontRenderer로 OpenGL 텍스처 렌더링, 도형은 GL 직접 그리기.
    /// </summary>
    internal class HudRenderer : IDisposable
    {
        FontRenderer fontRenderer;
        SpriteManager spriteManager;
        TextureManager textureManager;
        OverlaySettings settings;
        OsuMemoryReader reader;

        // 히트에러바 중앙값 계산용 스크래치 — 매 프레임 할당 방지 (D5)
        readonly List<int> medianScratch = new List<int>(32);

        // FPS 계산
        long fpsAccumTicks = 0;
        int fpsAccumCount = 0;
        double currentFps = 0;
        long lastFrameTicks = 0;
        bool fpsInitialized = false;

        // 텍스트 스프라이트 캐시 — 매 프레임 재생성 방지
        List<pSprite> hudSprites = new List<pSprite>();

        // 렌더링 영역 (창 크기)
        int viewportW, viewportH;

        // HUD 영역 (edit mode hit-test용)
        public RectangleF[] HudRects { get; private set; } = new RectangleF[4];

        // edit 모드 드래그 상태 (OverlayForm에서 주입)
        Overlay.OverlayEditState editState;

        public HudRenderer(FontRenderer fr, SpriteManager sm, TextureManager tm)
        {
            this.fontRenderer = fr;
            this.spriteManager = sm;
            this.textureManager = tm;
        }

        public void SetSettings(OverlaySettings s) { settings = s; }
        public void SetReader(OsuMemoryReader r) { reader = r; }
        // PP Tracker 제거됨
        public void SetEditState(Overlay.OverlayEditState es) { editState = es; }

        public void SetViewport(int w, int h)
        {
            viewportW = w;
            viewportH = h;
        }

        /// <summary>
        /// 매 프레임 호출 — FPS 업데이트.
        /// </summary>
        public void UpdateFrameTime()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (fpsInitialized)
            {
                long delta = now - lastFrameTicks;
                fpsAccumTicks += delta;
                fpsAccumCount++;

                double freq = System.Diagnostics.Stopwatch.Frequency;
                if ((double)fpsAccumTicks / freq >= 1.0)
                {
                    currentFps = fpsAccumCount / ((double)fpsAccumTicks / freq);
                    fpsAccumTicks = 0;
                    fpsAccumCount = 0;
                }
            }
            lastFrameTicks = now;
            fpsInitialized = true;
        }

        /// <summary>
        /// HUD 렌더링 — 매 프레임 Render()에서 호출.
        /// </summary>
        public void Render()
        {
            if (settings == null) return;

            // 이전 HUD 스프라이트 제거
            ClearHudSprites();

            bool editMode = settings.HudEditMode;

            // FPS
            if (settings.HudEnabled[0] || editMode)
                RenderFps(editMode);

            // Accuracy
            if (settings.HudEnabled[1] || editMode)
                RenderAccuracy(editMode);

            // Combo
            if (settings.HudEnabled[2] || editMode)
                RenderCombo(editMode);

            // Hit Error Bar
            if (settings.HudEnabled[3] || editMode)
                RenderHitErrorBar(editMode);

            // edit 모드 overlay (하이라이트 박스 + 가이드선)
            if (editMode)
                DrawEditOverlay();
        }

        void ClearHudSprites()
        {
            foreach (pSprite s in hudSprites)
                spriteManager.Remove(s);
            hudSprites.Clear();
        }

        // ── 텍스트 스프라이트 추가 헬퍼 ──
        void AddText(string text, string fontFamily, float size, FontStyle style,
            float x, float y, Color color, Color shadowColor, float shadowDx, float shadowDy)
        {
            pTexture tex = fontRenderer.RenderText(text, fontFamily, size, style, color, shadowColor, shadowDx, shadowDy);
            if (tex == null) return;

            pSprite sprite = new pSprite(tex, Fields.Native, Origins.TopLeft, Clocks.Game,
                new Vector2(x, y), 0.5f, true, Color.White);
            sprite.Alpha = 1.0f;
            spriteManager.Add(sprite);
            hudSprites.Add(sprite);
        }

        // ── 도형 렌더링 헬퍼 (직접 GL 호출) ──
        void FillRect(float x, float y, float w, float h, Color color)
        {
            GL.Disable(EnableCap.Texture2D);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.Begin(PrimitiveType.Quads);
            GL.Color4(color);
            GL.Vertex2(x, y);
            GL.Vertex2(x + w, y);
            GL.Vertex2(x + w, y + h);
            GL.Vertex2(x, y + h);
            GL.End();

            GL.Enable(EnableCap.Texture2D);
        }

        void FillTriangle(float x0, float y0, float x1, float y1, float x2, float y2, Color color)
        {
            GL.Disable(EnableCap.Texture2D);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.Begin(PrimitiveType.Triangles);
            GL.Color4(color);
            GL.Vertex2(x0, y0);
            GL.Vertex2(x1, y1);
            GL.Vertex2(x2, y2);
            GL.End();

            GL.Enable(EnableCap.Texture2D);
        }

        /// <summary>
        /// 직선 — 얇은 사각형으로 구현 (NEWNEWOVERLAY DrawLine 대응).
        /// edit 모드 가이드선용 (1px 두께).
        /// </summary>
        void DrawLine(float x1, float y1, float x2, float y2, Color color, float thick)
        {
            // 수평선
            if (Math.Abs(y1 - y2) < 0.001f)
            {
                FillRect(Math.Min(x1, x2), y1, Math.Abs(x2 - x1), thick, color);
            }
            // 수직선
            else if (Math.Abs(x1 - x2) < 0.001f)
            {
                FillRect(x1, Math.Min(y1, y2), thick, Math.Abs(y2 - y1), color);
            }
        }

        // ── 위치 계산 ──
        // HudPositionX/Y는 정규화 좌표(0.0~1.0)로 저장됨.
        // 렌더링 시 현재 viewport 크기를 곱해서 픽셀 좌표로 변환.
        void GetHudPosition(int index, float defaultX, float defaultY, out float x, out float y)
        {
            if (settings.HudUseCustomPos[index])
            {
                // 정규화 좌표 → 픽셀 좌표
                x = settings.HudPositionX[index] * viewportW;
                y = settings.HudPositionY[index] * viewportH;
            }
            else
            {
                x = defaultX;
                y = defaultY;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 1. FPS HUD — 좌상단, "{fps}fps"
        // ══════════════════════════════════════════════════════════════════
        void RenderFps(bool editMode)
        {
            float size = settings.HudFontSizes[0];
            float suffixSize = size * 0.60f;
            const float kInsetX = 12.0f, kInsetY = 12.0f;
            const float kSuffixGap = 2.0f;

            float x, y;
            GetHudPosition(0, kInsetX, kInsetY, out x, out y);

            string numText;
            if (editMode)
                numText = settings.FpsCap > 0 ? settings.FpsCap.ToString() : "360";
            else if (!fpsInitialized || currentFps == 0)
                numText = "...";
            else
            {
                int shown = (int)(currentFps + 0.5);
                if (settings.FpsCap > 0) shown = Math.Min(shown, settings.FpsCap);
                numText = shown.ToString();
            }

            // 숫자 크기 측정
            float numW, numH;
            fontRenderer.MeasureText(numText, "Roboto Mono", size, FontStyle.Bold, out numW, out numH);

            // 접미사 크기 측정
            string suffixText = "fps";
            float sufW, sufH;
            fontRenderer.MeasureText(suffixText, "Roboto Mono", suffixSize, FontStyle.Bold, out sufW, out sufH);

            // 렌더링
            Color textColor = Color.FromArgb(0xF0, 0xF0, 0xF0);
            Color shadowColor = Color.FromArgb(0xB4, 0, 0, 0);

            AddText(numText, "Roboto Mono", size, FontStyle.Bold, x, y, textColor, shadowColor, 2, 2);
            AddText(suffixText, "Roboto Mono", suffixSize, FontStyle.Bold, x + numW + kSuffixGap, y, textColor, shadowColor, 2, 2);

            HudRects[0] = new RectangleF(x, y, numW + kSuffixGap + sufW, Math.Max(numH, sufH));
        }

        // ══════════════════════════════════════════════════════════════════
        // 2. Accuracy HUD — 우상단, "98.74% SS" / "247pp/312pp"
        // ══════════════════════════════════════════════════════════════════
        void RenderAccuracy(bool editMode)
        {
            float size = settings.HudFontSizes[1];
            float gradeSize = size * (34.0f / 25.0f);

            string accText, gradeText;

            if (editMode)
            {
                accText = "100.00%";
                gradeText = "SSH";
            }
            else if (reader == null || !reader.ScoreLive)
                return;
            else
            {
                accText = string.Format("{0:F2}%", reader.Accuracy);
                gradeText = ComputeGrade(reader.Count300, reader.Count100, reader.Count50,
                    reader.CountMiss, reader.MenuMods);
            }

            // 크기 측정
            float accW, accH;
            fontRenderer.MeasureText(accText, "Roboto Mono", size, FontStyle.Bold, out accW, out accH);
            float gradeW, gradeH;
            fontRenderer.MeasureText(gradeText, "Montserrat", gradeSize, FontStyle.Bold, out gradeW, out gradeH);

            float blockW = accW + 2 + gradeW;
            float blockH = Math.Max(accH, gradeH);

            // 위치 — 우상단
            float x, y;
            GetHudPosition(1, viewportW - blockW, 0, out x, out y);

            Color white = Color.White;
            Color shadow = Color.FromArgb(0x99, 0, 0, 0);

            // acc (우측 정렬) + grade (우측 정렬)
            float gradeX = x + blockW - gradeW;
            AddText(gradeText, "Montserrat", gradeSize, FontStyle.Bold, gradeX, y, white, shadow, 2, 2);
            float accX = gradeX - 2 - accW;
            AddText(accText, "Roboto Mono", size, FontStyle.Bold, accX, y + (blockH - accH) / 2, white, shadow, 2, 2);

            HudRects[1] = new RectangleF(x, y, blockW, blockH);
        }

        string ComputeGrade(ushort c300, ushort c100, ushort c50, ushort miss, uint mods)
        {
            int total = c300 + c100 + c50 + miss;
            if (total == 0) return "D";

            double r300 = (double)c300 / total;
            double r50 = (double)c50 / total;
            bool silver = (mods & (Offsets.Mod_HD | Offsets.Mod_FL)) != 0;

            string grade;
            if (r300 == 1.0)
                grade = "SS";
            else if (r300 > 0.9 && r50 < 0.01 && miss == 0)
                grade = "S";
            else if (r300 > 0.8 && miss == 0 || r300 > 0.9)
                grade = "A";
            else if (r300 > 0.7 && miss == 0 || r300 > 0.8)
                grade = "B";
            else if (r300 > 0.6)
                grade = "C";
            else
                grade = "D";

            if (silver && (grade == "SS" || grade == "S"))
                grade += "H";

            return grade;
        }

        // ══════════════════════════════════════════════════════════════════
        // 3. Combo HUD — 좌하단, "{current} Combo"
        // ══════════════════════════════════════════════════════════════════
        void RenderCombo(bool editMode)
        {
            float size = settings.HudFontSizes[2];
            float subSize = size * (20.0f / 30.0f);
            const float kInlineGap = 2.0f;
            const float kBaselineDrop = 0.18f;

            string mainText, subText;
            if (editMode)
            {
                mainText = "1234";
                subText = "Combo";
            }
            else if (reader == null || !reader.ScoreLive)
                return;
            else
            {
                int combo = reader.CurrentCombo > 0 ? reader.CurrentCombo : 0;
                mainText = combo.ToString();
                subText = "Combo";
            }

            float mainW, mainH;
            fontRenderer.MeasureText(mainText, "Roboto Mono", size, FontStyle.Bold, out mainW, out mainH);
            float subW, subH;
            fontRenderer.MeasureText(subText, "Roboto Mono", subSize, FontStyle.Bold, out subW, out subH);

            float blockH = Math.Max(mainH, subH + mainH * kBaselineDrop);

            // 위치 — 좌하단
            float x, y;
            GetHudPosition(2, 0, viewportH - blockH, out x, out y);

            Color mainColor = Color.White;
            Color subColor = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
            Color shadow = Color.FromArgb(0xAA, 0, 0, 0);

            AddText(mainText, "Roboto Mono", size, FontStyle.Bold, x, y, mainColor, shadow, 2, 2);
            AddText(subText, "Roboto Mono", subSize, FontStyle.Bold,
                x + mainW + kInlineGap, y + mainH * kBaselineDrop, subColor, shadow, 2, 2);

            HudRects[2] = new RectangleF(x, y, mainW + kInlineGap + subW, blockH);
        }

        // ══════════════════════════════════════════════════════════════════
        // ══════════════════════════════════════════════════════════════════
        // Edit Overlay — 하이라이트 박스 + 스냅/축고정 가이드선
        // (NEWNEWOVERLAY overlay_edit.cpp DrawEditOverlay 1:1 포팅)
        // ══════════════════════════════════════════════════════════════════
        void DrawEditOverlay()
        {
            float pad = Overlay.EditConstants.HighlightPad;

            // 각 렌더링된 HUD 주변 하이라이트 박스 (selected vs unselected 스타일).
            for (int i = 0; i < 4; i++)
            {
                RectangleF r = HudRects[i];
                if (r.Width < 0.0f) continue;  // 미렌더링

                bool selected = (settings.HudEditSelected == i);
                float left = r.X - pad;
                float top = r.Y - pad;
                float w = r.Width + pad * 2.0f;
                float h = r.Height + pad * 2.0f;

                Color fill = selected ? Overlay.EditConstants.FillSelected
                                      : Overlay.EditConstants.FillUnselected;
                FillRect(left, top, w, h, fill);

                // 외곽선: 4개 얇은 사각형 (stroke API 없음).
                Color outline = selected ? Overlay.EditConstants.OutlineSelected
                                         : Overlay.EditConstants.OutlineUnselected;
                const float t = 1.0f;
                FillRect(left, top, w, t, outline);             // top
                FillRect(left, top + h - t, w, t, outline);     // bottom
                FillRect(left, top, t, h, outline);             // left
                FillRect(left + w - t, top, t, h, outline);     // right
            }

            // 중앙 스냅 가이드: snap ON + 드래그 중일 때 화면 중앙 세로선.
            if (settings.HudEditSnap && editState != null && editState.Dragging)
            {
                float cx = viewportW * 0.5f;
                DrawLine(cx, 0.0f, cx, viewportH,
                    Overlay.EditConstants.ColorSnapGuide, Overlay.EditConstants.SnapGuideThick);
            }

            // axis-lock 가이드선.
            if (editState != null && editState.Lock != Overlay.OverlayEditState.AxisLock.None)
            {
                if (editState.Lock == Overlay.OverlayEditState.AxisLock.Horizontal)
                {
                    DrawLine(0.0f, editState.LockCenterY, viewportW, editState.LockCenterY,
                        Overlay.EditConstants.ColorAxisLock, Overlay.EditConstants.AxisLockThick);
                }
                else
                {
                    DrawLine(editState.LockCenterX, 0.0f, editState.LockCenterX, viewportH,
                        Overlay.EditConstants.ColorAxisLock, Overlay.EditConstants.AxisLockThick);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 4. Hit Error Bar — 상단 중앙, 타이밍 오차 막대
        // ══════════════════════════════════════════════════════════════════
        void RenderHitErrorBar(bool editMode)
        {
            float size = settings.HudFontSizes[3];
            float scale = size / 60.0f;
            float pxPerMs = settings.HudHitErrorScale;

            const float kBarH = 60.0f, kTickH = 40.0f;
            const float kBarW = 8.0f, kTickW = 8.0f;
            const float kArrowSize = 25.0f;
            const float kTopMargin = 20.0f;

            // OD → 타이밍 윈도우
            float od = reader != null ? reader.BeatmapOD : 8.0f;
            if (reader != null)
            {
                if (reader.IsHR) od = Math.Min(10, od * 1.4f);
                if (reader.IsEZ) od = Math.Max(0, od * 0.5f);
            }
            if (editMode) od = 8.0f;

            float w300 = Math.Max(0, 80 - 6 * od);
            float w100 = Math.Max(w300, 140 - 8 * od);
            float w50 = Math.Max(w100, 200 - 10 * od);

            float barH = kBarH * scale;
            float tickH = kTickH * scale;
            float barW = kBarW * scale;
            float tickW = kTickW * scale;
            float arrowSize = kArrowSize * scale;

            float totalW = w50 * pxPerMs * 2.0f + tickW * 2.0f;
            float centerX = viewportW * 0.5f;
            float rectLeft = centerX - totalW * 0.5f;
            float rectTop = kTopMargin;

            // 위치 (커스텀 가능)
            float posX, posY;
            GetHudPosition(3, rectLeft, rectTop, out posX, out posY);
            centerX = posX + totalW * 0.5f;

            // 색상
            Color barCol = Color.FromArgb(0xFF, 0xBF, 0x00, 0x00);
            Color c300Col = Color.FromArgb(0xBF, 0xFF, 0xFF, 0x00);
            Color c100Col = Color.FromArgb(0xBF, 0x00, 0x80, 0xC0);
            Color c50Col = Color.FromArgb(0xBF, 0x80, 0x00, 0xFF);
            Color missCol = Color.FromArgb(0xBF, 0xFF, 0x00, 0x00);

            // 윈도우 배경 띠 (선택적 — NEWNEWOVERLAY은 배경 띠 없음, 틱만)

            // 중앙 바
            FillRect(centerX - barW * 0.5f, posY, barW, barH, barCol);

            // 틱 렌더링
            List<int> errors;
            if (editMode)
            {
                errors = new List<int> { -22, -14, -8, -3, 0, 2, 5, 11, 18 };
            }
            else
            {
                if (reader == null || !reader.ScoreLive || reader.HitErrors.Count == 0) return;
                errors = reader.HitErrors;
            }

            foreach (int err in errors)
            {
                float tickX = centerX + err * pxPerMs - tickW * 0.5f;
                Color tickCol;
                int absErr = Math.Abs(err);
                if (absErr <= w300) tickCol = c300Col;
                else if (absErr <= w100) tickCol = c100Col;
                else if (absErr <= w50) tickCol = c50Col;
                else tickCol = missCol;

                FillRect(tickX, posY + (barH - tickH) * 0.5f, tickW, tickH, tickCol);
            }

            // 중앙값 화살표
            if (errors.Count > 0)
            {
                // 스크래치 재사용 — 매 프레임 new List<int>는 순수 GC 압박이다 (D5).
                // errors는 최근 30개로 제한되므로 정렬 자체는 싸다.
                medianScratch.Clear();
                medianScratch.AddRange(errors);
                medianScratch.Sort();
                double median = medianScratch[medianScratch.Count / 2];

                float arrowX = centerX + (float)median * pxPerMs;
                Color arrowCol;
                if (Math.Abs(median) <= 5) arrowCol = Color.White;
                else if (median < 0) arrowCol = Color.FromArgb(0xFF, 0x00, 0x80, 0xFF);
                else arrowCol = Color.FromArgb(0xFF, 0xFF, 0x00, 0x00);

                // 역삼각형 (아래쪽 향함)
                float ay = posY - arrowSize - 2;
                FillTriangle(
                    arrowX, ay + arrowSize, // 위쪽 (apex)
                    arrowX - arrowSize * 0.5f, ay, // 좌하
                    arrowX + arrowSize * 0.5f, ay, // 우하
                    arrowCol);
            }

            HudRects[3] = new RectangleF(posX, posY, totalW, barH + arrowSize + 2);
        }

        public void Dispose()
        {
            ClearHudSprites();
        }
    }
}