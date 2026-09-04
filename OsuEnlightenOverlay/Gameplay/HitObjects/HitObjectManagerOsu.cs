using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Graphics.Renderers;
using OsuEnlightenOverlay.Memory;
using OsuEnlightenOverlay.Rendering;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Skinning;

namespace OsuEnlightenOverlay.Gameplay.HitObjects
{
    /// <summary>
    /// HitObject 관리자 — osu! stable HitObjectManager 포팅.
    /// HitObject 리스트 관리, Update, Draw.
    /// </summary>
    internal class HitObjectManagerOsu : IDisposable
    {
        SpriteManager spriteManager;
        TextureManager textureManager;
        DifficultyValues difficulty;
        BeatmapData beatmap;
        OsuGlRenderer renderer;
        MmSliderRenderer sliderRenderer;
        FollowPointRenderer followPointRenderer;

        public HitObjectManagerOsu(SpriteManager spriteManager, TextureManager textureManager, OsuGlRenderer renderer)
        {
            this.spriteManager = spriteManager;
            this.textureManager = textureManager;
            this.renderer = renderer;
            followPointRenderer = new FollowPointRenderer(spriteManager, textureManager);

            // 슬라이더 바디 렌더러 생성
            if (renderer != null && renderer.ShaderManager != null)
            {
                sliderRenderer = new MmSliderRenderer(renderer.ShaderManager);
                if (spriteManager != null)
                    spriteManager.SliderBodyRenderer = sliderRenderer;
            }
        }

        /// <summary>
        /// 맵 로드 — HitObject 생성.
        /// </summary>
        public void LoadBeatmap(BeatmapData beatmap, DifficultyValues difficulty)
        {
            this.beatmap = beatmap;
            this.difficulty = difficulty;

            // 기존 생성된 객체 제거 — lazy 생성이므로 live 맵만 비움
            foreach (var kv in liveSliders)
                kv.Value.Dispose();
            liveSliders.Clear();
            liveCircles.Clear();
            liveSpinners.Clear();
            spriteManager.Clear();

            // GamefieldSpriteRatio 설정 — CS 기반 스프라이트 스케일
            // SpriteRatio = SpriteDisplaySize / GamefieldSpriteRes(128)
            const int GamefieldSpriteRes = 128;
            spriteManager.GamefieldSpriteRatio = difficulty.SpriteDisplaySize / GamefieldSpriteRes;

            // Combo 색상 조회 — SkinManager에서
            List<Color> comboColours = SkinManager.GetComboColours();
            int colourCount = comboColours.Count;

            // Combo 할당 — osu! stable HitObjectManager.cs 정확 포팅
            int combo = 0;
            int comboNumber = 0;
            bool forceNew = false;
            int lastBreakPoint = -1;
            int breakCount = beatmap.Breaks.Count;

            if (textureManager != null)
                textureManager.WarmupGameplayTextures();

            for (int i = 0; i < beatmap.HitObjects.Count; i++)
            {
                HitObjectData h = beatmap.HitObjects[i];

                // 브레이크를 지난 첫 객체는 강제로 새 콤보 — osu! stable HitObjectManager.cs:1258-1263
                while (lastBreakPoint + 1 < breakCount &&
                       beatmap.Breaks[lastBreakPoint + 1].EndTime < h.StartTime)
                {
                    lastBreakPoint++;
                    h.NewCombo = true;
                }

                int offset = h.ComboOffset;

                if ((h.Type & HitObjectType.Spinner) != 0)
                {
                    // v≤8: 스피너는 무조건 다음 객체에 새 콤보 강제 — osu! stable HitObjectManager.cs:1267-1277
                    if (beatmap.BeatmapVersion <= 8)
                        forceNew = true;
                    else if (h.NewCombo)
                    {
                        combo += offset;
                        forceNew = true;
                    }
                }
                else if (forceNew || (h.Type & HitObjectType.NewCombo) != 0 || i == 0)
                {
                    comboNumber = 1;
                    combo += offset + 1;
                    forceNew = false;
                }
                else
                {
                    comboNumber++;
                }

                Color comboColour = colourCount > 0 ? comboColours[combo % colourCount] : Color.White;

                // lazy 생성 — 객체를 만들지 않고 데이터에 콤보/색상만 저장.
                // 실제 HitCircleOsu/SliderOsu/SpinnerOsu 생성은 윈도우 진입 시 (UpdateSpriteWindow).
                h.ComboNumber = comboNumber;
                h.ComboColourIndex = colourCount > 0 ? combo % colourCount : 0;
                h.Colour = comboColour;

                // 슬라이더는 커브/EndPosition/VirtualEndTime을 미리 계산 (스택/정렬/FollowPoint용)
                if ((h.Type & HitObjectType.Slider) != 0)
                    SliderOsu.PrecomputeSliderData(h, difficulty, beatmap);
            }

            // 스택 계산 — osu! stable UpdateStacking (v6+). 데이터 기반.
            UpdateStacking();

            // 스택 적용 — 데이터 레벨에서 Position 업데이트.
            // 실제 객체의 UpdateStackedPosition은 생성 시점(윈도우 진입)에 적용된다.
            foreach (HitObjectData h in beatmap.HitObjects)
            {
                Vector2 stackOffset = new Vector2(h.StackCount * difficulty.StackOffset,
                    h.StackCount * difficulty.StackOffset);
                h.Position = h.BasePosition - stackOffset;
                if ((h.Type & HitObjectType.Slider) != 0 && h.SliderComputed)
                    h.BaseEndPosition = h.SliderHitBurstEnd - stackOffset;
            }

            // Followpoint — lazer FollowPointRenderer: 연결 엔트리만, 점은 라이프타임에 풀에서.
            followPointRenderer.Rebuild(beatmap, difficulty);

            // 초기화 — lazy 생성이므로 데이터 리스트만 리셋
            sortedCircleData = null; // BuildSortedLists에서 재생성
            sortedSliderData = null;
            sortedSpinnerData = null;
            sliderColoursValid = false; // 스킨 변경 시 재계산
        }

