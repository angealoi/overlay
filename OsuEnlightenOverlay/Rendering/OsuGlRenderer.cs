using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.Gameplay.HitObjects;
using OsuEnlightenOverlay.Memory;
using OsuEnlightenOverlay.Rendering.Batches;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;

namespace OsuEnlightenOverlay.Rendering
{
    /// <summary>
    /// 메인 렌더링 컨텍스트 — osu! stable Graphics/OpenGl/OsuGlControl.cs 포팅.
    /// OpenGL ES 2.0 렌더링 파이프라인.
    /// </summary>
    internal class OsuGlRenderer : IDisposable
    {
        public GameField GameField { get; private set; }
        public ShaderManager ShaderManager { get; private set; }
        public TextureManager TextureManager { get; private set; }
        public SpriteManager SpriteManager { get; private set; }

        Matrix4 projectionMatrix;
        int viewportWidth;
        int viewportHeight;
        bool initialized;

        public int ViewportWidth { get { return viewportWidth; } }
        public int ViewportHeight { get { return viewportHeight; } }

        public OsuGlRenderer(string skinFolder)
        {
            ShaderManager = new ShaderManager();
            TextureManager = new TextureManager(skinFolder);
        }

        /// <summary>
        /// OpenGL 초기화 — 창 크기에 맞춰 설정.
        /// </summary>
        public void Initialize(int width, int height)
        {
            if (initialized) return;

            viewportWidth = width;
            viewportHeight = height;
            GameField = new GameField(width, height);

            // 직교 투영 행렬 (좌상단 원점, Y 아래로)
            projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);

            // GL 상태 설정
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.StencilTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);

            // fixed-function 투영 행렬 설정
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref projectionMatrix);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();

            // 셰이더 로드 (나중에 사용)
            ShaderManager.LoadAll();

            // SpriteManager 생성
            SpriteManager = new SpriteManager(GameField, ShaderManager);
            SpriteManager.SetProjectionMatrix(ref projectionMatrix);

            initialized = true;
        }

        /// <summary>
        /// 창 크기 변경 시.
        /// </summary>
        public void Resize(int width, int height)
        {
            viewportWidth = width;
            viewportHeight = height;
            GL.Viewport(0, 0, width, height);
            projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref projectionMatrix);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
            if (GameField != null)
                GameField.Resize(width, height);
            if (TextureManager != null)
                TextureManager.SetWindowHeight(height);
            if (SpriteManager != null)
            {
                SpriteManager.SetProjectionMatrix(ref projectionMatrix);
                SpriteManager.SetViewportSize(width, height);
            }
        }

        public HitObjectManagerOsu HitObjectManager { get; set; }
        public Action<int> PostHomUpdateCallback { get; set; }
        public Action<int> PreDrawCallback { get; set; }
        public List<OsuMemoryReader.HitObjectJudgement> PendingJudgements { get; set; }

        // ── Render 단계별 타이밍 (OverlayForm에서 로그 출력용, μs) ──
        long lastHomUs, lastPostHomUs, lastPreDrawUs, lastSpriteUpdateUs, lastSpriteDrawUs;
        // SpriteManager.Draw 세부 타이밍 (μs)
        long lastDrawSortUs, lastDrawBatchInitUs, lastDrawShaderBindUs, lastDrawSpriteLoopUs, lastDrawFlushUs;

        /// <summary>
        /// 직전 Render() 호출의 단계별 타이밍을 반환 (μs).
        /// </summary>
        public void GetLastRenderTimings(out long hom, out long postHom, out long preDraw, out long spriteUpdate, out long spriteDraw)
        {
            hom = lastHomUs;
            postHom = lastPostHomUs;
            preDraw = lastPreDrawUs;
            spriteUpdate = lastSpriteUpdateUs;
            spriteDraw = lastSpriteDrawUs;
        }

        /// <summary>
        /// 직전 SpriteManager.Draw() 세부 타이밍을 반환 (μs).
        /// </summary>
        public void GetLastDrawTimings(out long sort, out long batchInit, out long shaderBind, out long spriteLoop, out long flush)
        {
            sort = lastDrawSortUs;
            batchInit = lastDrawBatchInitUs;
            shaderBind = lastDrawShaderBindUs;
            spriteLoop = lastDrawSpriteLoopUs;
            flush = lastDrawFlushUs;
        }

        /// <summary>
        /// 매 프레임 렌더링.
        /// </summary>
        public void Render(int timeMs)
        {
            long freq = System.Diagnostics.Stopwatch.Frequency;
            double usPerTick = 1000000.0 / freq;
            long swStart = System.Diagnostics.Stopwatch.GetTimestamp();
            long t0, t1, t2, t3, t4, t5;

            // 불투명 검정 배경 (실제 osu! 가림)
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            if (!initialized) return;

            // Viewport
            GL.Viewport(0, 0, viewportWidth, viewportHeight);
            t0 = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * usPerTick);

            // HitObjectManager Update — 슬라이더 바디 렌더링 (depth buffer)
            // GL.Clear 후, SpriteManager.Draw 전에 호출
            // 주의: HitObjectManager.Update가 spriteManager.Clear()를 호출하므로
            // HitBurst 등 다른 스프라이트는 이 후에 추가해야 함
            if (HitObjectManager != null)
                HitObjectManager.Update(timeMs, PendingJudgements);
            t1 = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * usPerTick);

            // HitObjectManager.Update 후, SpriteManager.Draw 전에 HitBurst 추가
            if (PostHomUpdateCallback != null)
                PostHomUpdateCallback(timeMs);
            t2 = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * usPerTick);

            // HUD 렌더링 — SpriteManager.Draw 전에 스프라이트 추가
            if (PreDrawCallback != null)
                PreDrawCallback(timeMs);
            t3 = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * usPerTick);

            // SpriteManager Update + Draw (shader pipeline)
            SpriteManager.Update(timeMs);
            t4 = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * usPerTick);

            SpriteManager.Draw();
            t5 = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - swStart) * usPerTick);

            // 단계별 타이밍 기록 (μs)
            lastHomUs = t1 - t0;
            lastPostHomUs = t2 - t1;
            lastPreDrawUs = t3 - t2;
            lastSpriteUpdateUs = t4 - t3;
            lastSpriteDrawUs = t5 - t4;

            // SpriteManager.Draw 세부 타이밍 가져오기 (μs)
            if (SpriteManager != null)
            {
                lastDrawSortUs = SpriteManager.lastDrawSortUs;
                lastDrawBatchInitUs = SpriteManager.lastDrawBatchInitUs;
                lastDrawShaderBindUs = SpriteManager.lastDrawShaderBindUs;
                lastDrawSpriteLoopUs = SpriteManager.lastDrawSpriteLoopUs;
                lastDrawFlushUs = SpriteManager.lastDrawFlushUs;
            }
        }

        public void Dispose()
        {
            if (ShaderManager != null) ShaderManager.Dispose();
            if (TextureManager != null) TextureManager.Dispose();
            if (SpriteManager != null) SpriteManager.Dispose();
        }
    }
}