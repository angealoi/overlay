using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Graphics.OpenGl;
using OsuEnlightenOverlay.Graphics.Primitives;
using OsuEnlightenOverlay.Graphics.Renderers;
using OsuEnlightenOverlay.Rendering;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Skinning;

namespace OsuEnlightenOverlay.Gameplay.HitObjects
{
    /// <summary>
    /// Slider 렌더링 — ref/osu-stable SliderOsu.cs 포팅.
    /// enlighten 핵심: 항상 nomod 타이밍 사용 (hidden 타이밍 무시).
    /// 추측 없이 소스코드 그대로 포팅.
    /// </summary>
    internal class SliderOsu
    {
        HitObjectData data;
        public HitObjectData Data { get { return data; } }
        public int VirtualEndTime { get { return virtualEndTime; } }
        DifficultyValues difficulty;
        BeatmapData beatmap;

        // 커브 데이터
        List<Line> curvePath;
        List<double> cumulativeLengths;
        double curveLength;

        // 스프라이트들
        HitCircleSliderStart startCircle;
        List<HitCircleSliderEnd> endCircles = new List<HitCircleSliderEnd>();
        pAnimation sliderBall;
        pAnimation sliderFollower;
        pSprite sliderBallSpec;
        pSprite sliderBallNd;
        List<pSprite> sliderScorePoints = new List<pSprite>();

        // tracking 상태 — 메모리에서 읽은 IsTracking
        byte currentTracking = 0;
        byte prevTracking = 0;
        int trackingChangeTime = 0;   // tracking 상태가 변경된 시간

        // 시작원 Arm 상태
        bool startCircleArmed = false;
        public bool StartCircleArmed { get { return startCircleArmed; } }
        public bool IsSpriteAdded; // 시간 윈도우 기반 스프라이트 추가 추적

        // 타이밍
        int virtualEndTime;
        double velocity;

        // 콤보 색상 인덱스 — 슬라이더 바디 색상
        int comboColourIndex;
        Color comboColour;

        // FBO 캐싱 — snaking progress가 변경되었을 때만 재생성
        float cachedProgress = -1;
        pSprite cachedBodySprite;
        RenderTarget2D cachedFbo;

        public Vector2 EndPosition { get; private set; }

        /// <summary>
        /// HitBurst 표시 위치 — osu-stable HitObjectManager.Hit()에서 h.EndPosition 사용.
        /// repeat 슬라이더의 마지막 segment가 홀수(reverse)면 곡선 시작점, 짝수면 곡선 끝점.
        /// EndPosition(슬라이더 끝 원/repeat arrow용)과 분리.
        /// </summary>
        public Vector2 HitBurstEndPosition { get; private set; }

        public int ComboNumber { get; set; }

        /// <summary>
        /// 콤보 번호 위치 계산용 스케일 비율 설정 — startCircle에 전달.
        /// HitObjectManagerOsu.LoadBeatmap에서 호출.
        /// </summary>
        public void SetScaleRatios(float gsr, float gfr)
        {
            if (startCircle != null)
                startCircle.SetScaleRatios(gsr, gfr);
        }

        /// <summary>
        /// 콤보 번호 설정 — SetScaleRatios 후 호출해야 스프라이트가 올바른 간격으로 생성됨.
        /// </summary>
        public void SetComboNumber(int comboNumber)
        {
            ComboNumber = comboNumber;
            if (startCircle != null)
                startCircle.ComboNumber = comboNumber;
        }

        /// <summary>
        /// HD mod에서 시작원 approach circle 가시성 업데이트.
        /// </summary>
        public void UpdateStartCircleApproachVisibility(SpriteManager sm)
        {
            if (startCircle != null)
                startCircle.UpdateApproachCircleVisibility(sm);
        }