        int lastUpdateTime = -1; // Retry 감지용

        // 시간 윈도우 기반 스프라이트 동적 추가/제거
        const int SpriteWindowPast = 2000;   // 과거 2초 (잔상 유지)
        const int SpriteWindowFuture = 2000; // 미래 2초 (미리 로드)

        // 슬라이더 색상 캐싱 — 매 프레임 new List<Color> + GetComboColours() 호출 제거
        List<Color> cachedSliderColours;
        Color cachedSliderBorder = Color.White;
        float cachedSliderRadius = -1;
        bool sliderColoursValid = false;

        // 데이터 기반 정렬 리스트 — lazy 생성. StartTime 기준 정렬된 HitObjectData.
        List<HitObjectData> sortedCircleData;
        List<HitObjectData> sortedSliderData;
        List<HitObjectData> sortedSpinnerData;

        // lazy 생성 — 데이터 → 생성된 객체 매핑. 윈도우 진입 시에만 생성, 이탈 시 Dispose.
        readonly Dictionary<HitObjectData, HitCircleOsu> liveCircles = new Dictionary<HitObjectData, HitCircleOsu>();
        readonly Dictionary<HitObjectData, SliderOsu> liveSliders = new Dictionary<HitObjectData, SliderOsu>();
        readonly Dictionary<HitObjectData, SpinnerOsu> liveSpinners = new Dictionary<HitObjectData, SpinnerOsu>();
        // 만료 대상 수집용 스크래치 — dictionary 순회 중 제거할 수 없으므로 재사용 목록에 모은다.
        readonly List<HitObjectData> expiredCircleData = new List<HitObjectData>(64);
        readonly List<HitObjectData> expiredSliderData = new List<HitObjectData>(16);

        void BuildSortedLists()
        {
            sortedCircleData = new List<HitObjectData>();
            sortedSliderData = new List<HitObjectData>();
            sortedSpinnerData = new List<HitObjectData>();
            foreach (HitObjectData d in beatmap.HitObjects)
            {
                if ((d.Type & HitObjectType.Normal) != 0) sortedCircleData.Add(d);
                else if ((d.Type & HitObjectType.Slider) != 0) sortedSliderData.Add(d);
                else if ((d.Type & HitObjectType.Spinner) != 0) sortedSpinnerData.Add(d);
            }
            sortedCircleData.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            sortedSliderData.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            sortedSpinnerData.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        }

