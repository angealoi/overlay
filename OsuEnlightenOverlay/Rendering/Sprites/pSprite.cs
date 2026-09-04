using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OsuEnlightenOverlay.Rendering.Textures;

namespace OsuEnlightenOverlay.Rendering.Sprites
{
    /// <summary>
    /// 좌표계 — osu! stable Fields enum.
    /// </summary>
    public enum Fields
    {
        // osu! stable Graphics/Sprites/SpriteManager.cs의 Fields enum과 값까지 1:1로 맞춘다.
        // 예전엔 TopLeft=0,TopCentre=1,... 을 먼저 나열해 Gamefield(1)/GamefieldWide(2)/
        // Native(5)/NativeStandardScale(6)과 값이 겹쳤다(6쌍). 지금 쓰는 멤버끼리는 우연히
        // 겹치지 않아 동작했지만, 누가 Fields.Centre 등을 쓰는 순간 좌표계가 뒤바뀌는
        // 지뢰였다(E4). stable 순서/값으로 정렬해 충돌을 제거한다(충실도도 함께 확보).
        Gamefield = 1,           // 게임필드(512x384) — 히트오브젝트 등
        GamefieldWide = 2,
        Storyboard = 3,
        StoryboardCentre = 4,
        Native = 5,              // 네이티브 화면 해상도
        TopLeft = 6,
        TopCentre = 7,
        TopRight = 8,
        CentreLeft = 9,
        Centre = 10,
        CentreRight = 11,
        BottomLeft = 12,
        BottomCentre = 13,
        BottomRight = 14,
        // stable엔 StandardGamefieldScale=15, NativeRight=17 등이 더 있으나 오버레이 미사용.
        NativeStandardScale = 16 // 네이티브 해상도 + 1024x768 스프라이트 스케일
    }

    /// <summary>
    /// 스프라이트 원점 — osu! stable Origins enum.
    /// </summary>
    public enum Origins
    {
        TopLeft,
        Centre,
        CentreLeft,
        TopRight,
        BottomCentre,
        TopCentre,
        Custom,
        CentreRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// 시간 기준 — osu! stable Clocks enum.
    /// </summary>
    public enum Clocks
    {
        Game,
        Audio,
        AudioOnce
    }

    /// <summary>
    /// Update 결과 — osu-stable UpdateResult enum.
    /// </summary>
    public enum UpdateResult
    {
        Discard,
        NotVisible,
        Visible
    }

    /// <summary>
    /// 텍스처 스프라이트 — osu! stable Graphics/Sprites/pSprite.cs 포팅.
    /// </summary>
    internal class pSprite
    {
        public pTexture Texture;
        public Vector2 Position;
        public Vector2 BasePosition;
        public Fields Field;
        public Origins Origin;
        public Clocks Clock;
        public float Depth;
        // Depth 정렬 시 동점 스프라이트의 안정 정렬용 tiebreak (C5/H21).
        // SpriteManager.Add에서 전역 삽입 순서를 부여한다. 같은 Depth면 먼저 넣은 쪽이 위
        // (osu-stable SpriteManager.Add BinarySearch+Insert와 동일).
        // long이라 사실상 오버플로 불가(int면 장시간 세션에서 ~2^31 Add 후 wrap 가능).
        public long StableOrder;
        // SpriteManager.Remove가 O(1)로 마킹만 하고, 실제 리스트 제거는 Update의 압축 패스가
        // 한 번에 처리한다. List.Remove(O(n))를 수천 번 부르면 O(n²)로 수십 초 멈춘다
        // (Aspire 2048회 반복 슬라이더 파괴 시 실측 40s). true = 리스트엔 있지만 제거 예약됨.
        public bool PendingRemove;
        public float Scale = 1f;
        public float Rotation;
        public float Alpha = 1f;
        public Color Colour = Color.White;
        public bool Additive;
        public bool FlipHorizontal;
        public bool FlipVertical = false;
        public Vector2 VectorScale = new Vector2(1, 1);
        public bool AlwaysDraw = true;
        public byte TagNumeric;

        // lazer Path 합성: Texture는 SDF FBO, 이 ID는 1D 그라디언트 (0이면 일반 스프라이트).
        public int SliderPathGradientTexId;

        // 슬라이더 바디 메시 — FBO 텍스처 쿼드 대신 경로 삼각형을 SpriteManager가 직접 그린다.
        // AABB 풀스크린 쿼드는 슬라이더가 많을 때 필레이트만으로 수백 프레임을 깎는다.
        public System.Collections.Generic.List<OsuEnlightenOverlay.Graphics.Primitives.Line> SliderMeshLines;
        public float SliderMeshRadius;
        public int SliderMeshColourIndex = -1;

        // DrawTop/DrawHeight/DrawWidth — osu-stable pSprite, spinner metre용
        public int DrawTop;
        public int DrawHeight = -1; // -1 = 전체 높이
        public int DrawWidth = -1;  // -1 = 전체 너비

        public List<Transformation> Transformations = new List<Transformation>();

        // 시간 범위 캐시 — Transformations에서 자동 계산
        // Update/AddTransformations 호출 시 갱신, 시간 기반 컬링에 사용
        public int StartTime = int.MinValue;
        public int EndTime = int.MaxValue;
        public bool TimeRangeCached = false;

