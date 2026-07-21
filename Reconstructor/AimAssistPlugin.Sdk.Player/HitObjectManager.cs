using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AimAssistPlugin.Sdk;
using AimAssistPlugin.Sdk.Audio;
using AimAssistPlugin.Services;
using OsuParsers.Beatmaps;
using OsuParsers.Beatmaps.Objects;
using OsuParsers.Decoders;

namespace AimAssistPlugin.Sdk.Player;

internal class HitObjectManager
{
    public static List<HitObject> hitObjects = new List<HitObject>();

    private const float PLAYFIELD_HEIGHT = 384f;

    public static void CacheHitObjects()
    {
        if (hitObjects.Count > 0)
        {
            ClearHitObjects();
        }
        hitObjects = GetAndTransformHitObjects();
        // osu! stable UpdateStacking 포팅 — 겹치는 노트를 StackOffset만큼 밀어서
        // 오버레이가 렌더링하는 위치와 정확히 일치시킨다. 어시스트가 잘못된 위치로 끌리는 걸 방지.
        ApplyStacking();
    }

    public static void ClearHitObjects()
    {
        hitObjects.Clear();
    }

    public static HitObject GetHitObject(int index)
    {
        return hitObjects[index];
    }

    public static int GetPreEmpt()
    {
        var state = EnlightenService.LatestState;
        if (state == null) return 0;

        // 1순위 — Difficulty Changer override가 반영된 PreEmpt를 OsuEnlightenOverlay2가 직접 전달.
        // DifficultyController.Compute()이 AR + mod + 사용자 override + DT/HT 배수까지 모두 처리한 값.
        // 이 값을 쓰면 Reconstructor와 오버레이가 완전히 동일한 난이도 기준으로 동작.
        if (state.Value.DifficultyReady == 1 && state.Value.PreEmpt > 0)
            return state.Value.PreEmpt;

        // 폴백 — 맵 로드 직후 currentDifficulty가 아직 계산되지 않았을 때.
        // raw AR을 Reconstructor 자체 공식으로 변환. (참고: min=1800이 osu! stable 실제 공식.)
        return (int)MapDifficultyRange(state.Value.BeatmapAR, 1800.0, 1200.0, 450.0, adjustToMods: false);
    }

    /// <summary>
    /// 맵 원본 AR 기준 preEmpt — mod/override 반영 없이 순수 맵 스펙.
    /// stacking 알고리즘의 stackThreshold 계산에만 사용.
    /// osu! stable이 stacking에 Beatmap.DifficultyApproachRate (맵 원본)를 쓰기 때문.
    /// GetPreEmpt()를 쓰면 Difficulty Changer override + DT/HT가 반영된 값이 나와
    /// stacking이 왜곡되고 어시스트가 oversot함.
    /// </summary>
    static float GetMapOriginalPreEmpt()
    {
        var state = EnlightenService.LatestState;
        if (state == null) return 600f; // AR9 기본값 폴백
        return (int)MapDifficultyRange(state.Value.BeatmapAR, 1800.0, 1200.0, 450.0, adjustToMods: false);
    }

    public static float GetHitObjectRadius()
    {
        var state = EnlightenService.LatestState;
        if (state == null) return 0f;

        // 1순위 — Difficulty Changer override가 반영된 HitObjectRadius를 OsuEnlightenOverlay2가 직접 전달.
        // CS + mod + 사용자 override가 모두 반영됨.
        if (state.Value.DifficultyReady == 1 && state.Value.HitObjectRadius > 0f)
            return state.Value.HitObjectRadius;

        // 폴백 — raw CS + HR/EZ 비트로 반지름 계산 (기존 TosuService 동작).
        double cs = state.Value.BeatmapCS;
        uint mods = state.Value.MenuMods;
        if ((mods & SharedMods.EZ) != 0)
            cs *= 0.5;
        else if ((mods & SharedMods.HR) != 0)
            cs *= 1.3;
        return 54.4f - 4.48f * (float)cs;
    }

    public static int GetCurrentHitObjectIndex()
    {
        int time = AudioEngine.GetTime();
        for (int i = 0; i < hitObjects.Count; i++)
        {
            if (hitObjects[i].StartTime > time)
            {
                return Math.Max(0, i - 1);
            }
        }
        if (hitObjects.Count <= 0)
        {
            return 0;
        }
        return hitObjects.Count - 1;
    }

    public static int GetHitObjectsCount()
    {
        return hitObjects.Count;
    }

    public static double MapDifficultyRange(double difficulty, double min, double mid, double max, bool adjustToMods)
    {
        var state = EnlightenService.LatestState;
        if (adjustToMods && state != null)
        {
            uint mods = state.Value.MenuMods;
            if ((mods & SharedMods.EZ) != 0)
            {
                difficulty = Math.Max(0.0, difficulty * 0.5);
            }
            else if ((mods & SharedMods.HR) != 0)
            {
                difficulty = Math.Min(10.0, difficulty * 1.4);
            }
        }
        if (difficulty > 5.0)
        {
            return mid + (max - mid) * (difficulty - 5.0) / 5.0;
        }
        if (difficulty < 5.0)
        {
            return mid - (mid - min) * (5.0 - difficulty) / 5.0;
        }
        return mid;
    }