        public SliderOsu(HitObjectData data, DifficultyValues difficulty, BeatmapData beatmap, TextureManager texManager, Color comboColour, int comboNumber, int comboColourIndex, bool isFirstObject)
        {
            this.data = data;
            this.difficulty = difficulty;
            this.beatmap = beatmap;
            this.ComboNumber = comboNumber;
            this.comboColour = comboColour;
            this.comboColourIndex = comboColourIndex;

            int startTime = data.StartTime;
            int p = difficulty.PreEmpt;

            // 커브 계산 — osu! stable SliderOsu 생성자 로직 정확히 포팅
            List<Vector2> controlPoints = new List<Vector2>();
            if (data.CurvePoints != null && data.CurvePoints.Count > 0)
            {
                controlPoints.AddRange(data.CurvePoints);
                // 첫 점이 Position과 다르면 Position을 앞에 삽입
                if (controlPoints[0] != data.Position)
                    controlPoints.Insert(0, data.Position);
            }
            else
            {
                controlPoints.Add(data.Position);
            }

            curvePath = SliderCurve.CalculateCurve(controlPoints, data.CurveType, data.Length);
            cumulativeLengths = SliderCurve.CalculateCumulativeLengths(curvePath);

            // 커브 길이 계산
            curveLength = 0;
            foreach (Line l in curvePath)
                curveLength += l.Rho;

            // 끝 위치 — 곡선 끝점 (repeat arrow / slider end circle용).
            // HitBurst 위치는 별도 계산 (HitBurstEndPosition).
            if (curvePath.Count > 0)
                EndPosition = curvePath[curvePath.Count - 1].p2;
            else
                EndPosition = data.Position;

            // HitBurst 위치 — osu-stable HitObjectManager.Hit()에서 h.EndPosition 사용.
            // osu-stable SliderOsu.cs:957: EndPosition = p2 (마지막 segment의 p2)
            // 홀수 segment: reverse=true → p2 = l.p1 (곡선 시작점)
            // 짝수 segment: reverse=false → p2 = l.p2 (곡선 끝점)
            if (curvePath.Count > 0)
            {
                int segCount = Math.Max(1, data.RepeatCount);
                bool lastReverse = ((segCount - 1) % 2) == 1;
                if (lastReverse)
                    HitBurstEndPosition = curvePath[0].p1;  // 곡선 시작점
                else
                    HitBurstEndPosition = curvePath[curvePath.Count - 1].p2;  // 곡선 끝점
            }
            else
                HitBurstEndPosition = data.Position;

            // VirtualEndTime — osu! stable 공식
            // SpatialLength * BeatLengthAt(StartTime) * SegmentCount * 0.01 / DifficultySliderMultiplier + StartTime
            // osu! stable: SpatialLength = sliderLength = .osu 파일의 length 값
            double beatLength = BeatmapParser.BeatLengthAt(beatmap, startTime);
            int segmentCount = Math.Max(1, data.RepeatCount);
            virtualEndTime = (int)Math.Floor(data.Length * beatLength * segmentCount * 0.01 / beatmap.SliderMultiplier + startTime);

            // Velocity — osu! stable SliderVelocityAt
            // SliderVelocityAt = SliderScoringPointDistance * SliderTickRate * (1000 / BeatLength)
            // SliderScoringPointDistance = (100 * SliderMultiplier) / SliderTickRate
            // → Velocity = 100 * SliderMultiplier * 1000 / BeatLength = 100000 * SliderMultiplier / BeatLength
            velocity = (100 * beatmap.SliderMultiplier * 1000) / beatLength;

            // 기본 combo 색상 — SkinManager에서 조회 (이미 comboColour로 전달받음)
            // comboColour는 생성자 파라미터로 받음

            // 텍스처 로드
            pTexture texHitCircle = texManager.Load("hitcircle");
            pTexture texSliderStart = texManager.Load("sliderstartcircle");
            pTexture texSliderEnd = texManager.Load("sliderendcircle");
            pTexture texReverseArrow = texManager.Load("reversearrow");

            // 슬라이더 볼 — osu! stable: LoadAll("sliderb", SkinSource.All, false)
            // dashSeparator=false → sliderb0, sliderb1, ...
            pTexture[] sliderBallTextures = texManager.LoadAll("sliderb", SkinSource.All, false);
            bool usingDefault = sliderBallTextures.Length > 0 && sliderBallTextures[0].Source == SkinSource.Osu;

            // 슬라이더 팔로워 — osu! stable: LoadAll("sliderfollowcircle")
            pTexture[] sliderFollowerTextures = texManager.LoadAll("sliderfollowcircle");

            // ── 시작 원 (HitCircleSliderStart) ──
            // sliderstartcircle 텍스처 사용 (fallback: hitcircle)
            startCircle = new HitCircleSliderStart(data, difficulty, texManager, comboColour, isFirstObject);
            // ComboNumber는 SetScaleRatios 후 LoadBeatmap에서 SetComboNumber로 설정

            // ── 슬라이더 바디 ──
            // nomod: Fade In 0→1 (StartTime-PreEmpt → StartTime-PreEmpt+FadeIn)
            // nomod: Fade Out 1→0 (EndTime → EndTime+FadeOut)
            // 커브를 따라 작은 원들을 그려서 바디 표현 (간단 구현)
            // TODO: Phase 6 완성 시 MmSliderRenderer로 교체

            // ── 끝 원 + 리버스 화살표 + 슬라이더 틱 ──
            // osu! stable UpdateCalculations 포팅
            double currentTime = startTime;
            bool firstRun = true;

            // 슬라이더 틱 거리 — osu! stable: SliderScoringPointDistance
            // SliderScoringPointDistance = (100 * SliderMultiplier) / SliderTickRate
            // BeatmapVersion >= 8: tickDistance / BpmMultiplierAt(StartTime)
            double sliderScoringPointDistance = (100 * beatmap.SliderMultiplier) / beatmap.SliderTickRate;
            double tickDistance = sliderScoringPointDistance / BeatmapParser.BpmMultiplierAt(beatmap, startTime);
            if (tickDistance > data.Length) tickDistance = data.Length;
            double scoringDistance = 0;
            double scoringLengthTotal = 0;
            double minTickDistanceFromEnd = 0.01 * velocity;

            for (int i = 0; i < segmentCount; i++)
            {
                bool reverse = (i % 2) == 1;
                Vector2 circlePos = reverse ? data.Position : EndPosition;

                // segment 시작 시간
                int reverseStartTime = (int)currentTime;

                // 각 선분마다 볼/팔로워 Movement Transformation 생성 — osu! stable과 동일
                int pathCount = curvePath.Count;
                int startIdx = reverse ? pathCount - 1 : 0;
                int endIdx = reverse ? -1 : pathCount;
                int direction = reverse ? -1 : 1;

                for (int j = startIdx; j != endIdx; j += direction)
                {
                    Line l = curvePath[j];
                    float distance = l.Rho;

                    Vector2 p1, p2;
                    if (reverse)
                    {
                        p1 = l.p2;
                        p2 = l.p1;
                    }
                    else
                    {
                        p1 = l.p1;
                        p2 = l.p2;
                    }

                    double duration = 1000.0 * distance / velocity;

                    currentTime += duration;
                    scoringDistance += distance;

                    // 슬라이더 틱 (scoring points) — osu! stable UpdateCalculations 포팅
                    double distanceToEnd = data.Length * (segmentCount - i) - scoringLengthTotal - scoringDistance;
                    bool skipTick = false;

                    while (scoringDistance >= tickDistance && !skipTick)
                    {
                        scoringLengthTotal += tickDistance;
                        scoringDistance -= tickDistance;
                        distanceToEnd -= tickDistance;

                        skipTick = distanceToEnd <= minTickDistanceFromEnd;
                        if (skipTick)
                            break;

                        int scoreTime = TimeAtLength((float)scoringLengthTotal);

                        float thisPointRatio = 1 - (float)(scoringDistance / Vector2.Distance(p1, p2));
                        Vector2 adjustedPos = p1 + (p2 - p1) * thisPointRatio;

                        pTexture texScorePoint = texManager.Load("sliderscorepoint");
                        if (texScorePoint != null)
                        {
                            pSprite scoringDot = new pSprite(texScorePoint, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                                adjustedPos, SpriteManager.DrawOrderBwd(startTime - 5), false, Color.White);
                            if (texScorePoint.Source == SkinSource.Osu)
                                scoringDot.Additive = true;

                            if (firstRun)
                            {
                                int dotStartTime = (scoreTime - startTime) / 2 + startTime - difficulty.PreEmptSliderComplete;
                                int dotEndTime = dotStartTime + 150;
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Fade, 0f, 1f, dotStartTime, dotEndTime, EasingTypes.None));
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Scale, 0.5f, 1.2f, dotStartTime, dotEndTime, EasingTypes.None));
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Scale, 1.2f, 1f, dotEndTime, dotEndTime + 150, EasingTypes.Out));
                            }
                            else
                            {
                                int displayStartTime = reverseStartTime + (scoreTime - reverseStartTime) / 2;
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Fade, 0f, 1f, displayStartTime - 200, displayStartTime, EasingTypes.None));
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Scale, 0.5f, 1.2f, displayStartTime - 200, displayStartTime - 50, EasingTypes.None));
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Scale, 1.2f, 1f, displayStartTime - 50, displayStartTime + 150, EasingTypes.Out));
                            }

                            // 볼 도착 시 사라짐
                            scoringDot.Transformations.Add(new Transformation(
                                TransformationType.Fade, 0f, 0f, scoreTime, scoreTime, EasingTypes.None));

                            // osu! stable: 시작원/끝원 HitObjectRadius 반경 내 틱은 제외
                            float radiusSquared = difficulty.HitObjectRadius * difficulty.HitObjectRadius;
                            float distToStart = (adjustedPos - data.Position).LengthSquared;
                            float distToEnd = (adjustedPos - EndPosition).LengthSquared;
                            if (distToStart >= radiusSquared && distToEnd >= radiusSquared)
                                sliderScorePoints.Add(scoringDot);
                        }
                    }
                }

                // segment duration (전체) — end circle 타이밍용
                // currentTime이 이미 각 선분마다 누적됨

                // 끝 원 startTime = currentTime (세그먼트 끝)
                int circleStartTime = (int)currentTime;

                // appearTime — osu! stable 공식
                int appearTime;
                if (firstRun)
                {
                    appearTime = startTime - difficulty.PreEmptSliderComplete;
                }
                else
                {
                    appearTime = reverseStartTime - (circleStartTime - reverseStartTime);
                }

                // 리버스 방향 각도 계산 — osu! stable 정확 포팅
                // osu! stable: angle = Atan2(p1.Y - p2.Y, p1.X - p2.X)
                // 정방향 (reverse=false): p1=마지막선분.p1, p2=마지막선분.p2 → 끝점에서 시작점 방향
                // 역방향 (reverse=true):  p1=첫선분.p2, p2=첫선분.p1 → 시작점에서 끝점 방향
                float angle = 0;
                if (curvePath.Count > 0)
                {
                    if (reverse)
                    {
                        // 역방향: 슬라이더 시작점에서 끝점 방향 (p1=첫선분.p2, p2=첫선분.p1)
                        Line firstLine = curvePath[0];
                        angle = (float)Math.Atan2(firstLine.p2.Y - firstLine.p1.Y, firstLine.p2.X - firstLine.p1.X);
                    }
                    else
                    {
                        // 정방향: 슬라이더 끝점에서 시작점 방향 (p1=마지막선분.p1, p2=마지막선분.p2)
                        Line lastLine = curvePath[curvePath.Count - 1];
                        angle = (float)Math.Atan2(lastLine.p1.Y - lastLine.p2.Y, lastLine.p1.X - lastLine.p2.X);
                    }
                }

                // HitCircleSliderEnd 생성
                HitObjectData endData = new HitObjectData();
                endData.Position = circlePos;
                endData.BasePosition = circlePos;
                endData.StartTime = circleStartTime;
                endData.EndTime = circleStartTime;
                endData.Type = HitObjectType.Normal;
                endData.Colour = comboColour;

                bool isReverse = (i < segmentCount - 1); // 마지막 세그먼트가 아니면 리버스
                double segmentDuration = 1000.0 * data.Length / velocity;
                HitCircleSliderEnd endCircle = new HitCircleSliderEnd(endData, difficulty, texManager,
                    appearTime, isReverse, angle, circleStartTime, comboColour,
                    firstRun, startTime, segmentDuration);

                endCircles.Add(endCircle);

                firstRun = false;
            }

            // virtualEndTime = currentTime — osu! stable: EndTime = (int)currentTime
            // 이렇게 하면 virtualEndTime과 segmentDuration이 정확히 일치함
            virtualEndTime = (int)currentTime;

            // 슬라이더 볼 — osu! stable: pAnimation, LoadAll("sliderb")
            // usingDefault이면 SliderBall 색상 적용, 아니면 White
            if (sliderBallTextures.Length > 0)
            {
                sliderBall = new pAnimation(sliderBallTextures, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                    data.Position, 0.99f, false, usingDefault ? SkinManager.LoadColour("SliderBall") : Color.White);
                sliderBall.SetFramerateFromSkin();
                sliderBall.TrackRotation = true;
                // osu! stable: FrameDelay = Math.Max((150 / Velocity) * SIXTY_FRAME_TIME, SIXTY_FRAME_TIME)
                // SIXTY_FRAME_TIME = 1000/60 ≈ 16.67
                sliderBall.FrameDelay = Math.Max((150.0 / velocity) * (1000.0 / 60.0), 1000.0 / 60.0);
                sliderBall.Alpha = 0f;
                // osu! stable: sliderBall은 alwaysDraw=false, Fade 변환 없음.
                // StartTime에 즉시 나타나고 EndTime에 즉시 사라짐.
                sliderBall.Transformations.Add(new Transformation(
                    TransformationType.Fade, 0f, 1f, startTime, startTime + 1, EasingTypes.None));
                sliderBall.Transformations.Add(new Transformation(
                    TransformationType.Fade, 1f, 0f, virtualEndTime, virtualEndTime + 1, EasingTypes.None));

                // sliderb-spec / sliderb-nd — osu! stable: usingDefault일 때만 로드
                if (usingDefault)
                {
                    pTexture texSpec = texManager.Load("sliderb-spec", SkinSource.All);
                    pTexture texNd = texManager.Load("sliderb-nd", SkinSource.All);
                    if (texSpec != null)
                    {
                        sliderBallSpec = new pSprite(texSpec, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                            data.Position, 1.0f, false, Color.White);
                        sliderBallSpec.Additive = true;
                        sliderBallSpec.Alpha = 0f;
                        sliderBallSpec.Transformations.Add(new Transformation(
                            TransformationType.Fade, 0f, 1f, startTime, startTime + 1, EasingTypes.None));
                        sliderBallSpec.Transformations.Add(new Transformation(
                            TransformationType.Fade, 1f, 0f, virtualEndTime, virtualEndTime + 1, EasingTypes.None));
                    }
                    if (texNd != null)
                    {
                        sliderBallNd = new pSprite(texNd, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                            data.Position, 0.98f, false, Color.FromArgb(5, 5, 5));
                        sliderBallNd.Alpha = 0f;
                        sliderBallNd.Transformations.Add(new Transformation(
                            TransformationType.Fade, 0f, 1f, startTime, startTime + 1, EasingTypes.None));
                        sliderBallNd.Transformations.Add(new Transformation(
                            TransformationType.Fade, 1f, 0f, virtualEndTime, virtualEndTime + 1, EasingTypes.None));
                    }
                }
            }

            // ── 슬라이더 팔로워 — osu! stable: pAnimation, LoadAll("sliderfollowcircle") ──
            // tracking 기반 동적 제어: InitSlide/KillSlide 대신 메모리 IsTracking으로 follow circle 표시.
            if (sliderFollowerTextures.Length > 0)
            {
                sliderFollower = new pAnimation(sliderFollowerTextures, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                    data.Position, 0.99f, true, Color.White);
                sliderFollower.SetFramerateFromSkin();
                sliderFollower.Alpha = 0f;
                // transformation 없음 — AddToSpriteManager에서 tracking 상태에 따라 동적 제어
            }
        }

        /// <summary>
        /// 커브 위 특정 거리의 시간 — osu! stable timeAtLength.
        /// timeAtLength(length) = StartTime + (length / Velocity) * 1000
        /// </summary>
        int TimeAtLength(float length)
        {
            return (int)(data.StartTime + (length / velocity) * 1000);
        }

        /// <summary>
        /// 현재 시간에서 슬라이더 볼 위치 계산.
        /// osu! stable PositionAtTime 정확 포팅.
        /// </summary>
        Vector2 GetBallPosition(int time)
        {
            int segmentCount = Math.Max(1, data.RepeatCount);

            if (time <= data.StartTime) return data.Position;
            if (time >= virtualEndTime)
            {
                // 마지막 세그먼트 방향에 따라 끝 위치 결정
                // 짝수 세그먼트(0,2,4...)는 정방향 → EndPosition
                // 홀수 세그먼트(1,3,5...)는 역방향 → data.Position (시작점)
                int lastSegment = segmentCount - 1;
                return (lastSegment % 2 == 1) ? data.Position : EndPosition;
            }

            // osu! stable PositionAtTime:
            // Length = EndTime - StartTime (시간 길이, ms)
            // EndTime = VirtualEndTime (UpdateCalculations에서 currentTime = EndTime)
            // SpatialLength = 커브 공간 길이 (픽셀)
            // pos = (time - StartTime) / ((float)Length / SegmentCount)
            // lengthRequired = SpatialLength * pos
            float length = (float)(virtualEndTime - data.StartTime); // Length = EndTime - StartTime
            float pos = (time - data.StartTime) / (length / segmentCount);

            if (pos % 2 > 1)
                pos = 1 - (pos % 1);
            else
                pos = (pos % 1);

            // SpatialLength = data.Length (.osu 파일의 length) — osu! stable과 동일
            float lengthRequired = (float)(data.Length * pos);
            return PositionAtLength(lengthRequired);
        }

        /// <summary>
        /// 커브 위 특정 거리의 위치 — osu! stable positionAtLength 정확 포팅.
        /// </summary>
        Vector2 PositionAtLength(float length)
        {
            if (curvePath.Count == 0 || cumulativeLengths.Count == 0)
                return data.Position;

            if (length == 0)
                return curvePath[0].p1;

            double end = cumulativeLengths[cumulativeLengths.Count - 1];
            if (length >= end)
                return curvePath[curvePath.Count - 1].p2;

            int i = cumulativeLengths.BinarySearch(length);
            if (i < 0)
                i = Math.Min(~i, cumulativeLengths.Count - 1);

            double lengthNext = cumulativeLengths[i];
            double lengthPrevious = i == 0 ? 0 : cumulativeLengths[i - 1];

            Vector2 res = curvePath[i].p1;

            if (lengthNext != lengthPrevious)
                res += (curvePath[i].p2 - curvePath[i].p1) * (float)((length - lengthPrevious) / (lengthNext - lengthPrevious));

            return res;
        }

        /// <summary>
        /// 현재 시간에서 보이는지.
        /// IsVisible: StartTime-PreEmpt ≤ Time ≤ EndTime+FadeOut
        /// </summary>
        public bool IsVisibleAt(int time)
        {
            return time >= data.StartTime - difficulty.PreEmpt &&
                   time <= virtualEndTime + DifficultyCalculator.FadeOut;
        }

        /// <summary>
        /// 스택 적용 후 위치 업데이트 — UpdateStacking 호출 후.
        /// 슬라이더 시작원 위치만 업데이트.
        /// </summary>
        public void UpdateStackedPosition()
        {
            if (startCircle != null)
                startCircle.UpdateStackedPosition();
        }

        /// <summary>
        /// HitBurstEndPosition에 스택 오프셋 적용.
        /// </summary>
        public void ApplyStackOffsetToHitBurstEndPosition(Vector2 offset)
        {
            HitBurstEndPosition += offset;
        }

        /// <summary>
        /// 메모리에서 읽은 tracking 상태 설정.
        /// AddToSpriteManager 호출 전에 HitObjectManagerOsu.Update에서 호출됨.
        /// </summary>
        public void SetTracking(byte isTracking)
        {
            currentTracking = isTracking;
        }

        /// <summary>
        /// 슬라이더 시작원 Arm — osu-stable Hit(slider.sliderStartCircle).
        /// </summary>
        public void ArmStartCircle(bool isHit, int armTime)
        {
            if (startCircleArmed) return;
            startCircleArmed = true;
            if (startCircle != null)
                startCircle.Arm(isHit, armTime);
        }

        /// <summary>
        /// difficulty 변경 시 Transformation 재구성 — 객체 재생성 없이 업데이트.
        /// AR/CS/FadeIn/HitObjectRadius 변경 시 호출.
        /// </summary>
        public void UpdateDifficulty(DifficultyValues newDifficulty)
        {
            this.difficulty = newDifficulty;

            // 시작원 업데이트
            if (startCircle != null)
                startCircle.UpdateDifficulty(newDifficulty);

            // 끝원들 업데이트
            foreach (HitCircleSliderEnd endCircle in endCircles)
                endCircle.UpdateDifficulty(newDifficulty);

            // 슬라이더 바디 FBO 캐시 무효화 — HitObjectRadius 변경 시 재생성 필요
            cachedProgress = -1;
        }

        /// <summary>
        /// SpriteManager에 스프라이트 한 번 추가 — LoadBeatmap 시 호출.
        /// </summary>
        public void AddToSpriteManager(SpriteManager sm, int timeMs)
        {
            // 시작 원
            if (startCircle != null)
                startCircle.AddToSpriteManager(sm);
            // 끝 원들
            foreach (HitCircleSliderEnd endCircle in endCircles)
                endCircle.AddToSpriteManager(sm);
            // 슬라이더 틱
            foreach (pSprite scorePoint in sliderScorePoints)
                if (!sm.Contains(scorePoint)) sm.Add(scorePoint);
            // 슬라이더 볼
            if (sliderBall != null && !sm.Contains(sliderBall)) sm.Add(sliderBall);
            if (sliderBallNd != null && !sm.Contains(sliderBallNd)) sm.Add(sliderBallNd);
            if (sliderBallSpec != null && !sm.Contains(sliderBallSpec)) sm.Add(sliderBallSpec);
            // 슬라이더 팔로워
            if (sliderFollower != null && !sm.Contains(sliderFollower)) sm.Add(sliderFollower);
        }

        /// <summary>
        /// SpriteManager에서 스프라이트 제거.
        /// </summary>
        public void RemoveFromSpriteManager(SpriteManager sm)
        {
            if (startCircle != null)
                startCircle.RemoveFromSpriteManager(sm);
            foreach (HitCircleSliderEnd endCircle in endCircles)
                endCircle.RemoveFromSpriteManager(sm);
            foreach (pSprite scorePoint in sliderScorePoints)
                if (sm.Contains(scorePoint)) sm.Remove(scorePoint);
            if (sliderBall != null && sm.Contains(sliderBall)) sm.Remove(sliderBall);
            if (sliderBallNd != null && sm.Contains(sliderBallNd)) sm.Remove(sliderBallNd);
            if (sliderBallSpec != null && sm.Contains(sliderBallSpec)) sm.Remove(sliderBallSpec);
            if (sliderFollower != null && sm.Contains(sliderFollower)) sm.Remove(sliderFollower);
            // 캐시된 바디 스프라이트도 제거
            if (cachedBodySprite != null && sm.Contains(cachedBodySprite)) sm.Remove(cachedBodySprite);
        }

        /// <summary>
        /// 매 프레임 스프라이트 상태 업데이트 — HOM.Update에서 호출.
        /// 볼 위치, follow circle tracking 애니메이션 등.
        /// </summary>
        public void UpdateSprites(int timeMs)
        {
            // 슬라이더 볼 (StartTime ~ VirtualEndTime)
            if (sliderBall != null && timeMs >= data.StartTime && timeMs <= virtualEndTime)
            {
                Vector2 ballPos = GetBallPosition(timeMs);
                sliderBall.Position = ballPos;

                int segmentCount = Math.Max(1, data.RepeatCount);
                float length = (float)(virtualEndTime - data.StartTime);
                float pos = (timeMs - data.StartTime) / (length / segmentCount);
                int currentSegment = (int)pos;
                bool isReverseSegment = (currentSegment % 2) == 1;
                sliderBall.Reverse = isReverseSegment;
                if (SkinManager.Current != null)
                    sliderBall.FlipHorizontal = isReverseSegment && SkinManager.Current.SliderBallFlip;

                if (sliderBallNd != null) sliderBallNd.Position = ballPos;
                if (sliderBallSpec != null) sliderBallSpec.Position = ballPos;
            }

            // 슬라이더 팔로워 — tracking 기반 애니메이션 (osu-stable InitSlide/KillSlide)
            if (sliderFollower != null)
            {
                if (timeMs >= data.StartTime && timeMs <= virtualEndTime + 200)
                {
                    Vector2 ballPos = GetBallPosition(timeMs);
                    sliderFollower.Position = ballPos;

                    // tracking 상태 변화 감지
                    if (currentTracking != prevTracking)
                    {
                        trackingChangeTime = timeMs;
                        prevTracking = currentTracking;
                    }

                    float alpha, scale;
                    if (currentTracking == 1)
                    {
                        int elapsed = timeMs - trackingChangeTime;
                        float fadeT = Math.Min(1f, elapsed / 60f);
                        float scaleT = Math.Min(1f, elapsed / 180f);
                        float easedScaleT = 1f - (1f - scaleT) * (1f - scaleT);
                        alpha = fadeT;
                        scale = 0.5f + 0.5f * easedScaleT;

                        if (timeMs > virtualEndTime)
                        {
                            int endElapsed = timeMs - virtualEndTime;
                            float endT = Math.Min(1f, endElapsed / 200f);
                            float easedEndT = endT * endT;
                            alpha = 1f - easedEndT;
                            float outEndT = 1f - (1f - endT) * (1f - endT);
                            scale = 1f - 0.2f * outEndT;
                        }
                    }
                    else
                    {
                        int elapsed = timeMs - trackingChangeTime;
                        float fadeT = Math.Min(1f, elapsed / 100f);
                        alpha = 1f - fadeT;
                        scale = 1f + fadeT;
                    }

                    sliderFollower.Alpha = alpha;
                    sliderFollower.Scale = scale;
                }
                else
                {
                    // 시간 범위 밖 — follow circle 숨김
                    sliderFollower.Alpha = 0f;
                }
            }
        }

        /// <summary>
        /// 슬라이더 바디 렌더링 — osu! stable SliderOsu.Draw() 포팅.
        /// MmSliderRenderer 사용, snaking progress 적용.
        /// SpriteManager보다 먼저 호출되어야 함 (depth buffer).
        /// </summary>
        public void DrawBody(MmSliderRenderer renderer, GameField gameField, int timeMs, Matrix4 projectionMatrix, SpriteManager sm)
        {
            if (curvePath == null || curvePath.Count == 0) return;

            int startTime = data.StartTime;

            // 가시성 체크
            if (timeMs < startTime - difficulty.PreEmpt) return;
            if (timeMs > virtualEndTime + DifficultyCalculator.FadeOut) return;

            // Snaking progress — osu! stable:
            // progress = (AudioEngine.Time - (StartTime - PreEmpt)) / (PreEmpt / 3f)
            float progress;
            if (timeMs < startTime)
            {
                progress = (float)(timeMs - (startTime - difficulty.PreEmpt)) / (difficulty.PreEmpt / 3f);
                progress = Math.Min(1, Math.Max(0, progress));
            }
            else
            {
                progress = 1;
            }

            // lineList 구성 — snaking progress까지의 선분만
            // FBO 캐싱: progress가 변경되었을 때만 재생성
            // 스터터링 방지: progress를 1/30 단위로 양자화 — FBO 재생성 빈도 대폭 감소
            float quantizedProgress = (float)Math.Round(progress * 30) / 30f;
            bool needsRebuild = cachedProgress != quantizedProgress || cachedBodySprite == null;

            if (needsRebuild)
            {
                // 이전 스프라이트를 SpriteManager에서 제거 (cachedBodySprite가 null이 되기 전에)
                if (cachedBodySprite != null)
                    sm.Remove(cachedBodySprite);

                // 이전 FBO 삭제 — RenderTarget2D가 텍스처/FBO/렌더버퍼 모두 정리
                if (cachedFbo != null)
                {
                    cachedFbo.Dispose();
                    cachedFbo = null;
                }
                cachedBodySprite = null;

                cachedProgress = quantizedProgress;

                // osu! stable: 선분 병합 최적화 + cumulativeLengths 기반 분할
                List<Graphics.Primitives.Line> lineList = new List<Graphics.Primitives.Line>();
                double lengthToDraw = curveLength * progress;

                // cumulativeLengths에서 lengthToDraw에 해당하는 인덱스 찾기
                int count = curvePath.Count;
                if (progress < 1 && cumulativeLengths != null && cumulativeLengths.Count > 0)
                {
                    int idx = cumulativeLengths.FindIndex(l => l > lengthToDraw);
                    count = idx + 1;
                    if (count == 0) count = curvePath.Count;
                }

                // 마지막 선분의 남은 길이
                float countRemainder = 0;
                if (progress < 1 && count > 0)
                {
                    float prevCumul = count >= 2 ? (float)cumulativeLengths[count - 2] : 0;
                    countRemainder = (float)lengthToDraw - prevCumul;
                }

                // 선분 병합 (osu! stable: min_dist 기반)
                int storedStart = 0;
                bool waiting = false;

                for (int i = 0; i < count; i++)
                {
                    if (!waiting)
                        storedStart = i;

                    bool last = i == count - 1;
                    // 곡선 선분은 min_dist=6, 직선은 32
                    float minDist = 6; // 곡선 (straight 필드 없으므로 기본값)
                    float dist = Vector2.Distance(curvePath[storedStart].p1, curvePath[i].p2);

                    if (dist > minDist || last || (i == count - 2))
                    {
                        if (last && countRemainder > 0)
                        {
                            // 마지막 선분 — 남은 길이만큼 자르기
                            Graphics.Primitives.Line l = new Graphics.Primitives.Line(
                                curvePath[storedStart].p1, curvePath[i].p2);
                            if (l.p2 != l.p1)
                            {
                                Vector2 dir = l.p2 - l.p1;
                                dir.Normalize();
                                l.p2 = l.p1 + dir * countRemainder;
                            }
                            lineList.Add(l);
                        }
                        else if (storedStart == i)
                        {
                            lineList.Add(new Graphics.Primitives.Line(curvePath[i].p1, curvePath[i].p2));
                        }
                        else
                        {
                            lineList.Add(new Graphics.Primitives.Line(
                                curvePath[storedStart].p1, curvePath[i].p2));
                        }
                        waiting = false;
                    }
                    else
                        waiting = true;
                }

            if (lineList.Count == 0) return;

            // 게임필드 좌표 → 화면 좌표 변환
            List<Graphics.Primitives.Line> screenLines = new List<Graphics.Primitives.Line>();
            foreach (Graphics.Primitives.Line l in lineList)
            {
                Vector2 p1Screen = gameField.FieldToDisplay(l.p1);
                Vector2 p2Screen = gameField.FieldToDisplay(l.p2);
                screenLines.Add(new Graphics.Primitives.Line(p1Screen, p2Screen));
            }

            // 반경 — HitObjectRadius * GameField.Ratio (화면 좌표)
            float radius = difficulty.HitObjectRadius * gameField.Ratio;

            // 색상 인덱스 — SliderTrackOverride 확인
            int colourIndex;
            Color trackOverride = SkinManager.LoadColour("SliderTrackOverride");
            if (trackOverride.A > 0)
            {
                // 단일 색상 — 인덱스 0
                colourIndex = 0;
            }
            else
            {
                colourIndex = comboColourIndex;
            }

            // osu! stable: FBO에 렌더링 → pSprite 반환 → SpriteManager가 합성
                cachedBodySprite = renderer.Draw(screenLines, radius, colourIndex, projectionMatrix,
                    data.StartTime, virtualEndTime, difficulty.PreEmpt, difficulty.FadeIn, out cachedFbo);
            }

            // 캐시된 pSprite를 SpriteManager에 추가
            if (cachedBodySprite != null && !sm.Contains(cachedBodySprite))
                sm.Add(cachedBodySprite);
        }
    }
}