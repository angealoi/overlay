using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.Rendering.Batches;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;

namespace OsuEnlightenOverlay.Rendering
{
    /// <summary>
    /// 스프라이트 관리자 — osu! stable Graphics/Sprites/SpriteManager.cs 포팅.
    /// Depth 정렬 + Update + Draw.
    /// </summary>
    internal class SpriteManager : IDisposable
    {
        List<pSprite> sprites = new List<pSprite>();
        HashSet<pSprite> spriteSet = new HashSet<pSprite>(); // O(1) Contains
        bool needsSort = false; // lazy sort flag — sort only before Draw when dirty
        QuadBatch quadBatch;
        GameField gameField;
        ShaderManager shaderManager;
        Matrix4 projectionMatrix;
        int viewportWidth = 800;
        int viewportHeight = 600;

        public float GamefieldSpriteRatio = 1f;
        int currentTime = 0; // Draw 시간 기반 컬링용

        // ── Draw 세부 타이밍 (OsuGlRenderer에서 읽음, μs) ──
        public long lastDrawSortUs;
        public long lastDrawBatchInitUs;
        public long lastDrawShaderBindUs;
        public long lastDrawSpriteLoopUs;
        public long lastDrawFlushUs;

        public void SetViewportSize(int width, int height)
        {
            viewportWidth = width;
            viewportHeight = height;
        }

        // ── drawOrder — osu! stable SpriteManager 포팅 ──
        // drawOrderBwd: 0.8 - (number % 6000000) / 10000000 (내림차순, hitcircle용)
        public static float DrawOrderBwd(float number)
        {
            return 0.8f - (number % 6000000) / 10000000f;
        }

        // drawOrderFwdLowPrio: (number % 1999999) / 10000000 (0~0.2, 스피너용)
        public static float DrawOrderFwdLowPrio(float number)
        {
            return (number % 1999999) / 10000000f;
        }

        // drawOrderFwdPrio: 0.8 + (number % 6000000) / 30000000 (오름차순, approach circle용)
        public static float DrawOrderFwdPrio(float number)
        {
            return 0.8f + (number % 6000000) / 30000000f;
        }

        public SpriteManager(GameField gameField, ShaderManager shaderManager)
        {
            this.gameField = gameField;
            this.shaderManager = shaderManager;
            quadBatch = new QuadBatch();
        }

        public void SetProjectionMatrix(ref Matrix4 matrix)
        {
            projectionMatrix = matrix;
        }

        public void Add(pSprite sprite)
        {
            // O(1) append + lazy sort — avoids O(n) List.Insert per sprite
            sprites.Add(sprite);
            spriteSet.Add(sprite);
            needsSort = true;
        }

        public void Remove(pSprite sprite)
        {
            sprites.Remove(sprite);
            spriteSet.Remove(sprite);
        }

        public bool Contains(pSprite sprite)
        {
            return spriteSet.Contains(sprite);
        }

        public int GetSpriteCount()
        {
            return sprites.Count;
        }

        public void Clear()
        {
            sprites.Clear();
            spriteSet.Clear();
            needsSort = false;
        }