    private static List<HitObject> GetAndTransformHitObjects()
    {
        var state = EnlightenService.LatestState;
        if (state == null) return new List<HitObject>();

        string? osuDir = EnlightenService.OsuInstallDir;
        string? folder = EnlightenService.BeatmapFolder;
        string? filename = EnlightenService.BeatmapOsuFilename;
        if (string.IsNullOrEmpty(osuDir) || string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(filename))
            return new List<HitObject>();

        string path = Path.Combine(osuDir, "Songs", folder, filename);
        if (!File.Exists(path))
            return new List<HitObject>();

        // HR 모드일 때 Y축 미러링 (기존 동작 유지).
        // BasePosition도 같이 미러링해야 stacking이 HR 상태에서도 정확함.
        bool isHR = (state.Value.MenuMods & SharedMods.HR) != 0;
        _cachedIsHR = isHR;
        var beatmap = BeatmapDecoder.Decode(path);
        // StackLeniency를 맵에서 읽어 static에 캐싱 — ApplyStacking이 씀.
        // .osu 파일에 값이 없으면 OsuParsers 기본값 0.7 사용.
        _cachedStackLeniency = (float)beatmap.GeneralSection.StackLeniency;
        List<HitObject> list = beatmap.HitObjects;
        if (isHR)
        {
            foreach (HitObject item in list)
            {
                item.Position = MirrorHardRockPosition(item.Position);
                item.BasePosition = item.Position;
            }
        }
        return list;
    }

    /// <summary>GetAndTransformHitObjects에서 캐싱한 맵의 StackLeniency 값.</summary>
    static float _cachedStackLeniency = 0.7f;

    /// <summary>GetAndTransformHitObjects에서 캐싱한 HR 여부. 슬라이더 tail 미러링에 사용.</summary>
    static bool _cachedIsHR;

    private static Vector2 MirrorHardRockPosition(Vector2 pos)
    {
        return new Vector2(pos.X, 384f - pos.Y);
    }

    // ── Stacking 알고리즘 ──────────────────────────────────────────────
    // osu! stable HitObjectManager.UpdateStacking (v6+)의 정확 포팅.
    // OsuEnlightenOverlay2/Gameplay/HitObjects/HitObjectManagerOsu.cs:UpdateStacking 과 동일.
    // 겹치는 노트(같은 위치에 짧은 간격으로 배치된 노트)를 StackOffset만큼 밀어서 표시.
    // 이 처리가 없으면 어시스트가 밀리기 전 원본 위치로 끌려가 실제 표시 위치와 어긋남.

