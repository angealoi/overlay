using System;
using OpenTK;

namespace OsuEnlightenOverlay.Rendering
{
    /// <summary>
    /// GameField 좌표 변환 — ref/osu-stable/osu!/GameField.cs 직접 포팅.
    /// 512x384 게임필드 좌표 → 화면 좌표 변환.
    /// </summary>
    internal class GameField
    {
        public const int DEFAULT_WIDTH = 512;
        public const int DEFAULT_HEIGHT = 384;

        public float Width;
        public float Height;
        public float Ratio { get { return Height / DEFAULT_HEIGHT; } }
        public float ScaleFactor;
        public Vector2 OffsetVector1;
        public Vector2 OffsetVector1Widescreen;
        public bool CorrectionOffsetActive;

        public int windowWidth;
        public int windowHeight;
        float windowRatio;

        public GameField(int windowWidth, int windowHeight)
        {
            this.windowWidth = windowWidth;
            this.windowHeight = windowHeight;
            this.windowRatio = (float)windowHeight / 480f; // WindowManager.DEFAULT_HEIGHT = 480
            ScaleFactor = 1f;
            Update();
        }

        public void Update()
        {
            // CorrectionOffsetActive: Play 모드 + Osu 모드일 때 true
            // 오버레이는 항상 Play + Osu이므로 true
            CorrectionOffsetActive = true;

            Width = DEFAULT_WIDTH * windowRatio * ScaleFactor;
            Height = DEFAULT_HEIGHT * windowRatio * ScaleFactor;

            float modeOffset = CorrectionOffsetActive ? -16 * windowRatio : 0;

            OffsetVector1 = new Vector2(
                (windowWidth - Width) / 2f,
                (windowHeight - Height) / 4f * 3f + modeOffset);
            OffsetVector1Widescreen = new Vector2(
                0,
                (windowHeight - Height) / 4f * 3f + modeOffset);
        }

        public void Resize(int newWindowWidth, int newWindowHeight)
        {
            windowWidth = newWindowWidth;
            windowHeight = newWindowHeight;
            windowRatio = (float)windowHeight / 480f;
            Update();
        }

        /// <summary>
        /// 화면 좌표 → 게임필드 좌표
        /// </summary>
        public Vector2 DisplayToField(Vector2 pos)
        {
            return (pos - OffsetVector1) / Ratio;
        }

        /// <summary>
        /// 게임필드 좌표 → 화면 좌표
        /// </summary>
        public Vector2 FieldToDisplay(Vector2 pos)
        {
            return OffsetVector1 + pos * Ratio;
        }

        /// <summary>
        /// 게임필드 좌표 → 화면 좌표 (와이드스크린)
        /// </summary>
        public Vector2 FieldToDisplayWide(Vector2 pos)
        {
            return OffsetVector1Widescreen + pos * Ratio;
        }
    }
}