        // lazy 생성 헬퍼 — 데이터에서 객체를 가져오되, 없으면 생성.
        HitCircleOsu GetOrCreateCircle(HitObjectData d, bool isFirstObject)
        {
            HitCircleOsu c;
            if (liveCircles.TryGetValue(d, out c)) return c;
            c = new HitCircleOsu(d, difficulty, textureManager, d.Colour, isFirstObject);
            c.SetScaleRatios(spriteManager.GamefieldSpriteRatio, renderer.GameField.Ratio);
            c.ComboNumber = d.ComboNumber;
            liveCircles[d] = c;
            return c;
        }

        SliderOsu GetOrCreateSlider(HitObjectData d, bool isFirstObject)
        {
            SliderOsu s;
            if (liveSliders.TryGetValue(d, out s)) return s;
            s = new SliderOsu(d, difficulty, beatmap, textureManager, d.Colour, d.ComboNumber, d.ComboColourIndex, isFirstObject);
            s.SetScaleRatios(spriteManager.GamefieldSpriteRatio, renderer.GameField.Ratio);
            s.SetComboNumber(d.ComboNumber);
            liveSliders[d] = s;
            return s;
        }

        SpinnerOsu GetOrCreateSpinner(HitObjectData d)
        {
            SpinnerOsu sp;
            if (liveSpinners.TryGetValue(d, out sp)) return sp;
            sp = new SpinnerOsu(d, difficulty, textureManager, renderer.GameField);
            liveSpinners[d] = sp;
            return sp;
        }

        void DestroyCircle(HitObjectData d)
        {
            HitCircleOsu c;
            if (!liveCircles.TryGetValue(d, out c)) return;
            if (c.IsSpriteAdded) c.RemoveFromSpriteManager(spriteManager);
            liveCircles.Remove(d);
        }

        void DestroySlider(HitObjectData d)
        {
            SliderOsu s;
            if (!liveSliders.TryGetValue(d, out s)) return;
            if (s.IsSpriteAdded) s.RemoveFromSpriteManager(spriteManager);
            s.Dispose();
            liveSliders.Remove(d);
        }

        void DestroySpinner(HitObjectData d)
        {
            SpinnerOsu sp;
            if (!liveSpinners.TryGetValue(d, out sp)) return;
            if (sp.IsSpriteAdded) sp.RemoveFromSpriteManager(spriteManager);
            liveSpinners.Remove(d);
        }

