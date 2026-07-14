using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Skinning;

namespace OsuEnlightenOverlay.Rendering.Sprites
{
    /// <summary>
    /// 루프 타입 — osu! stable LoopTypes enum.
    /// </summary>
    public enum LoopTypes
    {
        LoopForever,
        LoopOnce
    }

    /// <summary>
    /// 애니메이션 스프라이트 — osu! stable Graphics/Sprites/pAnimation.cs 정확 포팅.
    /// pSprite 상속, 프레임 배열 + FrameDelay 기반 애니메이션.
    /// </summary>
    internal class pAnimation : pSprite
    {
        public bool DrawDimensionsManualOverride = false;
        public LoopTypes LoopType;
        public bool RunAnimation = true;
        public int TextureCount;

        private int currentFrame;
        private int textureFrame;
        private double frameDelay = 1000.0 / 60.0; // GameBase.SIXTY_FRAME_TIME
        private double animationStartTime;
        private bool firstFrame = true;
        private bool finished = false;
        private pTexture[] textureArray;
        private int[] customSequence;

        /// <summary>
        /// TrackRotation — 슬라이더 볼처럼 이동 방향으로 회전.
        /// osu! stable pSprite.TrackRotation.
        /// </summary>
        public bool TrackRotation;

        /// <summary>
        /// Reverse — 애니메이션 역방향 재생.
        /// osu! stable pSprite.Reverse (virtual, pAnimation override).
        /// </summary>
        public bool Reverse;

        public pAnimation(pTexture[] textures, Fields fieldType, Origins origin, Clocks clock,
                          Vector2 startPosition, float drawDepth, bool alwaysDraw, Color colour)
            : base(
                textures == null || textures.Length == 0 ? null : textures[0], fieldType, origin, clock,
                startPosition, drawDepth, alwaysDraw, colour)
        {
            if (textures != null)
                TextureArray = textures;
        }

        public int MaxFrame
        {
            get { return !HasCustomSequence ? TextureCount - 1 : CustomSequence.Length - 1; }
        }

        public double FrameDelay
        {
            get { return frameDelay; }
            set
            {
                if (frameDelay == value)
                    return;

                // osu! stable: frameDelay 변경 시 현재 프레임 유지
                if (!firstFrame && frameDelay > 0)
                {
                    double time = lastUpdateTime;
                    if (animationStartTime < time)
                    {
                        double frame = (time - animationStartTime) / frameDelay;
                        animationStartTime = LoopType == LoopTypes.LoopOnce && frame > MaxFrame
                            ? animationStartTime + MaxFrame * frameDelay - MaxFrame * value
                            : time - frame * value;
                    }
                }

                frameDelay = value;
            }
        }

        public pTexture[] TextureArray
        {
            get { return textureArray; }
            set
            {
                if (value == textureArray) return;

                textureArray = value;

                if (textureArray != null)
                {
                    TextureCount = textureArray.Length;
                    if (TextureCount > 0)
                        Texture = textureArray[0];
                    else
                        Texture = null;
                }
                else
                    TextureCount = 0;

                textureFrame = 0;
                ResetAnimation();
            }
        }

        public int[] CustomSequence
        {
            get { return customSequence; }
            set
            {
                if (customSequence == value)
                    return;

                customSequence = value;
                ResetAnimation();
            }
        }

        public bool HasCustomSequence { get { return CustomSequence != null; } }

        public int CurrentFrame
        {
            get { return currentFrame; }
            set
            {
                if (value < 0) value = 0;

                currentFrame = LoopType == LoopTypes.LoopOnce
                    ? Math.Min(value, MaxFrame)
                    : value % (MaxFrame + 1);

                // osu! stable: 시작 시간 조정으로 현재 프레임 유지
                double time = lastUpdateTime;
                int frame = Reverse ? (value - currentFrame) + (MaxFrame - currentFrame) : value;
                animationStartTime = time - frame * frameDelay;

                firstFrame = false;
            }
        }

        /// <summary>
        /// AnimationFramerate 기반 프레임 속도 설정.
        /// osu! stable pAnimation.SetFramerateFromSkin.
        /// </summary>
        public void SetFramerateFromSkin()
        {
            if (textureArray == null) return;

            if (SkinManager.Current != null && SkinManager.Current.AnimationFramerate > 0)
                FrameDelay = 1000f / SkinManager.Current.AnimationFramerate;
            else
                FrameDelay = 1000f / TextureArray.Length;
        }

        public void ResetAnimation()
        {
            firstFrame = true;
            finished = false;
            currentFrame = Reverse ? MaxFrame : 0;
        }

        private int lastUpdateTime;

        /// <summary>
        /// 프레임 업데이트 — osu! stable pAnimation.UpdateFrame 포팅.
        /// pSprite.Update 호출 후 호출.
        /// </summary>
        public void UpdateFrame(int time)
        {
            lastUpdateTime = time;

            if (TextureCount < 2)
                return;

            if (RunAnimation && frameDelay > 0)
            {
                if (!firstFrame && time < animationStartTime)
                    ResetAnimation();

                if (firstFrame)
                {
                    animationStartTime = Transformations.Count > 0 ? Transformations[0].Time1 : time;
                    firstFrame = false;
                }

                double animationTime = time - animationStartTime;
                int frame = (int)Math.Floor(animationTime / frameDelay);

                if (frame < 0)
                    frame = 0;

                bool justFinished = false;
                if (LoopType == LoopTypes.LoopOnce)
                {
                    if (frame > MaxFrame)
                    {
                        frame = MaxFrame;
                        if (!finished)
                            justFinished = finished = true;
                    }
                    else
                    {
                        finished = false;
                    }
                }
                else
                {
                    frame %= MaxFrame + 1;
                    finished = false;
                }

                currentFrame = Reverse ? MaxFrame - frame : frame;
            }
            else
            {
                // 애니메이션 정지 시 — 재개 시 올바른 프레임에서 시작하도록 설정
                int clamped = currentFrame;
                if (clamped < 0) clamped = 0;
                if (clamped > MaxFrame) clamped = MaxFrame;
                currentFrame = clamped;
            }

            if (textureFrame != currentFrame)
            {
                Texture = HasCustomSequence
                    ? textureArray[Math.Min(textureArray.Length - 1, customSequence[currentFrame])]
                    : textureArray[currentFrame];

                textureFrame = currentFrame;
            }
        }

        /// <summary>
        /// pSprite.Update 오버라이드 — UpdateFrame 호출 추가.
        /// osu! stable: pAnimation.Update() → base.Update() + UpdateFrame().
        /// </summary>
        public override UpdateResult Update(int time)
        {
            UpdateResult result = base.Update(time);
            if (result == UpdateResult.Visible)
                UpdateFrame(time);
            return result;
        }
    }
}