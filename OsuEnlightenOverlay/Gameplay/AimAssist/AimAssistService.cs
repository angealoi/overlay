using System;
using System.Collections.Generic;
using System.Diagnostics;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Rendering;
using OpenTK;

namespace OsuEnlightenOverlay.Gameplay.AimAssist
{
    /// <summary>
    /// 마우스 aim assist — 경로추종 + SmoothDamp(Attack) + idle gate.
    /// Release/Resync는 태블릿 절대좌표 snap-back 대응용이라 제외.
    /// 좌표: field → GameField.FieldToDisplay + gameFieldScreen origin (절대 화면 좌표).
    /// </summary>
    internal static class AimAssistService
    {
        static GameField _gameField;
        static float _originX, _originY;
        static List<HitObjectData> _objects;
        static float hitObjectRadius;
        static int _audioTime;
        static int lastAudioTime = -1;
        static int seg;

        static float[] _wpX = new float[0];
        static float[] _wpY = new float[0];
        static int[] _wpT = new int[0];
        static bool[] _wpGap = new bool[0];
        static int _wpN;
        static bool _assistOff;

        static Vector2 _offset;
        static float _offVelX, _offVelY;
        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static long _lastTicks;

        const int MoveHistoryMax = 64;
        static readonly long[] _moveTicks = new long[MoveHistoryMax];
        static readonly float[] _movePosX = new float[MoveHistoryMax];
        static readonly float[] _movePosY = new float[MoveHistoryMax];
        static int _moveHead;

        static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static void Initialize(
            BeatmapData beatmap,
            DifficultyValues difficulty,
            GameField gameField,
            float gameFieldScreenX,
            float gameFieldScreenY)
        {
            _gameField = gameField;
            _originX = gameFieldScreenX;
            _originY = gameFieldScreenY;
            _objects = beatmap != null ? beatmap.HitObjects : null;
            hitObjectRadius = difficulty != null ? difficulty.HitObjectRadius : 0f;
            BuildWaypoints();
            lastAudioTime = -1;
            seg = 0;
            _offset = Vector2.Zero;
            _offVelX = 0f;
            _offVelY = 0f;
            _lastTicks = _clock.ElapsedTicks;
            _moveHead = 0;
            Array.Clear(_moveTicks, 0, _moveTicks.Length);
        }

        public static void Reset()
        {
            lastAudioTime = -1;
            seg = 0;
            _wpN = 0;
            _objects = null;
            _offset = Vector2.Zero;
            _offVelX = 0f;
            _offVelY = 0f;
            _lastTicks = _clock.ElapsedTicks;
            _moveHead = 0;
            Array.Clear(_moveTicks, 0, _moveTicks.Length);
        }

        public static void SetAudioTime(int timeMs)
        {
            _audioTime = timeMs;
        }

        public static void UpdatePlayfield(GameField gameField, float ox, float oy)
        {
            _gameField = gameField;
            _originX = ox;
            _originY = oy;
        }

        static bool IsSpinner(HitObjectData ho)
        {
            return (ho.Type & HitObjectType.Spinner) != 0;
        }

        static bool IsSlider(HitObjectData ho)
        {
            return (ho.Type & HitObjectType.Slider) != 0;
        }

        static bool IsBodyObject(HitObjectData ho)
        {
            return (IsSlider(ho) || IsSpinner(ho)) && ho.EndTime > ho.StartTime;
        }

        static Vector2 GetExitFieldPosition(HitObjectData ho)
        {
            // Reconstructor GetExitFieldPosition — 홀수 segment→tail, 짝수→head.
            // HOM LoadBeatmap 이후 BaseEndPosition = SliderOsu.HitBurstEndPosition
            // (repeat + stack 반영된 실제 exit). CurvePoints는 컨트롤 포인트라 쓰면 안 됨.
            if (!IsSlider(ho))
                return ho.Position;

            int segments = Math.Max(1, ho.RepeatCount);
            if (segments % 2 == 0)
                return ho.Position;

            if (ho.BaseEndPosition.X != 0f || ho.BaseEndPosition.Y != 0f)
                return ho.BaseEndPosition;

            if (ho.CurvePoints == null || ho.CurvePoints.Count == 0)
                return ho.Position;

            Vector2 tail = ho.CurvePoints[ho.CurvePoints.Count - 1];
            Vector2 stackDelta = ho.Position - ho.BasePosition;
            return tail + stackDelta;
        }

