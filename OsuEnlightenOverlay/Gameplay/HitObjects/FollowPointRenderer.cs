using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Rendering;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Skinning;

namespace OsuEnlightenOverlay.Gameplay.HitObjects
{
    /// <summary>
    /// 팔로우포인트 — osu!lazer FollowPointRenderer / FollowPointConnection / FollowPoint 풀링.
    ///
    /// 맵 전체 pAnimation을 미리 만들지 않는다. 연결(LifetimeEntry)만 들고,
    /// 라이프타임에 들어온 연결만 FollowPoint 풀에서 점을 꺼내 SpriteManager에 넣는다.
    /// 풀 크기: 연결 200, 점 50→1000 (lazer DrawablePool과 동일).
    ///
    /// 점의 페이드/스케일 트랜스폼은 오버레이가 stable 위에 그리므로
    /// 기존 stable AddFollowPoints(IsDefault일 때만 Scale+Movement)를 유지한다.
    /// </summary>
    internal class FollowPointRenderer
    {
        // lazer FollowPointConnection
        public const int Spacing = 32;
        public const int Preempt = 800;
        const int FadeInDuration = 400;

        // lazer FollowPointRenderer.load: new DrawablePool<FollowPointConnection>(10, 200)
        //                                      new DrawablePool<FollowPoint>(50, 1000)
        const int PointPoolInitial = 50;
        const int PointPoolMax = 1000;

        readonly SpriteManager spriteManager;
        readonly TextureManager textureManager;
        readonly List<LifetimeEntry> entries = new List<LifetimeEntry>();
        readonly List<LifetimeEntry> alive = new List<LifetimeEntry>();
        readonly Stack<pAnimation> pointPool = new Stack<pAnimation>();

        pTexture[] textures;
        int nextIndex;
        int lastTime = int.MinValue;

        public FollowPointRenderer(SpriteManager spriteManager, TextureManager textureManager)
        {
            this.spriteManager = spriteManager;
            this.textureManager = textureManager;
        }

        /// <summary>
        /// lazer FollowPointRenderer.AddFollowPoints를 맵 로드 때 한 번에.
        /// 연결 엔트리만 만들고 drawable은 만들지 않는다.
        /// </summary>
        public void Rebuild(BeatmapData beatmap, DifficultyValues difficulty)
        {
            FreeAll();
            entries.Clear();
            nextIndex = 0;
            lastTime = int.MinValue;

            pointPool.Clear();
            textures = textureManager.LoadAll("followpoint");
            if (textures == null || textures.Length == 0)
                return;

            WarmPool();

            List<ObjectRef> objects = new List<ObjectRef>(beatmap.HitObjects.Count);
            foreach (HitObjectData d in beatmap.HitObjects)
            {
                bool isSlider = (d.Type & HitObjectType.Slider) != 0;
                Vector2 stackedEnd = d.Position;
                if (isSlider && d.SliderComputed)
                {
                    Vector2 stackOffset = new Vector2(
                        d.StackCount * difficulty.StackOffset,
                        d.StackCount * difficulty.StackOffset);
                    stackedEnd = d.SliderHitBurstEnd - stackOffset;
                }
                objects.Add(new ObjectRef
                {
                    Position = d.Position,
                    EndPosition = stackedEnd,
                    StartTime = d.StartTime,
                    EndTime = isSlider && d.SliderComputed ? d.SliderVirtualEndTime : d.StartTime,
                    NewCombo = d.NewCombo,
                    IsSpinner = (d.Type & HitObjectType.Spinner) != 0
                });
            }
            objects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            for (int i = 0; i < objects.Count; i++)
            {
                ObjectRef start = objects[i];
                ObjectRef? end = i + 1 < objects.Count ? objects[i + 1] : (ObjectRef?)null;
                LifetimeEntry entry = new LifetimeEntry(start, end);
                entry.RefreshLifetime();
                if (entry.LifetimeEnd > entry.LifetimeStart)
                    entries.Add(entry);
            }
            entries.Sort((a, b) => a.LifetimeStart.CompareTo(b.LifetimeStart));
        }

