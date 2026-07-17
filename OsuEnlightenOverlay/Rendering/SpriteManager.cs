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
        long stableOrderCounter = 0; // Add 전역 삽입 순서 — Depth 동점 안정 정렬 tiebreak (C5). long이라 오버플로 불가

        // Depth 오름차순 + 동점은 삽입 순서(StableOrder)로 결정하는 전순서 비교자.
        // static readonly IComparer로 캐싱해 List.Sort(Comparison<T>)의 매 호출 FunctorComparer
        // 할당(net48)을 피한다 — 정렬은 사실상 매 프레임 돈다.
        static readonly IComparer<pSprite> DepthOrderComparer = Comparer<pSprite>.Create((a, b) =>
        {
            int c = a.Depth.CompareTo(b.Depth);
            return c != 0 ? c : a.StableOrder.CompareTo(b.StableOrder);
        });

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
            // 삽입 순서를 기록해 Depth 동점 시 안정 정렬 tiebreak로 쓴다 (C5).
            sprite.StableOrder = stableOrderCounter++;
            sprites.Add(sprite);
            spriteSet.Add(sprite);
            needsSort = true;
        }

        public void Remove(pSprite sprite)
        {
            // 없으면 O(1)에 끝낸다 — 예전에는 리스트에 없는 스프라이트도 전체를 훑었다 (D4).
            // 트레일 스프라이트는 Update의 자동 Discard로 이미 빠진 뒤에 CursorRenderer가
            // 다시 Remove를 부르므로, 이 조기 종료가 헛도는 O(n) 스캔을 통째로 없앤다.
            if (!spriteSet.Remove(sprite)) return;
            sprites.Remove(sprite);
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
            // 리스트를 비웠으니 삽입 순서 카운터도 재기준화 (Clear는 맵 로드/Retry에서만 호출됨).
            // long이라 안 해도 오버플로는 없지만 숫자를 맵 단위로 작게 유지한다.
            stableOrderCounter = 0;
        }

        /// <summary>
        /// 모든 스프라이트의 Transformation 보간값 갱신.
        /// osu-stable SpriteManager.Update 포팅 — 만료된 스프라이트 자동 제거 (Discard).
        /// </summary>
        public void Update(int time)
        {
            currentTime = time;
            // Discard 제거 — osu-stable SpriteManager.cs:570-580.
            // 항목마다 RemoveAt하면 제거 1건당 O(n) 시프트라 다발 제거 시 O(n²)가 된다 (D4).
            // 살아남은 것만 앞으로 당기는 단일 O(n) 압축 패스로 처리한다 (순서 보존).
            int write = 0;
            int count = sprites.Count;
            for (int i = 0; i < count; i++)
            {
                pSprite sprite = sprites[i];
                if (sprite.Update(time) == UpdateResult.Discard)
                {
                    spriteSet.Remove(sprite);
                    continue;
                }
                sprites[write++] = sprite;
            }
            if (write < count)
                sprites.RemoveRange(write, count - write);
        }

        /// <summary>
        /// Depth 정렬된 스프라이트 렌더링.
        /// </summary>
        public void Draw()
        {
            // lazy sort — 정렬이 필요할 때만 (청크 추가 후 1회)
            // Depth 오름차순 + 동점은 삽입 순서(StableOrder)로 결정 → 전순서라 결과가 유일하게
            // 정해져 List.Sort(불안정)여도 프레임 간 z-플리커가 없다 (C5/H21).
            // 캐싱된 비교자라 매 호출 추가 할당 없음.
            if (needsSort)
            {
                sprites.Sort(DepthOrderComparer);
                needsSort = false;
            }

            pTexture currentTexture = null;
            bool currentAdditive = false;

            quadBatch.Initialize();

            // 셰이더 바인딩 — TextureShader2D (generic vertex attributes)
            Shader shader = shaderManager.TextureShader2D;
            if (shader != null && shader.IsValid)
            {
                shader.Begin();
                shader.SetProjectionMatrix(ref projectionMatrix);
                shader.SetColour(System.Drawing.Color.White);
            }

            // 배치가 가득 차 Add 중간에 자동 flush될 때도 이 셰이더를 쓰게 한다 (E3).
            quadBatch.SetActiveShader(shader);

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

            quadBatch.Flush(shader);

            if (shader != null && shader.IsValid)
                shader.End();

            GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Texture2D);
        }

        public void Dispose()
        {
            if (quadBatch != null)
                quadBatch.Dispose();
        }
    }
}