using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace OsuEnlightenOverlay.Graphics.OpenGl
{
    /// <summary>
    /// FBO 렌더 타겟 — osu! stable Graphics/OpenGl/RenderTarget2D.cs 정확 포팅.
    /// desktop GL (OpenTK.Graphics.OpenGL) 사용.
    /// 슬라이더 바디 그라디언트 텍스처 생성에 사용.
    /// </summary>
    internal class RenderTarget2D : IDisposable
    {
        /// <summary>
        /// 생성된 텍스처 ID (FBO에 연결된 컬러 첨부).
        /// </summary>
        public int TextureId { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        int frameBuffer = -1;
        int renderBuffer = -1;
        bool hasDepth;
        bool ownsTexture; // dispose 시 텍스처 삭제 여부

        // 이전 viewport/ortho 저장
        int[] prevViewport = new int[4];
        bool bound;

        /// <summary>
        /// FBO 사용 가능 여부 — desktop GL에서는 항상 true.
        /// osu! stable CanUseFBO 포팅.
        /// </summary>
        public static bool CanUseFBO = true;

        public RenderTarget2D(int width, int height, bool depthBuffer = false, bool ownsTexture = true)
        {
            Width = width;
            Height = height;
            hasDepth = depthBuffer;
            this.ownsTexture = ownsTexture;

            // 텍스처 생성 + 할당
            int texId;
            GL.GenTextures(1, out texId);
            TextureId = texId;
            GL.BindTexture(TextureTarget.Texture2D, TextureId);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            if (CanUseFBO)
            {
                frameBuffer = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);

                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, TextureId, 0);

                if (depthBuffer)
                {
                    renderBuffer = GL.GenRenderbuffer();
                    GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderBuffer);
                    GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent16, width, height);
                    GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                        RenderbufferTarget.Renderbuffer, renderBuffer);
                }

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
        }

        /// <summary>
        /// FBO 바인딩 — 렌더링 대상을 이 텍스처로 변경.
        /// </summary>
        public void Bind()
        {
            if (bound) return;

            // 이전 viewport 저장
            GL.GetInteger(GetPName.Viewport, prevViewport);

            if (CanUseFBO)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.Viewport(0, 0, Width, Height);

            bound = true;
        }

        /// <summary>
        /// FBO 언바인딩 — 이전 렌더링 대상으로 복원.
        /// </summary>
        public void Unbind()
        {
            if (!bound) return;

            if (CanUseFBO)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
            else
            {
                // 백버버에서 복사 — osu! stable fallback
                GL.BindTexture(TextureTarget.Texture2D, TextureId);
                GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 0, 0, Width, Height, 0);
            }

            // viewport 복원
            GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);

            bound = false;
        }

        public void Dispose()
        {
            if (ownsTexture && TextureId > 0)
            {
                int id = TextureId;
                GL.DeleteTextures(1, ref id);
                TextureId = 0;
            }
            if (frameBuffer != -1)
            {
                GL.DeleteFramebuffer(frameBuffer);
                frameBuffer = -1;
            }
            if (renderBuffer != -1)
            {
                GL.DeleteRenderbuffer(renderBuffer);
                renderBuffer = -1;
            }
        }
    }
}