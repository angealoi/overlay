using System;
using OpenTK;

namespace OsuEnlightenOverlay.Rendering.Sprites
{
    /// <summary>
    /// 이징 타입 — osu! stable Graphics/Sprites/Transformation.cs 포팅.
    /// http://easings.net 참조.
    /// </summary>
    public enum EasingTypes
    {
        None,
        Out,
        In,
        InQuad,
        OutQuad,
        InOutQuad,
        InCubic,
        OutCubic,
        InOutCubic,
        InQuart,
        OutQuart,
        InOutQuart,
        InQuint,
        OutQuint,
        InOutQuint,
        InSine,
        OutSine,
        InOutSine,
        InExpo,
        OutExpo,
        InOutExpo,
        InCirc,
        OutCirc,
        InOutCirc,
        InElastic,
        OutElastic,
        OutElasticHalf,
        OutElasticQuarter,
        InOutElastic,
        InBack,
        OutBack,
        InOutBack,
        InBounce,
        OutBounce,
        InOutBounce
    }

    /// <summary>
    /// 변환 타입 — osu! stable TransformationType 포팅.
    /// </summary>
    [Flags]
    public enum TransformationType
    {
        None = 0,
        Movement = 1 << 0,
        Fade = 1 << 1,
        Scale = 1 << 2,
        Rotation = 1 << 3,
        Colour = 1 << 4,
        ParameterFlipHorizontal = 1 << 5,
        ParameterFlipVertical = 1 << 6,
        MovementX = 1 << 7,
        MovementY = 1 << 8,
        VectorScale = 1 << 9,
        ParameterAdditive = 1 << 10,
        ClippingWidth = 1 << 11,
        ClippingHeight = 1 << 12,
        ClipRectangle = 1 << 13,
        Blur = 1 << 14
    }

    /// <summary>
    /// 시간 기반 애니메이션 보간 — osu! stable Transformation.cs 포팅.
    /// </summary>
    public class Transformation : IComparable<Transformation>
    {
        public float StartFloat;
        public float EndFloat;
        public Vector2 StartVector;
        public Vector2 EndVector;
        public int Time1;
        public int Time2;
        public TransformationType Type;
        public EasingTypes Easing;
        public byte TagNumeric;
        public bool Loop;

        public Transformation(TransformationType type, float startFloat, float endFloat, int time1, int time2, EasingTypes easing)
        {
            Type = type;
            StartFloat = startFloat;
            EndFloat = endFloat;
            Time1 = time1;
            Time2 = time2;
            Easing = easing;
        }

        public Transformation(TransformationType type, float startFloat, float endFloat, int time1, int time2)
            : this(type, startFloat, endFloat, time1, time2, EasingTypes.None)
        {
        }

        /// <summary>
        /// Movement 생성자 — osu-stable Transformation(Vector2, Vector2, int, int, EasingTypes).
        /// </summary>
        public Transformation(TransformationType type, Vector2 startVector, Vector2 endVector, int time1, int time2, EasingTypes easing)
        {
            Type = type;
            StartVector = startVector;
            EndVector = endVector;
            Time1 = time1;
            Time2 = time2;
            Easing = easing;
        }

        /// <summary>
        /// 현재 시간에서의 Vector2 보간값 계산 (Movement용).
        /// </summary>
        public Vector2 GetVectorAt(int time)
        {
            if (time <= Time1) return StartVector;
            if (time >= Time2) return EndVector;
            double current = time - Time1;
            double duration = Time2 - Time1;
            float progress = (float)ApplyEasing(Easing, current, 0, 1, duration);
            return StartVector + (EndVector - StartVector) * progress;
        }

        public int Duration { get { return Time2 - Time1; } }

        public int CompareTo(Transformation other)
        {
            int compare = Time1.CompareTo(other.Time1);
            if (compare != 0) return compare;
            compare = Time2.CompareTo(other.Time2);
            if (compare != 0) return compare;
            return Type.CompareTo(other.Type);
        }

        public Transformation Clone()
        {
            return (Transformation)MemberwiseClone();
        }

        /// <summary>
        /// 현재 시간에서의 보간값 계산.
        /// </summary>
        public float GetValueAt(int time)
        {
            if (time <= Time1) return StartFloat;
            if (time >= Time2) return EndFloat;
            double current = time - Time1;
            double duration = Time2 - Time1;
            return (float)ApplyEasing(Easing, current, StartFloat, EndFloat - StartFloat, duration);
        }