        // binary search: startTime >= target 인 첫 인덱스
        static int LowerBound<T>(List<T> list, Func<T, int> getStart, int target)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (getStart(list[mid]) < target)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        // binary search: startTime <= target 인 마지막 인덱스 + 1
        static int UpperBound<T>(List<T> list, Func<T, int> getStart, int target)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (getStart(list[mid]) <= target)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        void UpdateSpriteWindow(int timeMs)
        {
            int minTime = timeMs - SpriteWindowPast;
            int maxTime = timeMs + SpriteWindowFuture;

            if (sortedCircleData == null) BuildSortedLists();

            // binary-search 진입점보다 과거로 밀린 객체는 아래 생성 루프에서 다시 방문되지 않는다.
            // live dictionary를 먼저 정리해 circle 객체와 장시간 slider의 FBO가 맵 끝까지 남지 않게 한다.
            expiredCircleData.Clear();
            foreach (var kv in liveCircles)
            {
                HitObjectData d = kv.Key;
                int endTime = d.StartTime + difficulty.HitWindow50 + DifficultyCalculator.FadeOut;
                if (endTime < minTime)
                    expiredCircleData.Add(d);
            }
            for (int i = 0; i < expiredCircleData.Count; i++)
                DestroyCircle(expiredCircleData[i]);

            expiredSliderData.Clear();
            foreach (var kv in liveSliders)
            {
                HitObjectData d = kv.Key;
                int endTime = (d.SliderComputed ? d.SliderVirtualEndTime : d.EndTime)
                    + DifficultyCalculator.FadeOut;
                if (endTime < minTime)
                    expiredSliderData.Add(d);
            }
            for (int i = 0; i < expiredSliderData.Count; i++)
                DestroySlider(expiredSliderData[i]);

            // HitCircles — binary search로 윈도우 내 데이터만 순회, 진입 시 lazy 생성
            int cStart = LowerBound(sortedCircleData, c => c.StartTime, minTime - DifficultyCalculator.FadeOut - 100);
            int cEnd = UpperBound(sortedCircleData, c => c.StartTime, maxTime);
            for (int i = cStart; i < cEnd; i++)
            {
                HitObjectData d = sortedCircleData[i];
                int startTime = d.StartTime;
                int endTime = startTime + difficulty.HitWindow50 + DifficultyCalculator.FadeOut;
                bool inWindow = startTime <= maxTime && endTime >= minTime;
                HitCircleOsu c;
                bool exists = liveCircles.TryGetValue(d, out c);
                if (inWindow)
                {
                    if (!exists)
                        c = GetOrCreateCircle(d, ReferenceEquals(d, beatmap.HitObjects[0]));
                    if (!c.IsSpriteAdded) { c.AddToSpriteManager(spriteManager); c.IsSpriteAdded = true; }
                }
                else if (exists)
                {
                    DestroyCircle(d);
                }
            }

            // Sliders — binary search로 윈도우 주변만 순회 (767개 전체 순회는 프레임 멈춤 유발)
            // 긴 슬라이더는 StartTime이 창 밖이어도 EndTime이 창 안일 수 있으므로,
            // StartTime 기준 lower bound에서 시작해 EndTime이 minTime 이상인 동안만 순회.
            int sStart = LowerBound(sortedSliderData, d => d.StartTime, minTime - 30000); // 긴 슬라이더 여유
            for (int i = sStart; i < sortedSliderData.Count; i++)
            {
                HitObjectData d = sortedSliderData[i];
                int startTime = d.StartTime;
                int endTime = (d.SliderComputed ? d.SliderVirtualEndTime : d.EndTime) + DifficultyCalculator.FadeOut;
                // StartTime이 maxTime을 넘으면 이후는 전부 창 밖 — 조기 종료
                if (startTime > maxTime) break;
                bool inWindow = startTime <= maxTime && endTime >= minTime;
                SliderOsu s;
                bool exists = liveSliders.TryGetValue(d, out s);
                if (inWindow)
                {
                    if (!exists)
                        s = GetOrCreateSlider(d, ReferenceEquals(d, beatmap.HitObjects[0]));
                    if (!s.IsSpriteAdded)
                    {
                        s.AddToSpriteManager(spriteManager, timeMs);
                        s.IsSpriteAdded = true;
                    }
                }
                else if (exists)
                {
                    DestroySlider(d);
                }
            }

            // Spinners — 전체 순회 (스피너는 맵당 몇 개 안 됨)
            for (int i = 0; i < sortedSpinnerData.Count; i++)
            {
                HitObjectData d = sortedSpinnerData[i];
                int startTime = d.StartTime;
                int endTime = d.EndTime + DifficultyCalculator.FadeOut;
                bool inWindow = startTime <= maxTime && endTime >= minTime;
                SpinnerOsu sp;
                bool exists = liveSpinners.TryGetValue(d, out sp);
                if (inWindow)
                {
                    if (!exists)
                        sp = GetOrCreateSpinner(d);
                    if (!sp.IsSpriteAdded)
                    {
                        sp.ResetState();
                        sp.AddToSpriteManager(spriteManager, 0, 0, 0, 0, 0, 0);
                        sp.IsSpriteAdded = true;
                    }
                }
                else if (exists)
                {
                    DestroySpinner(d);
                }
            }
            followPointRenderer.Update(timeMs);
        }

