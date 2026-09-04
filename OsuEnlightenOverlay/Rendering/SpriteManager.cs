using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.Graphics.Renderers;
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

        // 슬라이더 바디를 FBO 쿼드가 아니라 경로 메시로 그릴 때 사용. HOM이 설정.
        public MmSliderRenderer SliderBodyRenderer;

        // Depth 오름차순(먼저 그린다 = 아래). 동점은 먼저 Add한 스프라이트가 위.
        // osu-stable SpriteManager.Add: BinarySearch 후 같은 Depth면 그 index에 Insert →
        // 새 항목이 리스트 앞(아래), 기존이 뒤(위). lazer HitObjectContainer도 같은 시각
        // (StartTime 동점이면 CompareReverseChildID = 먼저 넣은 오브젝트가 위).
        // 예전엔 StableOrder 오름차순이라 나중에 넣은 게 위였고, 슬라이더 끝원과 다음
        // 시작원이 같은 시각이면 다음 노트가 진행 중인 끝원을 덮었다.
        static readonly IComparer<pSprite> DepthOrderComparer = Comparer<pSprite>.Create((a, b) =>
        {
            int c = a.Depth.CompareTo(b.Depth);
            return c != 0 ? c : b.StableOrder.CompareTo(a.StableOrder);
        });

        public void SetViewportSize(int width, int height)
        {
            viewportWidth = width;
            viewportHeight = height;
        }

        // ── drawOrder — osu-lazer OsuPlayfield 층 (뒤→앞) ────────────────
        // spinnerProxies < FollowPoints < HitObjectContainer < judgementAbove
        //   < approachCircles < Cursor. HUD는 플레이필드 밖(위).
        //
        // 0.00–0.10  스피너 / 판정 파티클 (FwdLowPrio)
        // 0.12       팔로포인트
        // 0.20–0.80  히트오브젝트 (Bwd, 이른 노트일수록 위)
        // 0.81–0.83  판정 숫자 (오브젝트 위, 어프로치 아래)
        // 0.86–0.88  어프로치 서클
        // 0.95       HUD
        // 0.999/1.0  커서
        public const float FollowPointDepth = 0.12f;

        public static float DrawOrderBwd(float number)
        {
            return 0.8f - (number % 6000000) / 10000000f;
        }

        public static float DrawOrderFwdLowPrio(float number)
        {
            return (number % 1999999) / 20000000f;
        }

        public static float DrawOrderFwdPrio(float number)
        {
            return 0.8f + (number % 6000000) / 30000000f;
        }

        /// <summary>lazer judgementAboveHitObjectLayer — 오브젝트 위, 어프로치 아래.</summary>
        public static float DrawOrderJudgement(float number)
        {
            return 0.81f + (number % 6000000) / 300000000f;
        }

        /// <summary>lazer approachCircles 레이어 — 오브젝트·판정 위, HUD·커서 아래.</summary>
        public static float DrawOrderApproach(float number)
        {
            return 0.86f + (number % 6000000) / 300000000f;
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
            // 이미 리스트에 있는데 제거 예약만 된 상태(Remove 후 같은 프레임에 재Add)면
            // 물리적으로 다시 넣지 않고 예약만 취소한다 — 안 그러면 리스트에 중복이 생긴다.
            if (sprite.PendingRemove)
            {
                sprite.PendingRemove = false;
                sprite.StableOrder = stableOrderCounter++;
                spriteSet.Add(sprite);
                needsSort = true;
                return;
            }
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
            if (!spriteSet.Remove(sprite)) return;
            // 리스트에서 즉시 빼지 않는다. List.Remove는 선형 탐색+시프트로 O(n)이라
            // 스프라이트 수천 개를 한 프레임에 제거하면 O(n²) — Aspire 맵의 2048회 반복
            // 슬라이더가 윈도우를 벗어날 때 ~1.5만 개를 제거하며 40초 멈췄다.
            // 마킹만 하고 Update의 단일 O(n) 압축 패스(Discard와 동일 경로)가 걷어낸다.
            sprite.PendingRemove = true;
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
            // 제거 예약 플래그를 내려야 한다 — 리스트를 비운 뒤에도 플래그가 남으면
            // 그 스프라이트를 다시 Add할 때 "아직 리스트에 있다"로 오판해 영영 안 그려진다.
            for (int i = 0; i < sprites.Count; i++)
                sprites[i].PendingRemove = false;
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
                // Remove()가 예약한 스프라이트 — 여기서 실제로 리스트를 떠난다.
                // 플래그를 내려야 이후 새로 Add될 때 "아직 리스트에 있다"로 오판하지 않는다.
                if (sprite.PendingRemove)
                {
                    sprite.PendingRemove = false;
                    continue;
                }
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
            // Depth 오름차순 + 동점은 먼저 Add한 쪽이 위(StableOrder 내림차순).
            // 전순서라 List.Sort(불안정)여도 프레임 간 z-플리커가 없다 (C5/H21).
            // 캐싱된 비교자라 매 호출 추가 할당 없음.
            if (needsSort)
            {
                sprites.Sort(DepthOrderComparer);
                needsSort = false;
            }

            pTexture currentTexture = null;
            bool currentAdditive = false;
            bool usingPathResolve = false;
            int currentGradientId = 0;

            quadBatch.Initialize();

            // 셰이더 바인딩 — TextureShader2D (generic vertex attributes)
            Shader textureShader = shaderManager.TextureShader2D;
            Shader pathResolve = shaderManager.PathResolve;
            Shader activeShader = textureShader;
            if (textureShader != null && textureShader.IsValid)
            {
                textureShader.Begin();
                textureShader.SetProjectionMatrix(ref projectionMatrix);
                textureShader.SetColour(System.Drawing.Color.White);
                textureShader.SetTexture(0);
            }

            // 배치가 가득 차 Add 중간에 자동 flush될 때도 이 셰이더를 쓰게 한다 (E3).
            quadBatch.SetActiveShader(activeShader);

            GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Texture2D);
            GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Blend);
            GL.BlendFunc(OpenTK.Graphics.OpenGL.BlendingFactor.SrcAlpha, OpenTK.Graphics.OpenGL.BlendingFactor.OneMinusSrcAlpha);

            foreach (pSprite sprite in sprites)
            {
                // 제거 예약됐지만 아직 압축 전인 스프라이트는 그리지 않는다.
                if (sprite.PendingRemove)
                    continue;

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
                    quadBatch.Flush(activeShader);
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

                bool wantPath = sprite.SliderPathGradientTexId > 0
                    && pathResolve != null && pathResolve.IsValid;
                if (wantPath != usingPathResolve
                    || (wantPath && currentGradientId != sprite.SliderPathGradientTexId))
                {
                    quadBatch.Flush(activeShader);
                    if (wantPath)
                    {
                        if (!usingPathResolve)
                        {
                            if (textureShader != null && textureShader.IsValid)
                                textureShader.End();
                            pathResolve.Begin();
                            pathResolve.SetProjectionMatrix(ref projectionMatrix);
                            pathResolve.SetColour(System.Drawing.Color.White);
                            pathResolve.SetTexture(0);
                            pathResolve.SetGradient(1);
                            activeShader = pathResolve;
                            quadBatch.SetActiveShader(pathResolve);
                            usingPathResolve = true;
                        }
                        GL.ActiveTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture1);
                        GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, sprite.SliderPathGradientTexId);
                        GL.ActiveTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture0);
                        currentGradientId = sprite.SliderPathGradientTexId;
                    }
                    else
                    {
                        GL.ActiveTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture1);
                        GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 0);
                        GL.ActiveTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture0);
                        currentGradientId = 0;
                        if (usingPathResolve)
                        {
                            pathResolve.End();
                            if (textureShader != null && textureShader.IsValid)
                            {
                                textureShader.Begin();
                                textureShader.SetProjectionMatrix(ref projectionMatrix);
                                textureShader.SetColour(System.Drawing.Color.White);
                                textureShader.SetTexture(0);
                            }
                            activeShader = textureShader;
                            quadBatch.SetActiveShader(textureShader);
                            usingPathResolve = false;
                        }
                    }
                }

                // 텍스처 변경 시 flush
                if (sprite.Texture != currentTexture)
                {
                    quadBatch.Flush(activeShader);
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

            quadBatch.Flush(activeShader);

            GL.ActiveTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture1);
            GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 0);
            GL.ActiveTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture0);

            if (usingPathResolve)
            {
                if (pathResolve != null && pathResolve.IsValid)
                    pathResolve.End();
            }
            else if (textureShader != null && textureShader.IsValid)
                textureShader.End();

            GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Texture2D);
        }

        public void Dispose()
        {
            if (quadBatch != null)
                quadBatch.Dispose();
        }
    }
}