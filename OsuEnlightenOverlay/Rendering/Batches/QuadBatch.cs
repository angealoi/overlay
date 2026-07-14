using System;
using System.Drawing;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.Rendering.Sprites;

namespace OsuEnlightenOverlay.Rendering.Batches
{
    /// <summary>
    /// 쿼드(사각형) 배치 렌더링 — osu! stable Graphics/Batches/QuadBatch.cs 포팅.
    /// VBO 기반 정점 누적 → 일괄 드로우.
    /// </summary>
    internal class QuadBatch : IDisposable
    {
        const int MAX_VERTICES = 65536;

        // 정점 데이터: pos(2) + texCoord(2) + colour(4) = 8 floats per vertex
        float[] vertexData;
        int vertexCount;
        int vboId;
        bool vboInitialized;

        public QuadBatch()
        {
            vertexData = new float[MAX_VERTICES * 8];
            vertexCount = 0;
            vboInitialized = false;
        }

        public void Initialize()
        {
            if (vboInitialized) return;

            GL.GenBuffers(1, out vboId);
            vboInitialized = true;
        }

        /// <summary>
        /// 스프라이트 하나를 배치에 추가.
        /// 4개 정점 = 2개 삼각형 (쿼드).
        /// </summary>
        public void Add(pSprite sprite, Vector2 screenPos, float textureWidth, float textureHeight, float spriteScale, float drawTopRatio = 0, float drawHeightRatio = 1)
        {
            if (vertexCount + 6 > MAX_VERTICES)
            {
                Flush(null);
            }

            float scale = spriteScale;
            float w = textureWidth * scale;
            float h = textureHeight * scale;

            // Origin 오프셋 계산
            float ox = 0, oy = 0;
            switch (sprite.Origin)
            {
                case Origins.Centre: ox = -w / 2; oy = -h / 2; break;
                case Origins.TopLeft: ox = 0; oy = 0; break;
                case Origins.TopCentre: ox = -w / 2; oy = 0; break;
                case Origins.TopRight: ox = -w; oy = 0; break;
                case Origins.CentreLeft: ox = 0; oy = -h / 2; break;
                case Origins.CentreRight: ox = -w; oy = -h / 2; break;
                case Origins.BottomLeft: ox = 0; oy = -h; break;
                case Origins.BottomCentre: ox = -w / 2; oy = -h; break;
                case Origins.BottomRight: ox = -w; oy = -h; break;
            }

            // 4개 꼭지점 (회전 전)
            float cx = screenPos.X;
            float cy = screenPos.Y;
            float x0 = cx + ox;      // left
            float y0 = cy + oy;      // top
            float x1 = x0 + w;       // right
            float y1 = y0 + h;       // bottom

            // 회전 적용 — 4개 꼭지점 각각 회전
            if (sprite.CurrentRotation != 0)
            {
                float cos = (float)Math.Cos(sprite.CurrentRotation);
                float sin = (float)Math.Sin(sprite.CurrentRotation);
                RotateVertex(ref x0, ref y0, cx, cy, cos, sin);
                RotateVertex(ref x1, ref y1, cx, cy, cos, sin);
                // x0,y0는 좌상단, x1,y1은 우하단 — 회전 후에도 이 점들을 사용
            }

            float r = sprite.CurrentColour.R / 255f;
            float g = sprite.CurrentColour.G / 255f;
            float b = sprite.CurrentColour.B / 255f;
            float a = sprite.CurrentAlpha;

            // 텍스처 좌표 (flip 지원)
            // DrawTop/DrawHeight 비율 적용 — spinner metre에서 텍스처의 일부만 렌더링
            float u1 = sprite.FlipHorizontal ? 1 : 0;
            float u2 = sprite.FlipHorizontal ? 0 : 1;
            float v1, v2;
            if (sprite.FlipVertical)
            {
                v1 = 1 - (drawTopRatio + drawHeightRatio);
                v2 = 1 - drawTopRatio;
            }
            else
            {
                v1 = drawTopRatio;
                v2 = drawTopRatio + drawHeightRatio;
            }

            // 회전이 있으면 4개 꼭지점을 각각 회전시켜서 삼각형 추가
            if (sprite.CurrentRotation != 0)
            {
                float cos = (float)Math.Cos(sprite.CurrentRotation);
                float sin = (float)Math.Sin(sprite.CurrentRotation);

                // 4개 꼭지점 (Origin 적용 전, 회전 중심 = screenPos)
                float px0 = cx + ox, py0 = cy + oy;       // 좌상단
                float px1 = cx + ox + w, py1 = cy + oy;    // 우상단
                float px2 = cx + ox + w, py2 = cy + oy + h; // 우하단
                float px3 = cx + ox, py3 = cy + oy + h;    // 좌하단

                // 각 꼭지점 회전
                RotateVertex(ref px0, ref py0, cx, cy, cos, sin);
                RotateVertex(ref px1, ref py1, cx, cy, cos, sin);
                RotateVertex(ref px2, ref py2, cx, cy, cos, sin);
                RotateVertex(ref px3, ref py3, cx, cy, cos, sin);

                // 삼각형 1: (px0,py0) (px1,py1) (px3,py3) — UV: (u1,v1) (u2,v1) (u1,v2)
                AddVertex(px0, py0, u1, v1, r, g, b, a);
                AddVertex(px1, py1, u2, v1, r, g, b, a);
                AddVertex(px3, py3, u1, v2, r, g, b, a);
                // 삼각형 2: (px1,py1) (px2,py2) (px3,py3) — UV: (u2,v1) (u2,v2) (u1,v2)
                AddVertex(px1, py1, u2, v1, r, g, b, a);
                AddVertex(px2, py2, u2, v2, r, g, b, a);
                AddVertex(px3, py3, u1, v2, r, g, b, a);
            }
            else
            {
                // 회전 없음 — 단순 사각형
                AddVertex(x0, y0, u1, v1, r, g, b, a);
                AddVertex(x1, y0, u2, v1, r, g, b, a);
                AddVertex(x0, y1, u1, v2, r, g, b, a);
                AddVertex(x1, y0, u2, v1, r, g, b, a);
                AddVertex(x1, y1, u2, v2, r, g, b, a);
                AddVertex(x0, y1, u1, v2, r, g, b, a);
            }
        }

