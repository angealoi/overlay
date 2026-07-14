using System;
using System.Drawing;

namespace OsuEnlightenOverlay.Helpers
{
    /// <summary>
    /// 색상 헬퍼 — osu! stable Helpers/ColourHelper.cs 정확 포팅.
    /// </summary>
    internal static class ColourHelper
    {
        /// <summary>
        /// Lightens a colour in a way more friendly to dark or strong colours.
        /// osu! stable ColourHelper.Lighten2.
        /// </summary>
        public static Color Lighten2(Color color, float amount)
        {
            amount *= 0.5f;
            return Color.FromArgb(
                color.A,
                (byte)Math.Min(255, color.R * (1 + 0.5f * amount) + 255.0f * amount),
                (byte)Math.Min(255, color.G * (1 + 0.5f * amount) + 255.0f * amount),
                (byte)Math.Min(255, color.B * (1 + 0.5f * amount) + 255.0f * amount));
        }

        /// <summary>
        /// Returns a darkened version of the colour.
        /// osu! stable ColourHelper.Darken.
        /// </summary>
        public static Color Darken(Color color, float amount)
        {
            return Color.FromArgb(
                color.A,
                (byte)Math.Min(255, color.R / (1 + amount)),
                (byte)Math.Min(255, color.G / (1 + amount)),
                (byte)Math.Min(255, color.B / (1 + amount)));
        }

        /// <summary>
        /// Lightens a colour.
        /// osu! stable ColourHelper.Lighten.
        /// </summary>
        public static Color Lighten(Color color, float amount)
        {
            return Color.FromArgb(
                color.A,
                (byte)Math.Min(255, color.R * (1 + amount)),
                (byte)Math.Min(255, color.G * (1 + amount)),
                (byte)Math.Min(255, color.B * (1 + amount)));
        }

        /// <summary>
        /// 선형 보간 — osu! stable ColourHelper.ColourLerp.
        /// </summary>
        public static Color ColourLerp(Color first, Color second, float weight)
        {
            return Color.FromArgb(
                (byte)Lerp(first.A, second.A, weight),
                (byte)Lerp(first.R, second.R, weight),
                (byte)Lerp(first.G, second.G, weight),
                (byte)Lerp(first.B, second.B, weight));
        }

        static float Lerp(float a, float b, float weight)
        {
            return a + (b - a) * weight;
        }

        /// <summary>
        /// 알파 변경 — osu! stable ColourHelper.ChangeAlpha.
        /// </summary>
        public static Color ChangeAlpha(Color c, byte alphaByte)
        {
            return Color.FromArgb(alphaByte, c.R, c.G, c.B);
        }
    }
}