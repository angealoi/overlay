using System;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Input;
using OsuEnlightenOverlay.Overlay;
using OsuEnlightenOverlay.Rendering;
using OpenTK;

namespace OsuEnlightenOverlay.Gameplay.AimAssist
{
    /// <summary>
    /// 마우스 aim — AimAssistService(Reconstructor 동일) + lame WH_MOUSE_LL / SendInput.
    /// </summary>
    internal sealed class MouseAimAssist : IDisposable
    {
        readonly object _lock = new object();

        MouseHook _hook;
        volatile bool _inPlay;
        volatile bool _enabled;

        float _lastOffsetX, _lastOffsetY;
        BeatmapData _boundBeatmap;
        bool _initialized;

        public bool Enabled;

        public bool HookInstalled
        {
            get { return _hook != null && _hook.Installed; }
        }

        public void Start()
        {
            if (_hook != null) return;
            _hook = new MouseHook();
            _hook.SetTransform(OnHookMove);
            if (!_hook.Install())
                Console.WriteLine("[AimAssist] WH_MOUSE_LL install failed");
            else
                Console.WriteLine("[AimAssist] WH_MOUSE_LL installed");
        }

        public void Stop()
        {
            OnLeavePlay();
            if (_hook != null)
            {
                _hook.Dispose();
                _hook = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        public void InvalidateMap()
        {
            lock (_lock)
            {
                _initialized = false;
                _boundBeatmap = null;
                AimAssistService.Reset();
                _lastOffsetX = 0f;
                _lastOffsetY = 0f;
            }
        }

        public void OnLeavePlay()
        {
            _inPlay = false;
            InvalidateMap();
            MouseInput.InvalidateVirtualDesktop();
        }

        /// <summary>UI 스레드 — 설정 동기화 + waypoint init + 시간/필드 갱신.</summary>
        public void Update(
            bool playing,
            BeatmapData beatmap,
            DifficultyValues difficulty,
            GameField gameField,
            int gameFieldScreenX,
            int gameFieldScreenY,
            int timeMs)
        {
            _enabled = Enabled;
            if (!Enabled || !playing || beatmap == null || beatmap.HitObjects == null
                || beatmap.HitObjects.Count == 0 || gameField == null || difficulty == null)
            {
                if (_inPlay)
                    OnLeavePlay();
                return;
            }

            lock (_lock)
            {
                bool needInit = !_initialized || !ReferenceEquals(_boundBeatmap, beatmap);
                if (needInit)
                {
                    AimAssistService.Initialize(beatmap, difficulty, gameField,
                        gameFieldScreenX, gameFieldScreenY);
                    _boundBeatmap = beatmap;
                    _initialized = true;
                    _lastOffsetX = 0f;
                    _lastOffsetY = 0f;
                }
                else
                {
                    AimAssistService.UpdatePlayfield(gameField, gameFieldScreenX, gameFieldScreenY);
                }

                AimAssistService.SetAudioTime(timeMs);
                _inPlay = true;
            }
        }

        bool OnHookMove(WindowInterop.POINT pt, out WindowInterop.POINT result)
        {
            result = pt;
            if (!_enabled || !_inPlay)
                return false;

            lock (_lock)
            {
                if (!_initialized)
                    return false;

                // lame unwrap — observed includes last assist offset
                float rawX = pt.X - _lastOffsetX;
                float rawY = pt.Y - _lastOffsetY;

                Vector2 offset = AimAssistService.GetOffset(new Vector2(rawX, rawY));

                float outX = rawX + offset.X;
                float outY = rawY + offset.Y;

                result.X = (int)Math.Round(outX);
                result.Y = (int)Math.Round(outY);

                MouseInput.MoveAbsoluteVirtualDesktop(result.X, result.Y);

                _lastOffsetX = offset.X;
                _lastOffsetY = offset.Y;
                return true;
            }
        }
    }
}