        /// <summary>
        /// lazer PooledDrawableWithLifetimeContainer.CheckChildrenLife.
        /// Past/FutureLifetimeExtension은 FollowPointRenderer에서 0 (OsuPlayfield가 설정 안 함).
        /// </summary>
        public void Update(int timeMs)
        {
            if (entries.Count == 0)
                return;

            if (timeMs < lastTime)
                nextIndex = 0;
            lastTime = timeMs;

            while (nextIndex < entries.Count && entries[nextIndex].LifetimeStart <= timeMs)
            {
                LifetimeEntry entry = entries[nextIndex++];
                if (timeMs <= entry.LifetimeEnd)
                    Activate(entry);
            }

            for (int i = alive.Count - 1; i >= 0; i--)
            {
                LifetimeEntry entry = alive[i];
                if (timeMs > entry.LifetimeEnd || timeMs < entry.LifetimeStart)
                    Deactivate(entry);
            }
        }

        /// <summary>
        /// 재시도/맵 언로드 — 살아 있는 연결을 풀로 되돌린다.
        /// </summary>
        public void FreeAll()
        {
            for (int i = alive.Count - 1; i >= 0; i--)
                Deactivate(alive[i]);
            alive.Clear();
            nextIndex = 0;
            lastTime = int.MinValue;
        }

        void Activate(LifetimeEntry entry)
        {
            if (entry.Alive || !entry.End.HasValue)
                return;

            ObjectRef start = entry.Start;
            ObjectRef end = entry.End.Value;

            // lazer FollowPointLifetimeEntry.refreshLifetimes: NewCombo/Spinner면 점을 안 만든다.
            if (end.NewCombo || start.IsSpinner || end.IsSpinner)
                return;

            Vector2 pos1 = start.EndPosition;
            Vector2 pos2 = end.Position;
            Vector2 distanceVector = pos2 - pos1;
            int distance = (int)distanceVector.Length;
            int duration = end.StartTime - start.EndTime;
            float angle = (float)Math.Atan2(pos2.Y - pos1.Y, pos2.X - pos1.X);

            entry.Dots.Clear();
            int lastExpire = entry.LifetimeStart;

            // lazer FollowPointConnection.scheduleRefresh 루프
            for (int d = (int)(Spacing * 1.5); d < distance - Spacing; d += Spacing)
            {
                float fraction = (float)d / distance;
                Vector2 posStart = pos1 + (fraction - 0.1f) * distanceVector;
                Vector2 pos = pos1 + fraction * distanceVector;

                int fadeOut;
                int fadeIn;
                GetFadeTimes(start.EndTime, duration, fraction, out fadeIn, out fadeOut);

                pAnimation fp = RentPoint();
                fp.Position = pos;
                fp.BasePosition = pos;
                fp.Rotation = angle;
                fp.Alpha = 0f;
                fp.Scale = 1f;
                fp.Transformations.Clear();

                fp.Transformations.Add(new Transformation(
                    TransformationType.Fade, 0f, 1f, fadeIn, fadeIn + FadeInDuration, EasingTypes.None));

                // stable HitObjectManager: Scale+Movement는 default 스킨에서만.
                // lazer는 항상 넣지만 오버레이는 stable 위에 그린다.
                if (SkinManager.IsDefault)
                {
                    fp.Transformations.Add(new Transformation(
                        TransformationType.Scale, 1.5f, 1f, fadeIn, fadeIn + FadeInDuration, EasingTypes.Out));
                    fp.Transformations.Add(new Transformation(
                        TransformationType.Movement, posStart, pos, fadeIn, fadeIn + FadeInDuration, EasingTypes.Out));
                }

                fp.Transformations.Add(new Transformation(
                    TransformationType.Fade, 1f, 0f, fadeOut, fadeOut + FadeInDuration, EasingTypes.None));

                fp.ResetAnimation();
                fp.ComputeTimeRange();
                spriteManager.Add(fp);
                entry.Dots.Add(fp);
                lastExpire = fadeOut + FadeInDuration;
            }

            if (entry.Dots.Count == 0)
                return;

            // lazer: entry.LifetimeEnd = 마지막 점 Expire 시각
            entry.LifetimeEnd = lastExpire;
            entry.Alive = true;
            alive.Add(entry);
        }

