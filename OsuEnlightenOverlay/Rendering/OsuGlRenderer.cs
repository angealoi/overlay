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
        // SpriteManager.Draw 이후 호출 — HUD 즉시모드 도형(에러바·에디트 하이라이트)을 게임플레이
        // 위에 그리기 위함 (I-감사 #18). PreDrawCallback은 스프라이트 추가 전용으로 남는다.
        public Action<int> PostDrawCallback { get; set; }
        public List<OsuMemoryReader.HitObjectJudgement> PendingJudgements { get; set; }

        /// <summary>
        /// 매 프레임 렌더링.
        /// </summary>
        public void Render(int timeMs)
        {
            // 불투명 검정 배경 (실제 osu! 가림)
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            if (!initialized) return;

            // Viewport
            GL.Viewport(0, 0, viewportWidth, viewportHeight);

            // HitObjectManager Update — 슬라이더 바디 렌더링 (depth buffer)
            // GL.Clear 후, SpriteManager.Draw 전에 호출
            // 주의: HitObjectManager.Update는 retry(시간 역행) 시에만 spriteManager.Clear()를 하고
            // (매 프레임 아님, F9 교정) 매 프레임 스프라이트 윈도우를 갱신하므로,
            // HitBurst 등 다른 스프라이트는 이 뒤에 추가해야 순서가 맞는다
            if (HitObjectManager != null)
                HitObjectManager.Update(timeMs, PendingJudgements);

            // HitObjectManager.Update 후, SpriteManager.Draw 전에 HitBurst 추가
            if (PostHomUpdateCallback != null)
                PostHomUpdateCallback(timeMs);

            // HUD 렌더링 — SpriteManager.Draw 전에 스프라이트 추가
            if (PreDrawCallback != null)
                PreDrawCallback(timeMs);

            // SpriteManager Update + Draw (shader pipeline)
            SpriteManager.Update(timeMs);
            SpriteManager.Draw();

            // 게임플레이 스프라이트를 다 그린 뒤 HUD 즉시모드 도형을 위에 얹는다 (I-감사 #18).
            if (PostDrawCallback != null)
                PostDrawCallback(timeMs);
        }

        public void Dispose()
        {
            if (ShaderManager != null) ShaderManager.Dispose();
            if (TextureManager != null) TextureManager.Dispose();
            if (SpriteManager != null) SpriteManager.Dispose();
        }
    }
}