    /// <summary>
    /// 현재 hitObjects 리스트에 stacking 적용. Position을 StackCount*StackOffset만큼 밀어 저장.
    /// CacheHitObjects()에서 한 번만 호출 — 맵이 바뀌지 않는 한 결과는 변하지 않음.
    /// </summary>
    static void ApplyStacking()
    {
        int count = hitObjects.Count;
        if (count == 0) return;

        // StackOffset = HitObjectRadius / 10. 오버레이 DifficultyValues.StackOffset과 동일 공식.
        float stackOffset = GetHitObjectRadius() / 10f;
        Vector2 stackVector = new Vector2(stackOffset, stackOffset);

        // StackLeniency — 맵에서 읽은 값 사용 (GetAndTransformHitObjects에서 캐싱).
        // .osu 파일의 [General] StackLeniency 값. osu! stable 기본값은 0.7.
        float stackLeniency = _cachedStackLeniency;

        // ⚠️ stackThreshold는 맵 원본 preEmpt로 계산해야 함.
        // osu! stable HitObjectManager.cs:1623 — stackThreshold = PreEmpt * StackLeniency 인데,
        // 이 PreEmpt는 Beatmap.DifficultyApproachRate (맵 원본 AR) 기준. mod/override 반영 안 됨.
        // GetPreEmpt()를 쓰면 Difficulty Changer override + DT/HT가 이중 반영되어
        // stacking 결과가 왜곡되고 어시스트가 oversot하게 됨.
        float stackPreempt = GetMapOriginalPreEmpt();
        float stackThreshold = stackPreempt * stackLeniency;

        const float STACK_LENIENCE = 3f;

        // StackCount 초기화
        for (int i = 0; i < count; i++)
            hitObjects[i].StackCount = 0;

        // Extend end index
        int extendedEndIndex = count - 1;
        for (int i = count - 1; i >= 0; i--)
        {
            int stackBaseIndex = i;
            for (int n = stackBaseIndex + 1; n < count; n++)
            {
                HitObject stackBase = hitObjects[stackBaseIndex];
                if (IsSpinner(stackBase)) break;

                HitObject objectN = hitObjects[n];
                if (IsSpinner(objectN)) continue;

                if (objectN.StartTime - stackBase.EndTime > stackThreshold)
                    break;

                if (Vector2.Distance(stackBase.BasePosition, objectN.BasePosition) < STACK_LENIENCE ||
                    (IsSlider(stackBase) && Vector2.Distance(GetSliderEndPosition(stackBase), objectN.BasePosition) < STACK_LENIENCE))
                {
                    stackBaseIndex = n;
                    objectN.StackCount = 0;
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
            HitObject objectI = hitObjects[i];

            if (objectI.StackCount != 0 || IsSpinner(objectI)) continue;

            if (IsCircle(objectI))
            {
                while (--n >= 0)
                {
                    HitObject objectN = hitObjects[n];
                    if (IsSpinner(objectN)) continue;
                    if (objectI.StartTime - objectN.EndTime > stackThreshold)
                        break;

                    if (n < extendedStartIndex)
                    {
                        objectN.StackCount = 0;
                        extendedStartIndex = n;
                    }

                    if (IsSlider(objectN) && Vector2.Distance(GetSliderEndPosition(objectN), objectI.BasePosition) < STACK_LENIENCE)
                    {
                        int offset = objectI.StackCount - objectN.StackCount + 1;
                        for (int j = n + 1; j <= i; j++)
                        {
                            if (Vector2.Distance(GetSliderEndPosition(objectN), hitObjects[j].BasePosition) < STACK_LENIENCE)
                                hitObjects[j].StackCount -= offset;
                        }
                        break;
                    }

                    if (Vector2.Distance(objectN.BasePosition, objectI.BasePosition) < STACK_LENIENCE)
                    {
                        objectN.StackCount = objectI.StackCount + 1;
                        objectI = objectN;
                    }
                }
            }
            else if (IsSlider(objectI))
            {
                while (--n >= 0)
                {
                    HitObject objectN = hitObjects[n];
                    if (IsSpinner(objectN)) continue;
                    if (objectI.StartTime - objectN.StartTime > stackThreshold)
                        break;

                    if (Vector2.Distance(GetSliderEndPosition(objectN), objectI.BasePosition) < STACK_LENIENCE)
                    {
                        objectN.StackCount = objectI.StackCount + 1;
                        objectI = objectN;
                    }
                }
            }
        }

        // 스택 오프셋 적용 — Position = BasePosition - StackCount * stackVector
        for (int i = 0; i < count; i++)
        {
            HitObject ho = hitObjects[i];
            ho.Position = ho.BasePosition - ho.StackCount * stackVector;
        }
    }

    static bool IsCircle(HitObject ho) { return (ho.Type & 1) != 0; }
    static bool IsSlider(HitObject ho) { return (ho.Type & 2) != 0; }
    static bool IsSpinner(HitObject ho) { return (ho.Type & 8) != 0; }

    /// <summary>
    /// 슬라이더의 끝 위치 (BasePosition 기준). stacking 알고리즘이 끝 위치로 겹침을 판정.
    /// Slider의 SliderPoints 마지막 점이 끝점. 슬라이더가 아니면 BasePosition.
    /// </summary>
    static Vector2 GetSliderEndPosition(HitObject ho)
    {
        if (ho is Slider slider && slider.SliderPoints.Count > 0)
            return slider.SliderPoints[slider.SliderPoints.Count - 1];
        return ho.BasePosition;
    }

    /// <summary>슬라이더/스피너처럼 시간 구간(바디)을 점유하는 객체인가.</summary>
    public static bool IsBodyObject(int index)
    {
        HitObject ho = hitObjects[index];
        return (IsSlider(ho) || IsSpinner(ho)) && ho.EndTime > ho.StartTime;
    }

    /// <summary>
    /// 객체를 "빠져나가는" 위치 (field 좌표, Position과 동일 좌표계 = stacked+mirrored).
    /// 슬라이더는 반복 홀수면 tail에서, 짝수면 head에서 끝난다. 그 외/서클/스피너는 Position(중심).
    /// tail은 SliderPoints 원본이라 HR 미러 + stack 오프셋을 head와 동일하게 맞춰준다.
    /// </summary>
    public static Vector2 GetExitFieldPosition(int index)
    {
        HitObject ho = hitObjects[index];
        if (ho is Slider s && s.SliderPoints.Count > 0 && (s.Repeats % 2 == 1))
        {
            Vector2 tail = s.SliderPoints[s.SliderPoints.Count - 1];
            if (_cachedIsHR) tail = new Vector2(tail.X, PLAYFIELD_HEIGHT - tail.Y);
            float stackOffset = GetHitObjectRadius() / 10f;
            tail -= new Vector2(ho.StackCount * stackOffset, ho.StackCount * stackOffset);
            return tail;
        }
        return ho.Position;
    }
}
