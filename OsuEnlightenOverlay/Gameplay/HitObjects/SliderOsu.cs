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
    internal class SliderOsu : IDisposable
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

        // 틱 소비 상태 — 볼이 tracking(follow circle 표시) 상태로 지나친 틱은 즉시 숨긴다.
        // stable은 슬라이더 판정(scoring)이 틱을 소비하지만 오버레이는 틱 단위 판정을
        // 메모리에서 못 읽어 IsTracking으로 근사한다(사용자 결정).
        List<int> tickScoreTimes = new List<int>(); // sliderScorePoints[i]와 병렬 — 볼 통과 시각(ms)
        int nextConsumableTick = 0;                 // 다음 소비 판정 대상 (시간순 전진 전용)
        const int TagTickConsumed = 1;              // 소비 숨김 트랜스폼 태그 — retry 리셋용

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

        // FBO 캐싱 — snaking 중 SDF만 이어 굽고, 완료 후에는 재사용
        float cachedProgress = -1;
        pSprite cachedBodySprite;
        RenderTarget2D cachedFbo;
        int lastBakedVertexCount;
        bool bodyBakeFrozen;

        readonly List<Vector2> bodyPathVerts = new List<Vector2>();
        readonly List<Vector2> bodyScreenFullVerts = new List<Vector2>();
        int cachedBallTime = int.MinValue;
        Vector2 cachedBallPos;
        bool snakingFrozen;

        bool bodyBoundsValid;
        float bodyDrawLeft, bodyDrawTop, bodyDrawWidth, bodyDrawHeight;
        float bodyBoundsRatio = -1, bodyBoundsRadius = -1;

        /// <summary>
        /// 전체 커브를 감싸는 화면 좌표 박스. 스네이킹과 무관하게 FBO 크기를 고정한다.
        /// </summary>
        void ComputeBodyBounds(GameField gameField, float radius)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            bodyScreenFullVerts.Clear();
            if (curvePath.Count > 0)
            {
                Vector2 first = gameField.FieldToDisplay(curvePath[0].p1);
                bodyScreenFullVerts.Add(first);
                minX = maxX = first.X;
                minY = maxY = first.Y;
            }

            foreach (Line l in curvePath)
            {
                Vector2 b = gameField.FieldToDisplay(l.p2);
                bodyScreenFullVerts.Add(b);
                minX = Math.Min(minX, b.X);
                minY = Math.Min(minY, b.Y);
                maxX = Math.Max(maxX, b.X);
                maxY = Math.Max(maxY, b.Y);
            }

            float excess = radius * 1.15f;
            bodyDrawLeft = minX - excess;
            bodyDrawTop = minY - excess;
            bodyDrawWidth = (maxX - minX) + radius * 2.3f;
            bodyDrawHeight = (maxY - minY) + radius * 2.3f;

            float clipR = gameField.windowWidth;
            float clipB = gameField.windowHeight;
            float right = Math.Min(bodyDrawLeft + bodyDrawWidth, clipR);
            float bottom = Math.Min(bodyDrawTop + bodyDrawHeight, clipB);
            bodyDrawLeft = Math.Max(bodyDrawLeft, 0);
            bodyDrawTop = Math.Max(bodyDrawTop, 0);
            bodyDrawWidth = Math.Max(1, right - bodyDrawLeft);
            bodyDrawHeight = Math.Max(1, bottom - bodyDrawTop);

            bodyBoundsRatio = gameField.Ratio;
            bodyBoundsRadius = radius;
            bodyBoundsValid = true;
        }

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
        /// 슬라이더의 커브/EndPosition/VirtualEndTime을 데이터 레벨에서 미리 계산.
        /// lazy 생성 시 LoadBeatmap이 객체를 만들지 않고 스택/정렬/FollowPoint에 필요한
        /// 값만 먼저 구한다. 실제 SliderOsu 생성은 윈도우 진입 시.
        /// </summary>
        public static void PrecomputeSliderData(HitObjectData data, DifficultyValues difficulty, BeatmapData beatmap)
        {
            if (data.SliderComputed) return;

            Vector2 headPos = data.BasePosition;
            List<Vector2> controlPoints = new List<Vector2>();
            if (data.CurvePoints != null && data.CurvePoints.Count > 0)
            {
                controlPoints.AddRange(data.CurvePoints);
                if (controlPoints[0] != headPos)
                    controlPoints.Insert(0, headPos);
            }
            else
            {
                controlPoints.Add(headPos);
            }

            List<Line> curvePath = SliderCurve.CalculateCurve(controlPoints, data.CurveType, data.Length);
            List<double> cum = SliderCurve.CalculateCumulativeLengths(curvePath);
            double pathLen = 0;
            foreach (Line l in curvePath)
                pathLen += l.Rho;
            data.CachedSliderPath = curvePath;
            data.CachedSliderCumLen = cum;
            data.CachedSliderLength = pathLen;

            // 끝 위치
            if (curvePath.Count > 0)
                data.SliderEndPosition = curvePath[curvePath.Count - 1].p2;
            else
                data.SliderEndPosition = headPos;

            // HitBurst 위치
            if (curvePath.Count > 0)
            {
                int segCount = Math.Max(1, data.RepeatCount);
                bool lastReverse = ((segCount - 1) % 2) == 1;
                data.SliderHitBurstEnd = lastReverse ? curvePath[0].p1 : curvePath[curvePath.Count - 1].p2;
            }
            else
                data.SliderHitBurstEnd = headPos;

            // VirtualEndTime — osu! stable SliderOsu.VirtualEndTime과 동일.
            // SpatialLength(.osu pixelLength) * BeatLengthAt(SV 포함) 공식.
            // 커브 실측 길이/lazer px/ms를 쓰면 게임 위 오버레이와 EndTime이 어긋난다.
            data.SliderVirtualEndTime = BeatmapParser.SliderVirtualEndTime(data, beatmap);
            data.EndTime = data.SliderVirtualEndTime;

            data.SliderComputed = true;
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

            // 커브 계산 — osu! stable SliderOsu 생성자 로직 정확히 포팅.
            // 머리 좌표는 data.Position이 아니라 BasePosition을 쓴다. UpdateStacking이
            // data.Position을 스택 위치로 변형하는데 data.CurvePoints는 원본 그대로라,
            // 같은 BeatmapData로 재로드되면 머리만 밀린 뒤틀린 커브가 나온다.
            // stable처럼 기하는 base 좌표로 만들고 UpdateStackedPosition에서 통째로 옮긴다.
            Vector2 headPos = data.BasePosition;
            List<Vector2> controlPoints = new List<Vector2>();
            if (data.CurvePoints != null && data.CurvePoints.Count > 0)
            {
                controlPoints.AddRange(data.CurvePoints);
                // 첫 점이 머리와 다르면 머리를 앞에 삽입
                if (controlPoints[0] != headPos)
                    controlPoints.Insert(0, headPos);
            }
            else
            {
                controlPoints.Add(headPos);
            }

            if (data.CachedSliderPath != null)
            {
                curvePath = SliderCurve.ClonePath(data.CachedSliderPath);
                cumulativeLengths = data.CachedSliderCumLen ?? SliderCurve.CalculateCumulativeLengths(curvePath);
                curveLength = data.CachedSliderLength;
                if (curveLength <= 0 && curvePath.Count > 0)
                {
                    foreach (Line l in curvePath)
                        curveLength += l.Rho;
                }
            }
            else
            {
                curvePath = SliderCurve.CalculateCurve(controlPoints, data.CurveType, data.Length);
                cumulativeLengths = SliderCurve.CalculateCumulativeLengths(curvePath);
                curveLength = 0;
                foreach (Line l in curvePath)
                    curveLength += l.Rho;
                data.CachedSliderPath = curvePath;
                data.CachedSliderCumLen = cumulativeLengths;
                data.CachedSliderLength = curveLength;
                curvePath = SliderCurve.ClonePath(curvePath);
            }

            // 끝 위치 — 곡선 끝점 (repeat arrow / slider end circle용).
            // HitBurst 위치는 별도 계산 (HitBurstEndPosition).
            if (curvePath.Count > 0)
                EndPosition = curvePath[curvePath.Count - 1].p2;
            else
                EndPosition = headPos;

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
                HitBurstEndPosition = headPos;

            // Velocity — osu! stable HitObjectManager.SliderVelocityAt (픽셀/초).
            // lazer Velocity는 px/ms(=이 값/1000)인데, 아래 duration/TimeAtLength는
            // `1000 * distance / Velocity` (px/s 가정)라 lazer 단위를 넣으면 EndTime이 1000배 늘어나고
            // 볼은 거의 안 움직인다. 오버레이는 stable 위에 그리므로 stable 단위를 쓴다.
            int segmentCount = Math.Max(1, data.RepeatCount);
            velocity = BeatmapParser.SliderVelocityPxPerSecond(beatmap, startTime);
            // 1E-298 BPM이면 velocity가 Inf가 된다. 예전엔 1px/s로 바꿔서
            // 04:08 이후 슬라이더가 끝나지 않았다. Inf/0은 duration 0 (즉시 종료)로 둔다.

            // 기본 combo 색상 — SkinManager에서 조회 (이미 comboColour로 전달받음)
            // comboColour는 생성자 파라미터로 받음

            // 텍스처 로드
            pTexture texHitCircle = texManager.Load("hitcircle");
            pTexture texSliderStart = texManager.Load("sliderstartcircle");
            pTexture texSliderEnd = texManager.Load("sliderendcircle");
            pTexture texReverseArrow = texManager.Load("reversearrow");

            // 슬라이더 볼 — osu! stable: LoadAll("sliderb", SkinSource.All, false)
            // dashSeparator=false → sliderb0, sliderb1, ...
            // LoadAll은 유저 스킨·임베디드 기본 스킨 어디에도 텍스처가 없으면 null을 반환한다.
            // 그대로 .Length를 읽으면 NRE(A3) — 빈 배열로 합치면 아래 `.Length > 0` 가드가
            // 자연히 걸러 sliderBall/sliderFollower가 null로 남는다(모든 사용처가 null 가드됨).
            pTexture[] sliderBallTextures = texManager.LoadAll("sliderb", SkinSource.All, false) ?? new pTexture[0];
            bool usingDefault = sliderBallTextures.Length > 0 && sliderBallTextures[0].Source == SkinSource.Osu;

            // 슬라이더 팔로워 — osu! stable: LoadAll("sliderfollowcircle")
            pTexture[] sliderFollowerTextures = texManager.LoadAll("sliderfollowcircle") ?? new pTexture[0];

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

            // 슬라이더 틱 거리 — osu! stable UpdateCalculations:
            //   v<8: SliderScoringPointDistance
            //   v≥8: SliderScoringPointDistance / BpmMultiplierAt
            // Velocity가 px/s이므로 (velocity * beatLength) / tickRate 를 쓰면 틱 간격이 1000배가 된다.
            double tickDistance = BeatmapParser.SliderTickDistance(beatmap, startTime, data.Length);
            double scoringDistance = 0;
            double scoringLengthTotal = 0;
            double minTickDistanceFromEnd = 0.01 * velocity;

            for (int i = 0; i < segmentCount; i++)
            {
                // 세그먼트마다 리셋 — osu! stable SliderOsu.cs:818-819.
                // distanceToEnd는 한 번 통과하는 경로 길이에서 시작한다. curveLength는 data.Length로
                // 잘린 뒤의 실제 커브 길이라 stable의 total과 같은 값이다.
                // skipTick은 세그먼트 내내 유지 — 한 번 서면 그 세그먼트의 남은 틱은 전부 생략된다.
                double distanceToEnd = curveLength;
                bool skipTick = false;
                List<pSprite> segmentDots = new List<pSprite>();

                bool reverse = (i % 2) == 1;
                Vector2 circlePos = reverse ? headPos : EndPosition;

                // segment 시작 시간
                double segmentStartTime = currentTime;
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

                    double duration = DurationMs(distance, velocity);

                    currentTime += duration;
                    scoringDistance += distance;

                    // 슬라이더 틱 (scoring points) — osu! stable UpdateCalculations 포팅
                    // tickDistance > 0 가드 (C6): Length<=0인 에일리언 슬라이더는 tickDistance가
                    // <=0로 클램프되는데, 그러면 `scoringDistance -= tickDistance`가 줄지 않아
                    // while이 영영 안 끝나고 dot을 무한 생성해 로더가 멈춘다.
                    while (scoringDistance >= tickDistance && tickDistance > 0 && !skipTick)
                    {
                        scoringLengthTotal += tickDistance;
                        scoringDistance -= tickDistance;
                        distanceToEnd -= tickDistance;

                        skipTick = distanceToEnd <= minTickDistanceFromEnd;
                        if (skipTick)
                            break;

                        int scoreTime = TimeAtLength((float)scoringLengthTotal);

                        // 길이 0 선분(p1==p2, 중복 제어점을 가진 에일리언 맵)에서 Distance가 0이면
                        // 나눗셈이 NaN을 만들어 틱이 NaN 좌표에 찍혔다 (C6). 이런 틱은 위치가
                        // 정의되지 않고 stable도 radius 검사에서 누락하므로 dot 생성을 건너뛴다
                        // (거리 bookkeeping은 위에서 이미 완료). 정상 선분(dist>0)은 동작 무변화.
                        float segDist = Vector2.Distance(p1, p2);
                        if (segDist <= 0)
                            continue;
                        float thisPointRatio = 1 - (float)(scoringDistance / segDist);
                        Vector2 adjustedPos = p1 + (p2 - p1) * thisPointRatio;
                        if (!PlayfieldBounds.Contains(adjustedPos))
                            continue;

                        pTexture texScorePoint = texManager.Load("sliderscorepoint");
                        if (texScorePoint != null)
                        {
                            pSprite scoringDot = new pSprite(texScorePoint, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                                adjustedPos, SpriteManager.DrawOrderBwd(startTime + 3), false, Color.White);
                            if (texScorePoint.Source == SkinSource.Osu)
                                scoringDot.Additive = true;

                            if (firstRun)
                            {
                                int dotStartTime = (scoreTime - startTime) / 2 + startTime - difficulty.PreEmptSliderComplete;
                                int dotEndTime = dotStartTime + 150;
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Fade, 0f, 1f, dotStartTime, dotEndTime, EasingTypes.None));
                                // H9: HD(Hidden Override)에서 각 틱이 scoreTime에 맞춰 1→0 페이드
                                // — stable SliderOsu.cs:895. 시작은 최대 1000ms 전. HiddenActive 아니면 무변화.
                                if (HitCircleOsu.HiddenActive)
                                    scoringDot.Transformations.Add(new Transformation(
                                        TransformationType.Fade, 1f, 0f, Math.Max(dotEndTime, scoreTime - 1000), scoreTime, EasingTypes.None));
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
                                // H9: HD(Hidden Override)에서 각 틱이 scoreTime에 맞춰 1→0 페이드
                                // — stable SliderOsu.cs:903. 시작은 최대 1000ms 전. HiddenActive 아니면 무변화.
                                if (HitCircleOsu.HiddenActive)
                                    scoringDot.Transformations.Add(new Transformation(
                                        TransformationType.Fade, 1f, 0f, Math.Max(displayStartTime, scoreTime - 1000), scoreTime, EasingTypes.None));
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Scale, 0.5f, 1.2f, displayStartTime - 200, displayStartTime - 50, EasingTypes.None));
                                scoringDot.Transformations.Add(new Transformation(
                                    TransformationType.Scale, 1.2f, 1f, displayStartTime - 50, displayStartTime + 150, EasingTypes.Out));
                            }

                            // osu! stable: 시작원/끝원 HitObjectRadius 반경 내 틱은 제외
                            float radiusSquared = difficulty.HitObjectRadius * difficulty.HitObjectRadius;
                            float distToStart = (adjustedPos - headPos).LengthSquared;
                            float distToEnd = (adjustedPos - EndPosition).LengthSquared;
                            if (distToStart >= radiusSquared && distToEnd >= radiusSquared)
                            {
                                sliderScorePoints.Add(scoringDot);
                                tickScoreTimes.Add(scoreTime); // 소비 판정용 — 리스트 병렬 유지
                                segmentDots.Add(scoringDot);
                            }
                        }
                    }
                }

                // 세그먼트 경계 보정 — osu! stable SliderOsu.cs:921-931.
                // 다음 세그먼트의 첫 틱이 리버스 지점을 기준으로 이 세그먼트의 마지막 틱과
                // 대칭이 되도록 남은 거리를 미러링한다.
                scoringLengthTotal += scoringDistance;
                if (skipTick)
                {
                    // 끝에 마지막 틱이 아예 없었으면 미러링할 대상도 없다
                    scoringDistance = 0;
                }
                else
                {
                    scoringLengthTotal -= tickDistance - scoringDistance;
                    scoringDistance = tickDistance - scoringDistance;
                }

                // 틱은 볼이 지나갈 때가 아니라 세그먼트가 끝날 때 일괄로 사라진다 — stable :933-935 (H22)
                foreach (pSprite dot in segmentDots)
                    dot.Transformations.Add(new Transformation(
                        TransformationType.Fade, 0f, 0f, (int)currentTime, (int)currentTime, EasingTypes.None));

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

                // 리버스 방향 — lazer DrawableSliderRepeat: 경로에서 현재 위치와 다른
                // 다음 점을 찾아 Atan2. 마지막 선분만 쓰면 길이 0/지그재그 테셀 선분이
                // 화살표를 엉뚱한 곳으로 돌린다.
                float fromLen = reverse ? 0f : (float)curveLength;
                float angle = AimAngleAt(fromLen, lookTowardStart: !reverse, (float)curveLength);

                // HitCircleSliderEnd 생성
                HitObjectData endData = new HitObjectData();
                endData.Position = circlePos;
                endData.BasePosition = circlePos;
                endData.StartTime = circleStartTime;
                endData.EndTime = circleStartTime;
                endData.Type = HitObjectType.Normal;
                endData.Colour = comboColour;

                bool isReverse = (i < segmentCount - 1); // 마지막 세그먼트가 아니면 리버스
                double segmentDuration = DurationMs(data.Length, velocity);
                HitCircleSliderEnd endCircle = new HitCircleSliderEnd(endData, difficulty, texManager,
                    appearTime, isReverse, angle, circleStartTime, comboColour,
                    firstRun, !reverse, startTime, segmentDuration);

                endCircles.Add(endCircle);

                firstRun = false;

                // Aspire dummy (The Solace of Oblivion 00:34 `B,2048,-58.25` 등):
                // 경로 길이가 0이면 이 세그먼트에서 currentTime이 안 늘어난다.
                // 남은 리핏의 끝원/리버스는 같은 ms에 나타났다가 바로 사라져 화면에 안 나오는데,
                // 끝원마다 원+오버레이+화살표 3스프라이트라 2048리핏이면 SM에 ~6000개가 올라간다.
                // preempt에 보이는 첫 리버스만 남기고 이후는 만들지 않는다.
                if (currentTime <= segmentStartTime)
                    break;
            }

            // virtualEndTime = currentTime — osu! stable: EndTime = (int)currentTime
            // 이렇게 하면 virtualEndTime과 segmentDuration이 정확히 일치함
            virtualEndTime = (int)currentTime;
            data.EndTime = virtualEndTime;
            data.SliderVirtualEndTime = virtualEndTime;

            // 슬라이더 볼 — 같은 슬라이더 시작원 위, 더 이른 슬라이더보다는 아래.
            // 예전 0.99/1.0 고정은 모든 볼이 모든 원 위라, 나중에 나온 슬라이더 볼이
            // 앞 슬라이더 원을 뚫고 올라왔다 (lazer는 슬라이더 단위 스택).
            float ballDepth = SpriteManager.DrawOrderBwd(startTime - 8);
            float followerDepth = SpriteManager.DrawOrderBwd(startTime - 9);
            float specDepth = SpriteManager.DrawOrderBwd(startTime - 10);
            float ndDepth = SpriteManager.DrawOrderBwd(startTime - 7);

            if (sliderBallTextures.Length > 0)
            {
                sliderBall = new pAnimation(sliderBallTextures, Fields.Gamefield, Origins.Centre, Clocks.Audio,
                    headPos, ballDepth, false, usingDefault ? SkinManager.LoadColour("SliderBall") : Color.White);
                sliderBall.SetFramerateFromSkin();
                sliderBall.TrackRotation = true;
                // osu! stable: FrameDelay = Math.Max((150 / Velocity) * SIXTY_FRAME_TIME, SIXTY_FRAME_TIME)
                // SIXTY_FRAME_TIME = 1000/60 ≈ 16.67
                double velForAnim = (velocity > 0 && !double.IsInfinity(velocity)) ? velocity : 1.0;
                sliderBall.FrameDelay = Math.Max((150.0 / velForAnim) * (1000.0 / 60.0), 1000.0 / 60.0);
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
                            headPos, specDepth, false, Color.White);
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
                            headPos, ndDepth, false, Color.FromArgb(5, 5, 5));
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
                    headPos, followerDepth, true, Color.White);
                sliderFollower.SetFramerateFromSkin();
                sliderFollower.Alpha = 0f;
                // transformation 없음 — AddToSpriteManager에서 tracking 상태에 따라 동적 제어
            }
        }

        /// <summary>
        /// 커브 위 특정 거리의 시간 — osu! stable timeAtLength.
        /// timeAtLength(length) = StartTime + (length / Velocity) * 1000
        /// </summary>
        static double DurationMs(double distance, double velocityPxPerSec)
        {
            if (!(velocityPxPerSec > 0) || double.IsInfinity(velocityPxPerSec))
                return 0;
            double d = 1000.0 * distance / velocityPxPerSec;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) return 0;
            return d;
        }

        int TimeAtLength(float length)
        {
            return data.StartTime + (int)DurationMs(length, velocity);
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

            // 경계 포함(>=) — pos가 홀수 정수(볼이 리버스 화살표에 정확히 도달한 ms)일 때
            // pos%2==1.0이 `>`에선 else로 떨어져 pos%1==0 → PositionAtLength(0) = 시작원으로
            // 1프레임 순간이동했다. stable은 볼을 선분별 Movement 트랜스폼으로 움직여 이 특이점을
            // 안 밟지만, 오버레이는 매 프레임 샘플링이라 경계를 끝점(1)으로 매핑해야 연속이다.
            if (pos % 2 >= 1)
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
        /// lazer DrawableSliderRepeat: AlmostEquals가 아닌 다음 곡선 점으로 향하는 각.
        /// 1px 이상 떨어진 점을 찾을 때까지 전 경로를 걸으면 Aspire에서 프레임이 죽는다.
        /// 첫 번째로 다른 점만 보고, 최대 24 세그먼트만 본다.
        /// </summary>
        float AimAngleAt(float fromLength, bool lookTowardStart, float visibleLength)
        {
            if (curvePath == null || curvePath.Count == 0)
                return 0;

            int i = 0;
            if (cumulativeLengths != null && cumulativeLengths.Count > 0)
            {
                int bs = cumulativeLengths.BinarySearch(fromLength);
                if (bs < 0) bs = Math.Min(~bs, cumulativeLengths.Count - 1);
                i = bs;
            }
            if (i < 0) i = 0;
            if (i >= curvePath.Count) i = curvePath.Count - 1;

            Vector2 from = fromLength <= 0 ? curvePath[0].p1 : PositionAtLength(fromLength);

            const float epsSq = 0.0001f;
            const int maxSearch = 24;
            int steps = 0;

            if (lookTowardStart)
            {
                for (int k = i; k >= 0; k--)
                {
                    Vector2 p = curvePath[k].p1;
                    Vector2 diff = p - from;
                    if (diff.LengthSquared > epsSq)
                        return (float)Math.Atan2(diff.Y, diff.X);
                    if (++steps >= maxSearch) break;
                }
            }
            else
            {
                for (int k = i; k < curvePath.Count; k++)
                {
                    Vector2 p = curvePath[k].p2;
                    Vector2 diff = p - from;
                    if (diff.LengthSquared > epsSq)
                        return (float)Math.Atan2(diff.Y, diff.X);
                    if (++steps >= maxSearch) break;
                }
            }
            return 0;
        }

        float SnakingProgress(int timeMs)
        {
            int startTime = data.StartTime;
            if (timeMs >= startTime) return 1f;
            float progress = (float)(timeMs - (startTime - difficulty.PreEmpt)) / (difficulty.PreEmpt / 3f);
            if (progress < 0f) return 0f;
            if (progress > 1f) return 1f;
            return progress;
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
        /// osu! stable SliderOsu.ModifyPosition(:1395-1436)처럼 **슬라이더 전체**를 옮긴다.
        /// 기하는 생성자에서 base 좌표로 만들어졌고, 스택 오프셋은 여기서 한 번만 적용된다.
        /// </summary>
        public void UpdateStackedPosition()
        {
            // 시작 원은 부모와 data를 공유하므로 스택된 data.Position을 그대로 읽는다.
            if (startCircle != null)
                startCircle.UpdateStackedPosition();

            Vector2 change = data.Position - data.BasePosition;
            if (change == Vector2.Zero) return;

            // 커브 경로 — 바디/볼/틱 위치 계산이 전부 여기서 나오므로 이것만 옮기면 셋 다 따라온다.
            // (길이는 평행이동에 불변이라 cumulativeLengths는 그대로)
            for (int i = 0; i < curvePath.Count; i++)
            {
                curvePath[i].p1 += change;
                curvePath[i].p2 += change;
            }

            EndPosition += change;
            HitBurstEndPosition += change;

            // 틱 스프라이트는 생성 시 좌표가 굳어 있다
            foreach (pSprite dot in sliderScorePoints)
                dot.Position += change;

            // 끝 원 + 리버스 화살표 — 자기 HitObjectData를 따로 가지므로 스택 오프셋이 닿지 않는다
            foreach (HitCircleSliderEnd endCircle in endCircles)
                endCircle.ModifyPosition(change);

            cachedProgress = -1;
            lastBakedVertexCount = 0;
            bodyBakeFrozen = false;
            snakingFrozen = false;
            bodyBoundsValid = false;
            bodyPathVerts.Clear();
            bodyScreenFullVerts.Clear();
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
        /// 틱 소비 상태 리셋 — retry(시간 대역행) 시 HOM에서 호출.
        /// 소비 숨김 트랜스폼(TagTickConsumed)을 걷어내 새 시도에서 틱이 다시 보이게 한다.
        /// (OverlayForm의 retry 맵 재로드가 객체를 재생성하면 무의미하지만, HOM 단독
        /// 리셋 경로에서도 상태가 새기지 않도록 방어한다.)
        /// </summary>
        public void ResetTickConsumption()
        {
            nextConsumableTick = 0;
            foreach (pSprite dot in sliderScorePoints)
            {
                bool removed = false;
                for (int i = dot.Transformations.Count - 1; i >= 0; i--)
                {
                    if (dot.Transformations[i].TagNumeric == TagTickConsumed)
                    {
                        dot.Transformations.RemoveAt(i);
                        removed = true;
                    }
                }
                if (removed) dot.ComputeTimeRange();
            }
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

            // 슬라이더 바디 캐시 무효화 — HitObjectRadius 변경 시 재생성 필요
            cachedProgress = -1;
            lastBakedVertexCount = 0;
            bodyBakeFrozen = false;
            snakingFrozen = false;
            bodyBoundsValid = false;
            bodyPathVerts.Clear();
            bodyScreenFullVerts.Clear();
        }

        /// <summary>
        /// SpriteManager에 스프라이트 한 번 추가 — LoadBeatmap 시 호출.
        /// </summary>
        public void AddToSpriteManager(SpriteManager sm, int timeMs)
        {
            // 시작 원
            if (startCircle != null)
                startCircle.AddToSpriteManager(sm);
            // 끝 원들 — appear == arrival 이면 가시 구간이 0이라 SM에 넣지 않는다.
            foreach (HitCircleSliderEnd endCircle in endCircles)
            {
                if (endCircle.AppearTime >= endCircle.ArrivalTime)
                    continue;
                endCircle.AddToSpriteManager(sm);
            }
            // 슬라이더 틱
            foreach (pSprite scorePoint in sliderScorePoints)
                if (!sm.Contains(scorePoint)) sm.Add(scorePoint);
            // osu-stable SliderOsu SpriteCollection: follower → spec → nd → ball.
            // 같은 Depth(0.99)면 먼저 Add한 쪽이 위이므로 follower를 ball보다 먼저 넣는다.
            if (sliderFollower != null && !sm.Contains(sliderFollower)) sm.Add(sliderFollower);
            if (sliderBallNd != null && !sm.Contains(sliderBallNd)) sm.Add(sliderBallNd);
            if (sliderBall != null && !sm.Contains(sliderBall)) sm.Add(sliderBall);
            if (sliderBallSpec != null && !sm.Contains(sliderBallSpec)) sm.Add(sliderBallSpec);
        }

        /// <summary>
        /// 바디 FBO 해제 — 슬라이더를 버리기 전에 반드시 호출할 것.
        /// MmSliderRenderer.Draw는 RenderTarget2D를 새로 만들고 "호출자가 Dispose 책임"이라
        /// 명시하는데, RenderTarget2D에는 파이널라이저가 없어서 그냥 버리면 GL 텍스처와
        /// 프레임버퍼가 영구히 샌다 (B1).
        /// </summary>
        public void Dispose()
        {
            if (cachedFbo != null)
            {
                cachedFbo.Dispose();
                cachedFbo = null;
            }
            cachedBodySprite = null;
            cachedProgress = -1;
            lastBakedVertexCount = 0;
            bodyBakeFrozen = false;
            snakingFrozen = false;
            bodyBoundsValid = false;
            bodyPathVerts.Clear();
            bodyScreenFullVerts.Clear();
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
            Vector2 ballPos = Vector2.Zero;
            bool haveBallPos = false;

            // 슬라이더 볼 (StartTime ~ VirtualEndTime)
            if (sliderBall != null && timeMs >= data.StartTime && timeMs <= virtualEndTime)
            {
                if (timeMs != cachedBallTime)
                {
                    cachedBallTime = timeMs;
                    cachedBallPos = GetBallPosition(timeMs);
                }
                ballPos = cachedBallPos;
                haveBallPos = true;
                sliderBall.Position = ballPos;

                int segmentCount = Math.Max(1, data.RepeatCount);
                float length = (float)(virtualEndTime - data.StartTime);
                // length<=0 (0-duration 슬라이더)면 0/0=NaN → (int)NaN이 쓰레기 세그먼트 인덱스를
                // 만들어 Reverse/Flip이 뒤틀린다 (C6). GetBallPosition과 동일하게 0으로 가드.
                float pos = length > 0 ? (timeMs - data.StartTime) / (length / segmentCount) : 0;
                int currentSegment = (int)pos;
                bool isReverseSegment = (currentSegment % 2) == 1;
                sliderBall.Reverse = isReverseSegment;
                if (SkinManager.Current != null)
                    sliderBall.FlipHorizontal = isReverseSegment && SkinManager.Current.SliderBallFlip;

                if (sliderBallNd != null) sliderBallNd.Position = ballPos;
                if (sliderBallSpec != null) sliderBallSpec.Position = ballPos;
            }

            // 틱 소비 — 볼이 tracking 상태로 틱을 지나치는 순간 그 틱을 즉시 숨긴다.
            // 통과 순간 tracking이 아니었으면(놓친 틱) 남겨두고, 세그먼트 끝 일괄 페이드(H22)가
            // 처리한다 — stable에서 미스한 틱이 남는 것과 동일. 포인터는 시간순으로만 전진하므로
            // 각 틱은 통과 프레임에 정확히 한 번 판정된다. 숨김은 통과 시각(scoreTime)에 앵커된
            // zero-duration 페이드(H22 패턴)라 이후 시간 어디서 그려도 일관된다.
            while (nextConsumableTick < tickScoreTimes.Count && timeMs >= tickScoreTimes[nextConsumableTick])
            {
                if (currentTracking == 1)
                {
                    pSprite dot = sliderScorePoints[nextConsumableTick];
                    int consumeTime = tickScoreTimes[nextConsumableTick];
                    Transformation hide = new Transformation(
                        TransformationType.Fade, 0f, 0f, consumeTime, consumeTime, EasingTypes.None);
                    hide.TagNumeric = TagTickConsumed;
                    dot.Transformations.Add(hide);
                    dot.ComputeTimeRange();
                }
                nextConsumableTick++;
            }

            // 슬라이더 팔로워 — tracking 기반 애니메이션 (osu-stable InitSlide/KillSlide)
            if (sliderFollower != null)
            {
                if (timeMs >= data.StartTime && timeMs <= virtualEndTime + 200)
                {
                    if (!haveBallPos)
                    {
                        ballPos = GetBallPosition(timeMs);
                        haveBallPos = true;
                    }
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

            // lazer DrawableSliderRepeat.UpdateSnakingPosition
            // 아직 안 나타난 리버스는 건너뛰고, 각은 방향당 한 번만 계산한다.
            // Aspire 2048 리버스에서 매 화살표마다 경로를 걷던 것이 FPS를 죽였다.
            if (curvePath != null && curvePath.Count > 0 && endCircles.Count > 0)
            {
                float progress = SnakingProgress(timeMs);
                if (progress >= 1f && snakingFrozen)
                {
                    // already snapped
                }
                else
                {
                    float visibleLen = (float)(curveLength * progress);
                    Vector2 snakeStart = PositionAtLength(0);
                    Vector2 snakeEnd = PositionAtLength(visibleLen);
                    float aimFar = float.NaN;
                    float aimNear = float.NaN;
                    bool any = false;
                    for (int i = 0; i < endCircles.Count; i++)
                    {
                        HitCircleSliderEnd end = endCircles[i];
                        if (!end.HasReverseArrow) continue;
                        if (timeMs < end.AppearTime) continue;
                        if (timeMs >= end.ArrivalTime) continue;
                        any = true;
                        Vector2 pos = end.SnakingAtFarEnd ? snakeEnd : snakeStart;
                        float aim;
                        if (end.SnakingAtFarEnd)
                        {
                            if (float.IsNaN(aimFar))
                                aimFar = AimAngleAt(visibleLen, true, visibleLen);
                            aim = aimFar;
                        }
                        else
                        {
                            if (float.IsNaN(aimNear))
                                aimNear = AimAngleAt(0, false, visibleLen);
                            aim = aimNear;
                        }
                        end.UpdateSnaking(pos, aim);
                    }
                    if (progress >= 1f)
                        snakingFrozen = true;
                    else if (any)
                        snakingFrozen = false;
                }
            }
        }

        /// <summary>
        /// 슬라이더 바디 — lazer Path처럼 보이는 폴리라인을 capsule SDF로 FBO에 굽고
        /// SpriteManager가 그라디언트로 합성한다. 스네이킹이 끝나면 다시 굽지 않는다.
        /// </summary>
        public void DrawBody(MmSliderRenderer renderer, GameField gameField, int timeMs, Matrix4 projectionMatrix, SpriteManager sm)
        {
            if (curvePath == null || curvePath.Count == 0) return;

            int startTime = data.StartTime;
            if (timeMs < startTime - difficulty.PreEmpt) return;
            if (timeMs > virtualEndTime + DifficultyCalculator.FadeOut) return;

            float progress = SnakingProgress(timeMs);
            float bodyRadius = difficulty.HitObjectRadius * gameField.Ratio;

            if (!bodyBoundsValid
                || Math.Abs(bodyBoundsRatio - gameField.Ratio) > 0.0001f
                || Math.Abs(bodyBoundsRadius - bodyRadius) > 0.05f)
            {
                if (cachedBodySprite != null) { sm.Remove(cachedBodySprite); cachedBodySprite = null; }
                if (cachedFbo != null) { cachedFbo.Dispose(); cachedFbo = null; }
                cachedProgress = -1;
                lastBakedVertexCount = 0;
                bodyBakeFrozen = false;
                ComputeBodyBounds(gameField, bodyRadius);
            }

            if (bodyBakeFrozen && cachedFbo != null && cachedBodySprite != null)
            {
                if (!sm.Contains(cachedBodySprite))
                    sm.Add(cachedBodySprite);
                return;
            }

            GetPathToProgress(bodyPathVerts, progress, gameField);
            if (bodyPathVerts.Count == 0) return;

            bool rewind = progress + 0.0001f < cachedProgress;
            if (rewind)
                bodyBakeFrozen = false;

            bool clearSdf = cachedFbo == null || cachedBodySprite == null || rewind;
            int firstSeg = clearSdf ? 0 : Math.Max(0, lastBakedVertexCount - 2);

            int colourIndex = comboColourIndex;
            Color trackOverride = SkinManager.LoadColour("SliderTrackOverride");
            if (trackOverride.A > 0)
                colourIndex = 0;

            cachedBodySprite = renderer.DrawSdf(bodyPathVerts, firstSeg, clearSdf, bodyRadius, colourIndex,
                data.StartTime, virtualEndTime, difficulty.PreEmpt, difficulty.FadeIn,
                bodyDrawLeft, bodyDrawTop, bodyDrawWidth, bodyDrawHeight,
                ref cachedFbo, cachedBodySprite);

            lastBakedVertexCount = bodyPathVerts.Count;
            cachedProgress = progress;

            if (progress >= 1f && cachedFbo != null && cachedBodySprite != null)
                bodyBakeFrozen = true;

            if (cachedBodySprite != null && !sm.Contains(cachedBodySprite))
                sm.Add(cachedBodySprite);
        }

        /// <summary>
        /// lazer SliderPath.GetPathToProgress(0, progress) — 계산된 커브 점을 그대로 쓴다.
        /// stable DrawOGL의 min_dist 병합은 코드를 건너뛰게 해서 쓰지 않는다.
        /// </summary>
        void GetPathToProgress(List<Vector2> path, float progress, GameField gameField)
        {
            path.Clear();
            if (bodyScreenFullVerts.Count == 0) return;

            path.Add(bodyScreenFullVerts[0]);
            if (progress <= 0f)
                return;

            double d1 = curveLength * progress;
            if (cumulativeLengths != null)
            {
                int n = Math.Min(curvePath.Count, cumulativeLengths.Count);
                int maxFull = bodyScreenFullVerts.Count - 1;
                for (int i = 0; i < n && i < maxFull && cumulativeLengths[i] <= d1; i++)
                    AddPathPoint(path, bodyScreenFullVerts[i + 1]);
            }

            AddPathPoint(path, gameField.FieldToDisplay(PositionAtLength((float)d1)));
        }

        static void AddPathPoint(List<Vector2> path, Vector2 p)
        {
            if (path.Count > 0)
            {
                Vector2 last = path[path.Count - 1];
                float dx = p.X - last.X;
                float dy = p.Y - last.Y;
                if (dx * dx + dy * dy < 0.0001f)
                    return;
            }
            path.Add(p);
        }
    }
}