        /// <summary>
        /// 이징 함수 적용 — osu! stable OsuMathHelper.ApplyEasing 포팅.
        /// </summary>
        public static double ApplyEasing(EasingTypes easing, double time, double initial, double change, double duration)
        {
            if (change == 0 || time == 0 || duration == 0) return initial;
            if (time == duration) return initial + change;

            double Pi = Math.PI;
            switch (easing)
            {
                default:
                    return change * (time / duration) + initial;
                case EasingTypes.In:
                case EasingTypes.InQuad:
                    return change * (time /= duration) * time + initial;
                case EasingTypes.Out:
                case EasingTypes.OutQuad:
                    return -change * (time /= duration) * (time - 2) + initial;
                case EasingTypes.InOutQuad:
                    if ((time /= duration / 2) < 1) return change / 2 * time * time + initial;
                    return -change / 2 * ((--time) * (time - 2) - 1) + initial;
                case EasingTypes.InCubic:
                    return change * (time /= duration) * time * time + initial;
                case EasingTypes.OutCubic:
                    return change * ((time = time / duration - 1) * time * time + 1) + initial;
                case EasingTypes.InOutCubic:
                    if ((time /= duration / 2) < 1) return change / 2 * time * time * time + initial;
                    return change / 2 * ((time -= 2) * time * time + 2) + initial;
                case EasingTypes.InQuart:
                    return change * (time /= duration) * time * time * time + initial;
                case EasingTypes.OutQuart:
                    return -change * ((time = time / duration - 1) * time * time * time - 1) + initial;
                case EasingTypes.InOutQuart:
                    if ((time /= duration / 2) < 1) return change / 2 * time * time * time * time + initial;
                    return -change / 2 * ((time -= 2) * time * time * time - 2) + initial;
                case EasingTypes.InQuint:
                    return change * (time /= duration) * time * time * time * time + initial;
                case EasingTypes.OutQuint:
                    return change * ((time = time / duration - 1) * time * time * time * time + 1) + initial;
                case EasingTypes.InOutQuint:
                    if ((time /= duration / 2) < 1) return change / 2 * time * time * time * time * time + initial;
                    return change / 2 * ((time -= 2) * time * time * time * time + 2) + initial;
                case EasingTypes.InSine:
                    return -change * Math.Cos(time / duration * (Pi / 2)) + change + initial;
                case EasingTypes.OutSine:
                    return change * Math.Sin(time / duration * (Pi / 2)) + initial;
                case EasingTypes.InOutSine:
                    return -change / 2 * (Math.Cos(Pi * time / duration) - 1) + initial;
                case EasingTypes.InExpo:
                    return change * Math.Pow(2, 10 * (time / duration - 1)) + initial;
                case EasingTypes.OutExpo:
                    return (time == duration) ? initial + change : change * (-Math.Pow(2, -10 * time / duration) + 1) + initial;
                case EasingTypes.InOutExpo:
                    if ((time /= duration / 2) < 1) return change / 2 * Math.Pow(2, 10 * (time - 1)) + initial;
                    return change / 2 * (-Math.Pow(2, -10 * --time) + 2) + initial;
                case EasingTypes.InCirc:
                    return -change * (Math.Sqrt(1 - (time /= duration) * time) - 1) + initial;
                case EasingTypes.OutCirc:
                    return change * Math.Sqrt(1 - (time = time / duration - 1) * time) + initial;
                case EasingTypes.InOutCirc:
                    if ((time /= duration / 2) < 1) return -change / 2 * (Math.Sqrt(1 - time * time) - 1) + initial;
                    return change / 2 * (Math.Sqrt(1 - (time -= 2) * time) + 1) + initial;
                case EasingTypes.OutBack:
                    double s = 1.70158;
                    return change * ((time = time / duration - 1) * time * ((s + 1) * time + s) + 1) + initial;
                case EasingTypes.InBack:
                    s = 1.70158;
                    return change * (time /= duration) * time * ((s + 1) * time - s) + initial;
                case EasingTypes.InBounce:
                    return change * BounceEase(time / duration) + initial;
                case EasingTypes.OutBounce:
                    return change * (1 - BounceEase(1 - time / duration)) + initial;
            }
        }

        static double BounceEase(double t)
        {
            if (t < 1 / 2.75) return 7.5625 * t * t;
            if (t < 2 / 2.75) return 7.5625 * (t -= 1.5 / 2.75) * t + 0.75;
            if (t < 2.5 / 2.75) return 7.5625 * (t -= 2.25 / 2.75) * t + 0.9375;
            return 7.5625 * (t -= 2.625 / 2.75) * t + 0.984375;
        }
    }
}