        /// <summary>
        /// 스택 계산 — osu! stable HitObjectManager.UpdateStacking (v6+) 정확 포팅.
        /// 겹치는 HitObject의 위치를 StackOffset만큼 이동.
        /// </summary>
        void UpdateStacking()
        {
            // 통합 HitObject 리스트 (시간순) — 데이터 기반 (lazy 생성)
            List<StackEntry> entries = new List<StackEntry>();
            foreach (HitObjectData d in beatmap.HitObjects)
            {
                bool isSlider = (d.Type & HitObjectType.Slider) != 0;
                entries.Add(new StackEntry
                {
                    data = d,
                    isSpinner = (d.Type & HitObjectType.Spinner) != 0,
                    isSlider = isSlider,
                    isCircle = (d.Type & HitObjectType.Normal) != 0,
                    basePosition = d.BasePosition,
                    baseEndPosition = isSlider && d.SliderComputed ? d.SliderEndPosition : d.BasePosition,
                    startTime = d.StartTime,
                    endTime = isSlider && d.SliderComputed ? d.SliderVirtualEndTime : d.EndTime
                });
            }
            entries.Sort((a, b) => a.startTime.CompareTo(b.startTime));

            int count = entries.Count;
            if (count == 0) return;

            const int STACK_LENIENCE = 3;
            Vector2 stackVector = new Vector2(difficulty.StackOffset, difficulty.StackOffset);
            float stackThreshold = difficulty.PreEmpt * beatmap.StackLeniency;

            // StackCount 초기화
            for (int i = 0; i < count; i++)
                entries[i].data.StackCount = 0;

            // Extend end index
            int extendedEndIndex = count - 1;
            for (int i = count - 1; i >= 0; i--)
            {
                int stackBaseIndex = i;
                for (int n = stackBaseIndex + 1; n < count; n++)
                {
                    StackEntry stackBase = entries[stackBaseIndex];
                    if (stackBase.isSpinner) break;

                    StackEntry objectN = entries[n];
                    if (objectN.isSpinner) continue;

                    if (objectN.startTime - stackBase.endTime > stackThreshold)
                        break;

                    if (Vector2.Distance(stackBase.basePosition, objectN.basePosition) < STACK_LENIENCE ||
                        (stackBase.isSlider && Vector2.Distance(stackBase.baseEndPosition, objectN.basePosition) < STACK_LENIENCE))
                    {
                        stackBaseIndex = n;
                        objectN.data.StackCount = 0;
                    }
                }

                if (stackBaseIndex > extendedEndIndex)
                {
                    extendedEndIndex = stackBaseIndex;
                    if (extendedEndIndex == count - 1)
                        break;
                }
            }

            // Reverse pass
            int extendedStartIndex = 0;
            for (int i = extendedEndIndex; i > 0; i--)
            {
                int n = i;
                StackEntry objectI = entries[i];

                if (objectI.data.StackCount != 0 || objectI.isSpinner) continue;

                if (objectI.isCircle)
                {
                    while (--n >= 0)
                    {
                        StackEntry objectN = entries[n];
                        if (objectN.isSpinner) continue;
                        if (objectI.startTime - objectN.endTime > stackThreshold)
                            break;

                        if (n < extendedStartIndex)
                        {
                            objectN.data.StackCount = 0;
                            extendedStartIndex = n;
                        }

                        if (objectN.isSlider && Vector2.Distance(objectN.baseEndPosition, objectI.basePosition) < STACK_LENIENCE)
                        {
                            int offset = objectI.data.StackCount - objectN.data.StackCount + 1;
                            for (int j = n + 1; j <= i; j++)
                            {
                                if (Vector2.Distance(objectN.baseEndPosition, entries[j].basePosition) < STACK_LENIENCE)
                                    entries[j].data.StackCount -= offset;
                            }
                            break;
                        }

                        if (Vector2.Distance(objectN.basePosition, objectI.basePosition) < STACK_LENIENCE)
                        {
                            objectN.data.StackCount = objectI.data.StackCount + 1;
                            objectI = objectN;
                        }
                    }
                }
                else if (objectI.isSlider)
                {
                    while (--n >= 0)
                    {
                        StackEntry objectN = entries[n];
                        if (objectN.isSpinner) continue;
                        if (objectI.startTime - objectN.startTime > stackThreshold)
                            break;

                        if (Vector2.Distance(objectN.baseEndPosition, objectI.basePosition) < STACK_LENIENCE)
                        {
                            objectN.data.StackCount = objectI.data.StackCount + 1;
                            objectI = objectN;
                        }
                    }
                }
            }

            // 스택 오프셋 적용 — osu! stable HitObjectManager.cs:1761-1765는 범위 내 전 객체에
            // 무조건 ModifyPosition을 건다 (H20). StackCount==0을 건너뛰면 Position이 낡은 값으로
            // 남을 수 있고, UpdateStackedPosition이 쓰는 `Position - BasePosition` 불변식이 깨진다.
            for (int i = 0; i < count; i++)
            {
                StackEntry e = entries[i];
                e.data.Position = e.basePosition - e.data.StackCount * stackVector;
            }
        }

