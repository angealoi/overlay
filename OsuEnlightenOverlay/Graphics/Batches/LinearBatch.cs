using System;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.Graphics.OpenGl;
using OsuEnlightenOverlay.Rendering;

namespace OsuEnlightenOverlay.Graphics.Batches
{
    /// <summary>
    /// 선형 배치 — osu! stable Graphics/Batches/LinearBatch.cs 포팅.
    /// Vertex2d (Position + Colour) 정점을 LineStrip 등으로 렌더링.
    /// 슬라이더 그라디언트 텍스처 생성에 사용 (ColourShader2D).
    /// </summary>
    internal class LinearBatch : IDisposable
    {
        Vertex2d[] vertices;
        int vertexCount;
        int vboId;
        bool vboInitialized;
        PrimitiveType type;

        public LinearBatch(int size, PrimitiveType type)
        {
            vertices = new Vertex2d[size];
            this.type = type;
        }

        public void Initialize()
        {
            if (vboInitialized) return;
            GL.GenBuffers(1, out vboId);
            vboInitialized = true;
        }

        public void Add(Vertex2d vertex)
        {
            if (vertexCount >= vertices.Length)
            {
                Draw(null);
            }
            vertices[vertexCount++] = vertex;
        }

        /// <summary>
        /// 누적된 정점을 드로우 — ColourShader2D용.
        /// </summary>
        public void Draw(Shader shader)
        {
            if (vertexCount == 0) return;
            if (!vboInitialized) Initialize();

            int stride = Vertex2d.Stride;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboId);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vertexCount * stride),
                vertices, BufferUsageHint.DynamicDraw);

            if (shader != null && shader.IsValid)
            {
                if (shader.PositionLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.PositionLoc);
                    GL.VertexAttribPointer(shader.PositionLoc, 2, VertexAttribPointerType.Float, false, stride, IntPtr.Zero);
                }
                if (shader.ColourAttrLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.ColourAttrLoc);
                    GL.VertexAttribPointer(shader.ColourAttrLoc, 4, VertexAttribPointerType.UnsignedByte, true, stride, (IntPtr)(2 * sizeof(float)));
                }

                GL.DrawArrays(type, 0, vertexCount);

                if (shader.PositionLoc >= 0) GL.DisableVertexAttribArray(shader.PositionLoc);
                if (shader.ColourAttrLoc >= 0) GL.DisableVertexAttribArray(shader.ColourAttrLoc);
            }
            else
            {
                // fixed-function fallback
                GL.EnableClientState(ArrayCap.VertexArray);
                GL.EnableClientState(ArrayCap.ColorArray);

                GL.VertexPointer(2, VertexPointerType.Float, stride, IntPtr.Zero);
                GL.ColorPointer(4, ColorPointerType.UnsignedByte, stride, (IntPtr)(2 * sizeof(float)));

                GL.DrawArrays(type, 0, vertexCount);

                GL.DisableClientState(ArrayCap.ColorArray);
                GL.DisableClientState(ArrayCap.VertexArray);
            }

            vertexCount = 0;
        }

        public void Dispose()
        {
            if (vboInitialized && vboId > 0)
            {
                GL.DeleteBuffers(1, ref vboId);
                vboId = 0;
                vboInitialized = false;
            }
        }
    }

    /// <summary>
    /// 3D 쿼드 배치 — osu! stable Graphics/Batches/QuadBatch.cs 포팅.
    /// TexturedVertex3d (Position3 + Colour + TexCoord2) 정점을 Triangles로 렌더링.
    /// 슬라이더 바디 렌더링에 사용 (TextureShader3D + depth buffer).
    /// </summary>
    internal class QuadBatch3D : IDisposable
    {
        TexturedVertex3d[] vertices;
        int vertexCount;
        int vboId;
        bool vboInitialized;
        Shader autoFlushShader;

        public QuadBatch3D(int size)
        {
            vertices = new TexturedVertex3d[size];
        }

        public void Initialize()
        {
            if (vboInitialized) return;
            GL.GenBuffers(1, out vboId);
            vboInitialized = true;
        }

        public void Add(TexturedVertex3d vertex)
        {
            if (vertexCount >= vertices.Length)
            {
                Draw(autoFlushShader);
            }
            vertices[vertexCount++] = vertex;
        }

        /// <summary>
        /// 누적된 정점을 드로우 — TextureShader3D용.
        /// </summary>
        public void Draw(Shader shader)
        {
            if (vertexCount == 0) return;
            if (!vboInitialized) Initialize();

            autoFlushShader = shader;

            int stride = TexturedVertex3d.Stride;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboId);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vertexCount * stride),
                vertices, BufferUsageHint.DynamicDraw);

            if (shader != null && shader.IsValid)
            {
                if (shader.PositionLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.PositionLoc);
                    GL.VertexAttribPointer(shader.PositionLoc, 3, VertexAttribPointerType.Float, false, stride, IntPtr.Zero);
                }
                if (shader.ColourAttrLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.ColourAttrLoc);
                    GL.VertexAttribPointer(shader.ColourAttrLoc, 4, VertexAttribPointerType.UnsignedByte, true, stride, (IntPtr)(3 * sizeof(float)));
                }
                if (shader.TexCoordLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.TexCoordLoc);
                    GL.VertexAttribPointer(shader.TexCoordLoc, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)(3 * sizeof(float) + 4));
                }

                GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);

                if (shader.PositionLoc >= 0) GL.DisableVertexAttribArray(shader.PositionLoc);
                if (shader.ColourAttrLoc >= 0) GL.DisableVertexAttribArray(shader.ColourAttrLoc);
                if (shader.TexCoordLoc >= 0) GL.DisableVertexAttribArray(shader.TexCoordLoc);
            }

            vertexCount = 0;
        }

        public void Dispose()
        {
            if (vboInitialized && vboId > 0)
            {
                GL.DeleteBuffers(1, ref vboId);
                vboId = 0;
                vboInitialized = false;
            }
        }
    }

    /// <summary>
    /// 3D 선형 배치 — 슬라이더 반원 캡용.
    /// TexturedVertex3d를 Triangles로 렌더링.
    /// </summary>
    internal class LinearBatch3D : IDisposable
    {
        TexturedVertex3d[] vertices;
        int vertexCount;
        int vboId;
        bool vboInitialized;
        Shader autoFlushShader;

        public LinearBatch3D(int size)
        {
            vertices = new TexturedVertex3d[size];
        }

        public void Initialize()
        {
            if (vboInitialized) return;
            GL.GenBuffers(1, out vboId);
            vboInitialized = true;
        }

        public void Add(TexturedVertex3d vertex)
        {
            if (vertexCount >= vertices.Length)
            {
                Draw(autoFlushShader);
            }
            vertices[vertexCount++] = vertex;
        }

        public void Draw(Shader shader)
        {
            if (vertexCount == 0) return;
            if (!vboInitialized) Initialize();

            autoFlushShader = shader;

            int stride = TexturedVertex3d.Stride;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboId);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vertexCount * stride),
                vertices, BufferUsageHint.DynamicDraw);

            if (shader != null && shader.IsValid)
            {
                if (shader.PositionLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.PositionLoc);
                    GL.VertexAttribPointer(shader.PositionLoc, 3, VertexAttribPointerType.Float, false, stride, IntPtr.Zero);
                }
                if (shader.ColourAttrLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.ColourAttrLoc);
                    GL.VertexAttribPointer(shader.ColourAttrLoc, 4, VertexAttribPointerType.UnsignedByte, true, stride, (IntPtr)(3 * sizeof(float)));
                }
                if (shader.TexCoordLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.TexCoordLoc);
                    GL.VertexAttribPointer(shader.TexCoordLoc, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)(3 * sizeof(float) + 4));
                }

                GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);

                if (shader.PositionLoc >= 0) GL.DisableVertexAttribArray(shader.PositionLoc);
                if (shader.ColourAttrLoc >= 0) GL.DisableVertexAttribArray(shader.ColourAttrLoc);
                if (shader.TexCoordLoc >= 0) GL.DisableVertexAttribArray(shader.TexCoordLoc);
            }

            vertexCount = 0;
        }

        public void Dispose()
        {
            if (vboInitialized && vboId > 0)
            {
                GL.DeleteBuffers(1, ref vboId);
                vboId = 0;
                vboInitialized = false;
            }
        }
    }
}