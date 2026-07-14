using System;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenTK;

namespace OsuEnlightenOverlay.Graphics.OpenGl
{
    /// <summary>
    /// 4바이트 색상 — System.Drawing.Color 대신 정점 데이터에 사용.
    /// System.Drawing.Color는 12+바이트이므로 GL 정점 레이아웃이 깨짐.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Color4b
    {
        public byte R, G, B, A;

        public Color4b(Color c)
        {
            R = c.R;
            G = c.G;
            B = c.B;
            A = c.A;
        }

        public static implicit operator Color4b(Color c)
        {
            return new Color4b(c);
        }
    }

    /// <summary>
    /// 정점 타입 — osu! stable Graphics/OpenGl/Vertex.cs 정확 포팅.
    /// TexturedVertex3d: 슬라이더 바디 (3D position + depth buffer).
    /// Vertex2d: 슬라이더 그라디언트 텍스처 생성 (ColourShader2D용).
    /// </summary>

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vertex2d : IEquatable<Vertex2d>
    {
        public Vector2 Position;
        public Color4b Colour;

        public bool Equals(Vertex2d other)
        {
            return Position.Equals(other.Position) && Colour.Equals(other.Colour);
        }

        public static readonly int Stride = Marshal.SizeOf(typeof(Vertex2d));
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TexturedVertex3d : IEquatable<TexturedVertex3d>
    {
        public Vector3 Position;
        public Color4b Colour;
        public Vector2 TexturePosition;

        public bool Equals(TexturedVertex3d other)
        {
            return Position.Equals(other.Position) && TexturePosition.Equals(other.TexturePosition) && Colour.Equals(other.Colour);
        }

        public static readonly int Stride = Marshal.SizeOf(typeof(TexturedVertex3d));
    }
}