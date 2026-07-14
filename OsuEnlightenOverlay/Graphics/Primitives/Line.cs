using System;
using OpenTK;

namespace OsuEnlightenOverlay.Graphics.Primitives
{
    /// <summary>
    /// 선분 — osu! stable Graphics/Primitives/Line.cs 정확 포팅.
    /// p1, p2, rho(길이), theta(방향), WorldMatrix, EndWorldMatrix.
    /// 슬라이더 바디 렌더링에 사용.
    /// </summary>
    internal class Line : ICloneable
    {
        /// <summary>
        /// Begin point of the line.
        /// </summary>
        public Vector2 p1;

        /// <summary>
        /// End point of the line.
        /// </summary>
        public Vector2 p2;

        /// <summary>
        /// This line's endpoint is important so always include it in optimizations.
        /// </summary>
        public bool forceEnd = false;

        /// <summary>
        /// This line is part of a longer straight line so it's subject to more optimizations.
        /// </summary>
        public bool straight = false;

        /// <summary>
        /// The length of the line.
        /// </summary>
        public float rho
        {
            get { return (p2 - p1).Length; }
        }

        /// <summary>
        /// The direction of the second point from the first.
        /// </summary>
        public float theta
        {
            get { return (float)Math.Atan2(p2.Y - p1.Y, p2.X - p1.X); }
        }

        public Line(Vector2 p1, Vector2 p2)
        {
            this.p1 = p1;
            this.p2 = p2;
        }

        /// <summary>
        /// WorldMatrix — 회전(theta) + 이동(p1).
        /// osu! stable: Matrix.CreateRotationZ(theta) * Matrix.CreateTranslation(p1.X, p1.Y, 0)
        /// OpenTK = XNA: rotate * translate → rotate 먼저, translate 나중
        /// </summary>
        public Matrix4 WorldMatrix()
        {
            Matrix4 rotate = Matrix4.CreateRotationZ(theta);
            Matrix4 translate = Matrix4.CreateTranslation(p1.X, p1.Y, 0);
            return rotate * translate;
        }

        /// <summary>
        /// EndWorldMatrix — 회전(theta) + 이동(p2).
        /// osu! stable: Matrix.CreateRotationZ(theta) * Matrix.CreateTranslation(p2.X, p2.Y, 0)
        /// OpenTK = XNA: rotate * translate → rotate 먼저, translate 나중
        /// </summary>
        public Matrix4 EndWorldMatrix()
        {
            Matrix4 rotate = Matrix4.CreateRotationZ(theta);
            Matrix4 translate = Matrix4.CreateTranslation(p2.X, p2.Y, 0);
            return rotate * translate;
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}