        void Deactivate(LifetimeEntry entry)
        {
            if (!entry.Alive)
                return;
            for (int i = 0; i < entry.Dots.Count; i++)
                Recycle(entry.Dots[i]);
            entry.Dots.Clear();
            entry.Alive = false;
            alive.Remove(entry);
        }

        pAnimation RentPoint()
        {
            if (pointPool.Count > 0)
                return pointPool.Pop();

            pAnimation dot = new pAnimation(textures, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                Vector2.Zero, SpriteManager.FollowPointDepth, false, Color.White);
            dot.SetFramerateFromSkin();
            return dot;
        }

        void Recycle(pAnimation dot)
        {
            if (dot.PendingRemove || spriteManager.Contains(dot))
                spriteManager.Remove(dot);
            dot.Transformations.Clear();
            dot.Alpha = 0f;
            dot.Scale = 1f;
            dot.Rotation = 0f;
            dot.TagNumeric = 0;
            dot.TimeRangeCached = false;
            dot.ResetAnimation();
            if (pointPool.Count < PointPoolMax)
                pointPool.Push(dot);
        }

        void WarmPool()
        {
            while (pointPool.Count < PointPoolInitial)
            {
                pAnimation dot = new pAnimation(textures, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                    Vector2.Zero, SpriteManager.FollowPointDepth, false, Color.White);
                dot.SetFramerateFromSkin();
                pointPool.Push(dot);
            }
        }

        /// <summary>
        /// lazer FollowPointConnection.GetFadeTimes. preempt는 stable/오버레이와 같이 800ms 고정
        /// (lazer는 AR에 비례해 줄이지만 이 오버레이는 stable 타이밍을 쓴다).
        /// </summary>
        public static void GetFadeTimes(int startEndTime, int duration, float fraction, out int fadeInTime, out int fadeOutTime)
        {
            fadeOutTime = startEndTime + (int)(fraction * duration);
            fadeInTime = fadeOutTime - Preempt;
        }

        struct ObjectRef
        {
            public Vector2 Position;
            public Vector2 EndPosition;
            public int StartTime;
            public int EndTime;
            public bool NewCombo;
            public bool IsSpinner;
        }

        /// <summary>
        /// lazer FollowPointLifetimeEntry. Start는 이 오브젝트, End는 시간상 다음 오브젝트.
        /// </summary>
        sealed class LifetimeEntry
        {
            public readonly ObjectRef Start;
            public readonly ObjectRef? End;
            public int LifetimeStart;
            public int LifetimeEnd;
            public bool Alive;
            public readonly List<pAnimation> Dots = new List<pAnimation>();

            public LifetimeEntry(ObjectRef start, ObjectRef? end)
            {
                Start = start;
                End = end;
                LifetimeStart = start.StartTime;
                LifetimeEnd = start.StartTime;
            }

            public void RefreshLifetime()
            {
                // lazer refreshLifetimes
                if (End == null || End.Value.NewCombo || Start.IsSpinner || End.Value.IsSpinner)
                {
                    LifetimeEnd = LifetimeStart;
                    return;
                }

                ObjectRef end = End.Value;
                Vector2 distanceVector = end.Position - Start.EndPosition;
                float dist = distanceVector.Length;
                int distance = (int)dist;
                bool anyDot = false;
                int lastD = (int)(Spacing * 1.5);
                for (int d = lastD; d < distance - Spacing; d += Spacing)
                {
                    anyDot = true;
                    lastD = d;
                }
                if (!anyDot)
                {
                    LifetimeEnd = LifetimeStart;
                    return;
                }

                int duration = end.StartTime - Start.EndTime;
                float firstFraction = (int)(Spacing * 1.5) / dist;
                int fadeIn;
                int fadeOut;
                GetFadeTimes(Start.EndTime, duration, firstFraction, out fadeIn, out fadeOut);
                LifetimeStart = fadeIn;
                GetFadeTimes(Start.EndTime, duration, lastD / dist, out _, out fadeOut);
                LifetimeEnd = fadeOut + FadeInDuration;
            }
        }
    }
}