        void AddVertex(float x, float y, float u, float v, float r, float g, float b, float a)
        {
            int idx = vertexCount * 8;
            vertexData[idx] = x;
            vertexData[idx + 1] = y;
            vertexData[idx + 2] = u;
            vertexData[idx + 3] = v;
            vertexData[idx + 4] = r;
            vertexData[idx + 5] = g;
            vertexData[idx + 6] = b;
            vertexData[idx + 7] = a;
            vertexCount++;
        }

        void RotateVertex(ref float x, ref float y, float cx, float cy, float cos, float sin)
        {
            float dx = x - cx;
            float dy = y - cy;
            x = cx + dx * cos - dy * sin;
            y = cy + dx * sin + dy * cos;
        }

        /// <summary>
        /// 누적된 정점을 일괄 드로우.
        /// generic vertex attributes (glVertexAttribPointer) 사용.
        /// </summary>
        public void Flush(Shader shader)
        {
            if (vertexCount == 0) return;

            if (!vboInitialized) Initialize();

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboId);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vertexCount * 8 * sizeof(float)),
                vertexData, BufferUsageHint.DynamicDraw);

            int stride = 8 * sizeof(float);

            // generic vertex attributes — 셰이더 기반
            if (shader != null && shader.IsValid)
            {
                if (shader.PositionLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.PositionLoc);
                    GL.VertexAttribPointer(shader.PositionLoc, 2, VertexAttribPointerType.Float, false, stride, IntPtr.Zero);
                }
                if (shader.TexCoordLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.TexCoordLoc);
                    GL.VertexAttribPointer(shader.TexCoordLoc, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)(2 * sizeof(float)));
                }
                if (shader.ColourAttrLoc >= 0)
                {
                    GL.EnableVertexAttribArray(shader.ColourAttrLoc);
                    GL.VertexAttribPointer(shader.ColourAttrLoc, 4, VertexAttribPointerType.Float, false, stride, (IntPtr)(4 * sizeof(float)));
                }

                GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);

                if (shader.PositionLoc >= 0) GL.DisableVertexAttribArray(shader.PositionLoc);
                if (shader.TexCoordLoc >= 0) GL.DisableVertexAttribArray(shader.TexCoordLoc);
                if (shader.ColourAttrLoc >= 0) GL.DisableVertexAttribArray(shader.ColourAttrLoc);
            }
            else
            {
                // fallback: fixed-function (셰이더 없을 때)
                GL.EnableClientState(ArrayCap.VertexArray);
                GL.EnableClientState(ArrayCap.TextureCoordArray);
                GL.EnableClientState(ArrayCap.ColorArray);

                GL.VertexPointer(2, VertexPointerType.Float, stride, IntPtr.Zero);
                GL.TexCoordPointer(2, TexCoordPointerType.Float, stride, (IntPtr)(2 * sizeof(float)));
                GL.ColorPointer(4, ColorPointerType.Float, stride, (IntPtr)(4 * sizeof(float)));

                GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);

                GL.DisableClientState(ArrayCap.ColorArray);
                GL.DisableClientState(ArrayCap.TextureCoordArray);
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
}