        static void BuildWaypoints()
        {
            if (_objects == null || _objects.Count == 0)
            {
                _wpN = 0;
                return;
            }

            int count = _objects.Count;
            var xs = new List<float>(count + 4);
            var ys = new List<float>(count + 4);
            var ts = new List<int>(count + 4);
            var gaps = new List<bool>(count + 4);

            for (int i = 0; i < count; i++)
            {
                HitObjectData ho = _objects[i];
                Vector2 head = ho.Position;
                bool nextIsSpinner = (i + 1 < count) && IsSpinner(_objects[i + 1]);

                if (IsBodyObject(ho))
                {
                    xs.Add(head.X); ys.Add(head.Y); ts.Add(ho.StartTime); gaps.Add(false);
                    Vector2 exit = GetExitFieldPosition(ho);
                    xs.Add(exit.X); ys.Add(exit.Y); ts.Add(ho.EndTime); gaps.Add(!nextIsSpinner);
                }
                else
                {
                    xs.Add(head.X); ys.Add(head.Y); ts.Add(ho.StartTime); gaps.Add(!nextIsSpinner);
                }
            }

            _wpX = xs.ToArray();
            _wpY = ys.ToArray();
            _wpT = ts.ToArray();
            _wpGap = gaps.ToArray();
            _wpN = xs.Count;
        }

        static Vector2 ScreenWp(int k)
        {
            if (_gameField == null)
                return new Vector2(_wpX[k], _wpY[k]);
            Vector2 d = _gameField.FieldToDisplay(new Vector2(_wpX[k], _wpY[k]));
            return new Vector2(_originX + d.X, _originY + d.Y);
        }

        public static Vector2 GetOffset(Vector2 cursorPosition)
        {
            if (_wpN <= 0) return Vector2.Zero;

            int time = _audioTime;

            if (lastAudioTime != -1 && time < lastAudioTime)
            {
                // retry — waypoints keep, reset motion / segment
                lastAudioTime = -1;
                seg = 0;
                _offset = Vector2.Zero;
                _offVelX = 0f;
                _offVelY = 0f;
                _moveHead = 0;
                Array.Clear(_moveTicks, 0, _moveTicks.Length);
                _lastTicks = _clock.ElapsedTicks;
            }
            lastAudioTime = time;

            long now = _clock.ElapsedTicks;
            float dt = (float)((now - _lastTicks) / (double)Stopwatch.Frequency);
            _lastTicks = now;
            dt = Clamp(dt, 0.001f, 0.05f);

            float gateScale = ComputeIdleGate(cursorPosition, now);

            Vector2 rawTarget = ComputeTargetOffset(cursorPosition, time);
            Vector2 target = rawTarget * gateScale;

            // Attack만 SmoothDamp. Release는 즉시 target으로 — 화면 snap-back은
            // MouseAimAssist가 offset 감소분을 bake 해서 막는다.
            float attackSt = Clamp(AimAssistSettings.AttackInertia, 1f, 500f) / 1000f;
            if (Math.Abs(target.X) >= Math.Abs(_offset.X))
                _offset.X = SmoothDamp(_offset.X, target.X, ref _offVelX, attackSt, dt);
            else
            {
                _offset.X = target.X;
                _offVelX = 0f;
            }
            if (Math.Abs(target.Y) >= Math.Abs(_offset.Y))
                _offset.Y = SmoothDamp(_offset.Y, target.Y, ref _offVelY, attackSt, dt);
            else
            {
                _offset.Y = target.Y;
                _offVelY = 0f;
            }

            float maxOff = Clamp(AimAssistSettings.MaxOffset, 0f, 1000f);
            float m = _offset.Length;
            if (m > maxOff && m > 0.0001f)
                _offset *= maxOff / m;

            return _offset;
        }

        static Vector2 ComputeTargetOffset(Vector2 cursor, int time)
        {
            float strength = Clamp(AimAssistSettings.Strength, 0f, 10f);
            if (strength <= 0f) return Vector2.Zero;

            Vector2 guide = GuideAt(time);
            if (_assistOff) return Vector2.Zero;

            Vector2 toGuide = guide - cursor;
            float dist = toGuide.Length;

            float ratio = _gameField != null ? _gameField.Ratio : 1f;
            float hitR = Math.Max(hitObjectRadius * ratio, 1f);
            float maxR = hitR * Clamp(AimAssistSettings.Range, 1f, 20f);
            if (dist > maxR || dist < 0.0001f) return Vector2.Zero;

            float deadR = hitR * Clamp(AimAssistSettings.DeadZone, 0f, 1f);
            bool inDeadZone = IsCursorNearClosestCircle(cursor, time, deadR);

            float kDist;
            if (inDeadZone)
            {
                kDist = 0f;
            }
            else
            {
                float t = Clamp((dist - deadR) / Math.Max(hitR - deadR, 1f), 0f, 1f);
                kDist = t * (1f - Clamp((dist - hitR) / Math.Max(maxR - hitR, 1f), 0f, 1f));
            }
            float kMax = Clamp(1f - (float)Math.Exp(-strength * 0.45f), 0f, 0.95f);
            float k = kMax * kDist;

            return toGuide * k;
        }