        /// <summary>
        /// 모든 스프라이트의 Transformation 보간값 갱신.
        /// osu-stable SpriteManager.Update 포팅 — 만료된 스프라이트 자동 제거 (Discard).
        /// </summary>
        public void Update(int time)
        {
            currentTime = time;
            // 역방향 순회하며 Discard 제거 — osu-stable SpriteManager.cs:570-580
            for (int i = sprites.Count - 1; i >= 0; i--)
            {
                pSprite sprite = sprites[i];
                UpdateResult result = sprite.Update(time);
                if (result == UpdateResult.Discard)
                {
                    spriteSet.Remove(sprite);
                    sprites.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Depth 정렬된 스프라이트 렌더링.
        /// </summary>
        public void Draw()
        {
            double usPerTick = 1e6 / System.Diagnostics.Stopwatch.Frequency;
            long swStart, t0, t1, t2, t3;

            // lazy sort — 정렬이 필요할 때만 (청크 추가 후 1회)
            swStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (needsSort)
            {
                sprites.Sort((a, b) => a.Depth.CompareTo(b.Depth));
                needsSort = false;
            }
            t0 = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * usPerTick);

            pTexture currentTexture = null;
            bool currentAdditive = false;

            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            quadBatch.Initialize();

            // 셰이더 바인딩 — TextureShader2D (generic vertex attributes)
            Shader shader = shaderManager.TextureShader2D;
            if (shader != null && shader.IsValid)
            {
                shader.Begin();
                shader.SetProjectionMatrix(ref projectionMatrix);
                shader.SetColour(System.Drawing.Color.White);
            }
            t2 = System.Diagnostics.Stopwatch.GetTimestamp();

            GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Texture2D);
            GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Blend);
            GL.BlendFunc(OpenTK.Graphics.OpenGL.BlendingFactor.SrcAlpha, OpenTK.Graphics.OpenGL.BlendingFactor.OneMinusSrcAlpha);

            foreach (pSprite sprite in sprites)
            {
                // 시간 기반 컬링 — 시간 범위 밖 스프라이트 스킵
                if (sprite.TimeRangeCached)
                {
                    if (currentTime < sprite.StartTime || currentTime > sprite.EndTime)
                        continue;
                }

                if (sprite.CurrentAlpha <= 0.001f)
                    continue;

                // 텍스처 바인딩
                if (sprite.Texture == null) continue;

                // view-rectangle culling — 화면 밖 스프라이트 skip
                Vector2 screenPosCheck;
                if (sprite.Field == Fields.Gamefield)
                    screenPosCheck = gameField.FieldToDisplay(sprite.CurrentPosition);
                else if (sprite.Field == Fields.GamefieldWide)
                    screenPosCheck = gameField.FieldToDisplayWide(sprite.CurrentPosition);
                else
                    screenPosCheck = sprite.CurrentPosition;

                float checkScale = sprite.CurrentScale;
                if (sprite.Field == Fields.Gamefield || sprite.Field == Fields.GamefieldWide)
                    checkScale *= GamefieldSpriteRatio;
                float checkW = (sprite.Texture.Width / sprite.Texture.DpiScale) * checkScale;
                float checkH = (sprite.Texture.Height / sprite.Texture.DpiScale) * checkScale;

                if (screenPosCheck.X + checkW < 0 || screenPosCheck.X - checkW > viewportWidth ||
                    screenPosCheck.Y + checkH < 0 || screenPosCheck.Y - checkH > viewportHeight)
                    continue;

                // Additive 변경 시 flush
                if (sprite.Additive != currentAdditive)
                {
                    quadBatch.Flush(shader);
                    if (sprite.Additive)
                    {
                        GL.BlendFunc(OpenTK.Graphics.OpenGL.BlendingFactor.SrcAlpha, OpenTK.Graphics.OpenGL.BlendingFactor.One);
                    }
                    else
                    {
                        GL.BlendFunc(OpenTK.Graphics.OpenGL.BlendingFactor.SrcAlpha, OpenTK.Graphics.OpenGL.BlendingFactor.OneMinusSrcAlpha);
                    }
                    currentAdditive = sprite.Additive;
                }

                // 텍스처 변경 시 flush
                if (sprite.Texture != currentTexture)
                {
                    quadBatch.Flush(shader);
                    currentTexture = sprite.Texture;
                    currentTexture.Bind();
                }

                // 화면 좌표로 변환
                Vector2 screenPos;
                // osu! stable: drawScaleVector = VectorScale × drawScale
                // @2x에서 drawScaleVector *= 1/DpiScale
                float dpi = sprite.Texture.DpiScale;
                float spriteScale = sprite.CurrentScale * sprite.VectorScale.X;
                if (dpi != 1)
                    spriteScale /= dpi;

                if (sprite.Field == Fields.Gamefield)
                {
                    screenPos = gameField.FieldToDisplay(sprite.CurrentPosition);
                    // CS 적용: GamefieldSpriteRatio를 scale에 곱함
                    spriteScale *= GamefieldSpriteRatio;
                }
                else if (sprite.Field == Fields.GamefieldWide)
                {
                    screenPos = gameField.FieldToDisplayWide(sprite.CurrentPosition);
                    spriteScale *= GamefieldSpriteRatio;
                }
                else if (sprite.Field == Fields.NativeStandardScale)
                {
                    // osu! stable NativeStandardScale:
                    // 위치: drawPosition 그대로 (스케일링 없음)
                    // 스케일: drawScale *= RatioInverse = Height / SpriteRes(768)
                    screenPos = sprite.CurrentPosition;
                    float ratioInverse = (float)viewportHeight / 768f;
                    spriteScale *= ratioInverse;
                }
                else if (sprite.Field == Fields.TopLeft)
                {
                    // osu! stable Fields.TopLeft:
                    // 위치: drawPosition * Ratio (Ratio = Height / 480)
                    // + NonWidescreenOffsetX (와이드스크린에서 4:3 영역 중앙 보정)
                    // 스케일: drawScale *= RatioInverse (RatioInverse = Height / SpriteRes, SpriteRes = 768)
                    float ratio = (float)viewportHeight / 480f;
                    screenPos = sprite.CurrentPosition * ratio;
                    // NonWidescreenOffsetX = max(0, (Width - Height*4/3) / 2)
                    float nonWidescreenOffsetX = Math.Max(0, (viewportWidth - viewportHeight * 4f / 3f) / 2f);
                    screenPos.X += nonWidescreenOffsetX;
                    float ratioInverse = (float)viewportHeight / 768f;
                    spriteScale *= ratioInverse;
                }
                else
                {
                    screenPos = sprite.CurrentPosition;
                }

                // 텍스처 크기 — 원본 픽셀 기준 (DpiScale로 나누지 않음)
                // osu! stable: drawRectangleSource *= DpiScale로 원본 픽셀 기준 사용
                // drawScaleVector에 1/DpiScale이 이미 적용되어 표시 크기는 절반으로
                float texW = sprite.Texture.Width;
                float texH = sprite.Texture.Height;
                float fullTexH = sprite.Texture.Height;

                // DrawTop/DrawHeight — spinner metre 부분 렌더링
                // DrawTop/DrawHeight — spinner metre 부분 렌더링
                // osu-stable: DrawTop/DrawHeight는 1x 기준 픽셀 값
                // @2x에서: drawRectangleSource *= DpiScale → 원본 픽셀 기준
                // texW/texH는 원본 픽셀 기준, spriteScale에 1/DpiScale 적용됨
                float drawTopRatio = 0;
                float drawHeightRatio = 1;
                if (sprite.DrawHeight >= 0 && sprite.DrawHeight * dpi < fullTexH)
                {
                    // 텍스처 좌표 — 원본 픽셀 기준 비율 (DrawTop * DpiScale = 원본 픽셀)
                    drawTopRatio = (sprite.DrawTop * dpi) / fullTexH;
                    drawHeightRatio = (sprite.DrawHeight * dpi) / fullTexH;
                    // 표시 크기 — 원본 픽셀 기준 (DrawHeight * DpiScale)
                    // spriteScale에 1/DpiScale이 이미 적용되어 있으므로 표시 크기는 자동으로 절반
                    texH = sprite.DrawHeight * dpi;
                }

                quadBatch.Add(sprite, screenPos, texW, texH, spriteScale, drawTopRatio, drawHeightRatio);
            }

            t3 = System.Diagnostics.Stopwatch.GetTimestamp();

            quadBatch.Flush(shader);

            if (shader != null && shader.IsValid)
                shader.End();

            GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Texture2D);

            // 세부 타이밍 기록 (μs)
            lastDrawSortUs = t0;
            lastDrawBatchInitUs = (long)((t2 - t1) * usPerTick);
            lastDrawShaderBindUs = (long)((t3 - t2) * usPerTick);
            lastDrawSpriteLoopUs = (long)((t3 - t2) * usPerTick); // sprite loop는 위에서 측정
            lastDrawFlushUs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - t3) * usPerTick);
        }

        public void Dispose()
        {
            if (quadBatch != null)
                quadBatch.Dispose();
        }
    }
}