        struct StackEntry
        {
            public HitObjectData data;
            public bool isSpinner;
            public bool isSlider;
            public bool isCircle;
            public Vector2 basePosition;
            public Vector2 baseEndPosition;
            public int startTime;
            public int endTime;
        }

        /// <summary>
        /// difficulty 변경 시 모든 HitObject의 Transformation 재구성.
        /// LoadBeatmap 전체 재생성 없이 UpdateDifficulty만 호출 — 성능 최적화.
        /// </summary>
        public void UpdateDifficulty(DifficultyValues newDifficulty)
        {
            this.difficulty = newDifficulty;

            // GamefieldSpriteRatio 업데이트 — CS 기반
            const int GamefieldSpriteRes = 128;
            spriteManager.GamefieldSpriteRatio = newDifficulty.SpriteDisplaySize / GamefieldSpriteRes;

            // 콤보 넘버 위치 재계산용 스케일 비율 갱신
            float gsr = spriteManager.GamefieldSpriteRatio;
            float gfr = renderer.GameField.Ratio;

            // 각 HitObject의 Transformation 재구성 + 스케일 비율 갱신 — 생성된 객체만 (lazy)
            foreach (var kv in liveCircles)
            {
                kv.Value.SetScaleRatios(gsr, gfr);
                kv.Value.UpdateDifficulty(newDifficulty);
            }
            foreach (var kv in liveSliders)
            {
                kv.Value.SetScaleRatios(gsr, gfr);
                kv.Value.UpdateDifficulty(newDifficulty);
            }
            foreach (var kv in liveSpinners)
                kv.Value.UpdateDifficulty(newDifficulty);
        }

        // 판정 인덱스 — StartTime → 해당 시간의 judgement 리스트.
        // Aspire 맵처럼 객체가 많을 때 O(N×M) foreach 매칭이 프레임당 수만 번 비교가 되어
        // FPS를 죽인다. StartTime으로 인덱싱해 O(1) 조회로 바꾼다.
        // 같은 StartTime에 여러 타입(circle/slider/spinner)이 있을 수 있으므로 리스트로 둔다.
        readonly Dictionary<int, List<OsuMemoryReader.HitObjectJudgement>> judgementIndex
            = new Dictionary<int, List<OsuMemoryReader.HitObjectJudgement>>(256);
        readonly List<List<OsuMemoryReader.HitObjectJudgement>> judgementListPool
            = new List<List<OsuMemoryReader.HitObjectJudgement>>(64);

        void RebuildJudgementIndex(List<OsuMemoryReader.HitObjectJudgement> judgements)
        {
            foreach (var kv in judgementIndex)
            {
                kv.Value.Clear();
                judgementListPool.Add(kv.Value);
            }
            judgementIndex.Clear();
            if (judgements == null) return;
            for (int i = 0; i < judgements.Count; i++)
            {
                var j = judgements[i];
                List<OsuMemoryReader.HitObjectJudgement> list;
                if (!judgementIndex.TryGetValue(j.StartTime, out list))
                {
                    int last = judgementListPool.Count - 1;
                    if (last >= 0)
                    {
                        list = judgementListPool[last];
                        judgementListPool.RemoveAt(last);
                    }
                    else
                    {
                        list = new List<OsuMemoryReader.HitObjectJudgement>(2);
                    }
                    judgementIndex[j.StartTime] = list;
                }
                list.Add(j);
            }
        }