        static bool IsCursorNearClosestCircle(Vector2 cursor, int time, float deadR)
        {
            if (deadR <= 0f || _objects == null) return false;
            int idx = GetUpcomingNonSpinnerIndex(time);
            if (idx < 0) return false;
            HitObjectData ho = _objects[idx];
            Vector2 circleScreen = ScreenField(ho.Position);
            return (cursor - circleScreen).Length <= deadR;
        }

        static Vector2 ScreenField(Vector2 field)
        {
            if (_gameField == null) return field;
            Vector2 d = _gameField.FieldToDisplay(field);
            return new Vector2(_originX + d.X, _originY + d.Y);
        }

        static int GetUpcomingNonSpinnerIndex(int dueTime)
        {
            if (_objects == null) return -1;
            for (int i = 0; i < _objects.Count; i++)
            {
                HitObjectData ho = _objects[i];
                if (IsSpinner(ho)) continue;
                if (ho.StartTime > dueTime) return i;
            }
            return -1;
        }

        static float ComputeIdleGate(Vector2 cursorPosition, long nowTicks)
        {
            float windowMs = Clamp(AimAssistSettings.IdleGateWindow, 0f, 500f);
            float threshold = Clamp(AimAssistSettings.IdleThreshold, 0f, 50f);
            if (windowMs <= 0f || threshold <= 0f)
            {
                RecordMove(cursorPosition, nowTicks);
                return 1f;
            }

            RecordMove(cursorPosition, nowTicks);

            long windowTicks = (long)(windowMs * Stopwatch.Frequency / 1000.0);
            long cutoff = nowTicks - windowTicks;

            float oldestX = cursorPosition.X, oldestY = cursorPosition.Y;
            bool found = false;
            for (int i = 1; i < MoveHistoryMax; i++)
            {
                int idx = (_moveHead - 1 - i + MoveHistoryMax) % MoveHistoryMax;
                if (_moveTicks[idx] == 0) break;
                if (_moveTicks[idx] < cutoff) break;
                oldestX = _movePosX[idx];
                oldestY = _movePosY[idx];
                found = true;
            }

            if (!found) return 1f;

            float dx = cursorPosition.X - oldestX;
            float dy = cursorPosition.Y - oldestY;
            float travel = (float)Math.Sqrt(dx * dx + dy * dy);
            return travel >= threshold ? 1f : 0f;
        }

        static void RecordMove(Vector2 pos, long ticks)
        {
            int i = _moveHead;
            _moveTicks[i] = ticks;
            _movePosX[i] = pos.X;
            _movePosY[i] = pos.Y;
            _moveHead = (i + 1) % MoveHistoryMax;
        }

        static Vector2 GuideAt(int time)
        {
            _assistOff = false;

            if (time < _wpT[0]) { _assistOff = true; return Vector2.Zero; }
            if (_wpN == 1) return ScreenWp(0);
            if (time >= _wpT[_wpN - 1]) { _assistOff = true; return Vector2.Zero; }

            while (seg + 1 < _wpN && time >= _wpT[seg + 1])
                seg++;
            if (seg > _wpN - 2) seg = _wpN - 2;
            if (seg < 0) seg = 0;

            if (!_wpGap[seg]) { _assistOff = true; return Vector2.Zero; }

            int t0 = _wpT[seg];
            int t1 = _wpT[seg + 1];
            float u = (t1 > t0) ? (float)(time - t0) / (t1 - t0) : 0f;
            u = Clamp(u, 0f, 1f);

            Vector2 p1 = ScreenWp(seg);
            Vector2 p2 = ScreenWp(seg + 1);
            Vector2 p0 = ScreenWp(Math.Max(seg - 1, 0));
            Vector2 p3 = ScreenWp(Math.Min(seg + 2, _wpN - 1));

            Vector2 lin = Vector2.Lerp(p1, p2, u);
            float u2 = u * u, u3 = u2 * u;
            Vector2 cr = 0.5f * ((2f * p1)
                + (-p0 + p2) * u
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);

            float curv = Clamp(AimAssistSettings.Curviness, 0f, 1f);
            return lin + (cr - lin) * curv;
        }

        static float SmoothDamp(float current, float target, ref float vel, float smoothTime, float dt)
        {
            smoothTime = Math.Max(smoothTime, 1e-4f);
            float omega = 2f / smoothTime;
            float x = omega * dt;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float temp = (vel + omega * change) * dt;
            vel = (vel - omega * temp) * exp;
            return target + (change + temp) * exp;
        }
    }
}