        // 현재 보간된 값들
        public float CurrentAlpha = 1f;
        public float CurrentScale = 1f;
        public float CurrentRotation;
        public Vector2 CurrentPosition;
        public Color CurrentColour = Color.White;

        public pSprite(pTexture texture, Fields field, Origins origin, Clocks clock,
            Vector2 position, float depth, bool alwaysDraw, Color colour)
        {
            Texture = texture;
            Field = field;
            Origin = origin;
            Clock = clock;
            Position = position;
            BasePosition = position;
            Depth = depth;
            AlwaysDraw = alwaysDraw;
            Colour = colour;
            CurrentColour = colour;
        }

        /// <summary>
        /// Transformation 보간값 갱신 — osu-stable pSprite.UpdateTransformations 포팅.
        /// UpdateResult 반환: Discard/NotVisible/Visible.
        /// </summary>
        /// <summary>
        /// Transformations에서 시간 범위 계산 — 컬링용.
        /// </summary>
        public void ComputeTimeRange()
        {
            if (Transformations.Count == 0)
            {
                StartTime = int.MinValue;
                EndTime = int.MaxValue;
                TimeRangeCached = true;
                return;
            }
            int min = int.MaxValue;
            int max = int.MinValue;
            foreach (Transformation t in Transformations)
            {
                // Loop transformation은 무한히 반복되므로 시간 범위를 무한대로 설정
                if (t.Loop)
                {
                    min = int.MinValue;
                    max = int.MaxValue;
                }
                else
                {
                    if (t.Time1 < min) min = t.Time1;
                    if (t.Time2 > max) max = t.Time2;
                }
            }
            StartTime = min;
            EndTime = max;
            TimeRangeCached = true;
        }

        public virtual UpdateResult Update(int time)
        {
            // 시간 범위 기반 조기 종료 — Transformation 순회 전 스킵
            if (!TimeRangeCached)
                ComputeTimeRange();

            // AlwaysDraw=false이고 시간이 EndTime 이후면 Discard (Game clock만)
            if (!AlwaysDraw && Clock == Clocks.Game && time > EndTime)
            {
                CurrentAlpha = 0;
                return UpdateResult.Discard;
            }

            // 시간이 StartTime 이전이면 NotVisible (alpha=0, 순회 생략)
            if (time < StartTime)
            {
                CurrentAlpha = 0;
                return UpdateResult.NotVisible;
            }

            // 기본값으로 리셋
            CurrentAlpha = Alpha;
            CurrentScale = Scale;
            CurrentRotation = Rotation;
            CurrentPosition = Position;
            CurrentColour = Colour;

            int transformationCount = Transformations.Count;

            if (transformationCount == 0)
            {
                return AlwaysDraw ? UpdateResult.Visible : UpdateResult.Discard;
            }

            bool shouldDraw = AlwaysDraw;
            bool hasFuture = false;
            bool hasPast = false;

            bool hasFade = false;
            bool hasScale = false;
            bool hasRotation = false;
            bool hasMovement = false;

            foreach (Transformation t in Transformations)
            {
                if (t.Time1 >= time || t.Time2 > time)
                {
                    hasFuture = true;
                    if (t.Time1 > time)
                        continue;
                }

                if (t.Time2 <= time)
                {
                    hasPast = true;
                    // 과거 transformation 최종값 적용
                    switch (t.Type)
                    {
                        case TransformationType.Fade:
                            CurrentAlpha = t.EndFloat;
                            break;
                        case TransformationType.Scale:
                            CurrentScale = t.EndFloat;
                            break;
                        case TransformationType.Rotation:
                            CurrentRotation = t.EndFloat;
                            break;
                        case TransformationType.Movement:
                            CurrentPosition = t.EndVector;
                            break;
                    }
                    if (t.Time2 < time)
                        continue;
                }

                shouldDraw = true;

                switch (t.Type)
                {
                    case TransformationType.Fade:
                        if (hasFade) break;
                        CurrentAlpha = t.GetValueAt(time);
                        hasFade = true;
                        break;
                    case TransformationType.Scale:
                        if (hasScale) break;
                        CurrentScale = t.GetValueAt(time);
                        hasScale = true;
                        break;
                    case TransformationType.Rotation:
                        if (hasRotation) break;
                        CurrentRotation = t.GetValueAt(time);
                        hasRotation = true;
                        break;
                    case TransformationType.Movement:
                        if (hasMovement) break;
                        CurrentPosition = t.GetVectorAt(time);
                        hasMovement = true;
                        break;
                }
            }

            // discard: !hasFuture && !shouldDraw && (Clock == Game)
            if (!hasFuture && !shouldDraw && Clock == Clocks.Game)
            {
                CurrentAlpha = 0;
                return UpdateResult.Discard;
            }

            // not visible: !(hasFuture && hasPast) && !shouldDraw
            if (!(hasFuture && hasPast) && !shouldDraw)
            {
                CurrentAlpha = 0;
                if (Clock == Clocks.AudioOnce)
                    return UpdateResult.Discard;
                return UpdateResult.NotVisible;
            }

            // Alpha 클램프
            if (CurrentAlpha < 0) CurrentAlpha = 0;
            if (CurrentAlpha > 1) CurrentAlpha = 1;

            return UpdateResult.Visible;
        }

        /// <summary>
        /// 현재 시간에서 보이는지.
        /// </summary>
        public bool IsVisibleAt(int time)
        {
            return CurrentAlpha > 0.001f;
        }
    }
}