        // StartTime + typeMask 로 judgement 1개 조회 (없으면 false)
        bool TryGetJudgement(int startTime, int typeMask, out OsuMemoryReader.HitObjectJudgement result)
        {
            result = default(OsuMemoryReader.HitObjectJudgement);
            List<OsuMemoryReader.HitObjectJudgement> list;
            if (!judgementIndex.TryGetValue(startTime, out list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i].Type & typeMask) != 0)
                {
                    result = list[i];
                    return true;
                }
            }
            return false;
        }

        public void Update(int timeMs, List<OsuMemoryReader.HitObjectJudgement> judgements)
        {
            const int GamefieldSpriteRes = 128;
            spriteManager.GamefieldSpriteRatio = difficulty.SpriteDisplaySize / GamefieldSpriteRes;

            RebuildJudgementIndex(judgements);

            if (lastUpdateTime > 0 && timeMs < lastUpdateTime - 2000)
            {
                foreach (var kv in liveCircles)
                {
                    if (kv.Value.IsSpriteAdded) { kv.Value.RemoveFromSpriteManager(spriteManager); kv.Value.IsSpriteAdded = false; }
                }
                foreach (var kv in liveSliders)
                {
                    if (kv.Value.IsSpriteAdded) { kv.Value.RemoveFromSpriteManager(spriteManager); kv.Value.IsSpriteAdded = false; }
                    kv.Value.ResetTickConsumption(); // 새 시도 — 소비된 틱 복원
                    kv.Value.Dispose();
                }
                foreach (var kv in liveSpinners)
                {
                    if (kv.Value.IsSpriteAdded) { kv.Value.RemoveFromSpriteManager(spriteManager); kv.Value.IsSpriteAdded = false; }
                }
                // 순회가 끝난 뒤 Clear
                liveCircles.Clear();
                liveSliders.Clear();
                liveSpinners.Clear();
                followPointRenderer.FreeAll();
                spriteManager.Clear();
            }
            lastUpdateTime = timeMs;

            UpdateSpriteWindow(timeMs);

            // 시간 윈도우 — 이 범위 밖의 HitObject는 처리 스킵 (성능 최적화)
            int timeWindow = difficulty.PreEmpt + 500; // PreEmpt + 여유
            int minTime = timeMs - timeWindow;
            int maxTime = timeMs + timeWindow;

            if (sortedCircleData == null) BuildSortedLists();

            // 슬라이더 바디 먼저 렌더링 (depth buffer 사용, 스프라이트보다 아래)
            // 전체 순회 — 긴 슬라이더(34초+)는 binary search가 StartTime 기준이라 놓침
            if (sliderRenderer != null && renderer != null && renderer.GameField != null)
            {
                // 색상 할당 — 캐싱 (매 프레임 new List<Color> + GetComboColours() 호출 제거)
                float defaultRadius = difficulty.HitObjectRadius * renderer.GameField.Ratio;
                if (!sliderColoursValid || cachedSliderRadius != defaultRadius)
                {
                    cachedSliderColours = new List<Color>();
                    Color trackOverride = SkinManager.LoadColour("SliderTrackOverride");
                    if (trackOverride.A > 0)
                    {
                        cachedSliderColours.Add(trackOverride);
                    }
                    else
                    {
                        cachedSliderColours.AddRange(SkinManager.GetComboColours());
                    }
                    cachedSliderBorder = SkinManager.LoadColour("SliderBorder");
                    cachedSliderRadius = defaultRadius;
                    sliderColoursValid = true;
                }
                sliderRenderer.AssignColours(cachedSliderColours, cachedSliderBorder, cachedSliderRadius);

                // projection matrix — 창 크기 기준 (화면 좌표)
                Matrix4 projMatrix = Matrix4.CreateOrthographicOffCenter(0, renderer.ViewportWidth,
                    renderer.ViewportHeight, 0, -1, 1);

                // 슬라이더 바디 — 생성된 객체만 렌더링 (lazy)
                foreach (var kv in liveSliders)
                {
                    SliderOsu slider = kv.Value;
                    if (slider.VirtualEndTime < minTime)
                        continue;

                    if (slider.IsVisibleAt(timeMs))
                        slider.DrawBody(sliderRenderer, renderer.GameField, timeMs, projMatrix, spriteManager);
                }
            }

            int hcStart = LowerBound(sortedCircleData, c => c.StartTime, minTime);
            int hcEnd = UpperBound(sortedCircleData, c => c.StartTime, maxTime);
            for (int i = hcStart; i < hcEnd; i++)
            {
                HitObjectData d = sortedCircleData[i];
                HitCircleOsu circle;
                if (!liveCircles.TryGetValue(d, out circle)) continue; // 아직 생성 안 됨 (윈도우 밖)

                if (circle.IsArmed)
                    continue;

                OsuMemoryReader.HitObjectJudgement cj;
                if (TryGetJudgement(d.StartTime, 1, out cj))
                {
                    if (cj.IsHit == 1)
                    {
                        bool isHit = cj.ScoreValue > 0;
                        circle.Arm(isHit, timeMs);
                    }
                }

                // osu-stable HitObjectManager.UpdateHitObject:
                // EndTime + HitWindow50 < Time && !IsHit → Hit() → Arm(false)
                if (!circle.IsArmed && timeMs > d.StartTime + difficulty.HitWindow50)
                    circle.Arm(false, timeMs);
            }

            foreach (var kv in liveSliders)
            {
                SliderOsu slider = kv.Value;
                HitObjectData d = kv.Key;
                if (slider.VirtualEndTime < minTime)
                    continue;

                byte tracking = 0;
                byte startIsHit = 0;
                int startHitValue = 0;
                int startScoreValue = 0;
                bool foundJudgement = false;
                OsuMemoryReader.HitObjectJudgement sj;
                if (TryGetJudgement(d.StartTime, 2, out sj))
                {
                    foundJudgement = true;
                    tracking = sj.IsTracking;
                    startIsHit = sj.StartIsHit;
                    startHitValue = sj.StartHitValue;
                    startScoreValue = sj.StartScoreValue;
                }

                if (!slider.StartCircleArmed && startIsHit == 1)
                {
                    if (startHitValue > 0 || startScoreValue > 0)
                        slider.ArmStartCircle(true, timeMs);
                    else if (startHitValue < 0) // IncreaseScoreType.Miss = -131072
                        slider.ArmStartCircle(false, timeMs);
                }

                if (!slider.StartCircleArmed && timeMs > d.StartTime + difficulty.HitWindow50)
                    slider.ArmStartCircle(false, timeMs);

                // osu-stable: IsSliding은 EndTime에 끄지 않는다. 끝까지 hold면 InitSlide가
                // 넣어 둔 Fade(EndTime→EndTime+200)로 팔로워가 줄어들고, 중간에 손을 떼면
                // KillSlide(다음 틱까지 scale 1→2). 메모리 IsTracking이 그 둘을 가른다.
                // 창에서 빠지면 tracking=0으로 덮어쓰지 않는다 — 진행 중 hold가 풀린다.
                if (foundJudgement)
                    slider.SetTracking(tracking);
                slider.UpdateSprites(timeMs);
            }

            foreach (var kv in liveSpinners)
            {
                SpinnerOsu spinner = kv.Value;
                HitObjectData d = kv.Key;
                if (spinner.EndTime < minTime)
                    continue;

                float floatRot = 0;
                int spinState = 0;
                int scoringRot = 0;
                int memReq = 0;
                bool found = false;
                OsuMemoryReader.HitObjectJudgement spj;
                if (TryGetJudgement(d.StartTime, 8, out spj))
                {
                    floatRot = spj.FloatRotationCount;
                    spinState = spj.SpinningState;
                    scoringRot = spj.ScoringRotationCount;
                    memReq = spj.RotationRequirement;
                    found = true;
                }
                int memEndTime = 0;
                if (found)
                {
                    if (floatRot > 0)
                        memEndTime = spinner.EndTime;
                }
                spinner.UpdateState(timeMs, floatRot, spinState, memEndTime, scoringRot, memReq);
            }
        }

        public void Dispose()
        {
            foreach (var kv in liveSliders)
                kv.Value.Dispose();
            liveSliders.Clear();
            liveCircles.Clear();
            liveSpinners.Clear();
            if (followPointRenderer != null)
                followPointRenderer.FreeAll();

            if (sliderRenderer != null)
            {
                sliderRenderer.Dispose();
                sliderRenderer = null;
            }
        }
    }

    /// <summary>
    /// 플레이필드 512×384 + 스택/와이드 여유.
    /// 이 밖은 오버레이에도 안 보이므로 슬라이더 틱 스프라이트를 만들지 않는다.
    /// </summary>
    internal static class PlayfieldBounds
    {
        public const float MinX = -192f;
        public const float MaxX = 704f;
        public const float MinY = -192f;
        public const float MaxY = 576f;

        public static bool Contains(Vector2 p)
        {
            return p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
        }
    }
}
