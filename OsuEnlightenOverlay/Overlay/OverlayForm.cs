using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics;
using OsuEnlightenOverlay.Memory;
using OsuEnlightenOverlay.Rendering;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;
using OsuEnlightenOverlay.Gameplay.HitObjects;
using OsuEnlightenOverlay.Gameplay.Scoring;
using OsuEnlightenOverlay.Gameplay.Cursor;
using OsuEnlightenOverlay.Gameplay;
using OsuEnlightenOverlay.Skinning;
using OsuEnlightenOverlay.ControlPanel;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// osu! 위에 떠 있는 불투명 검정 오버레이 창.
    /// - OpenTK GLControl (OpenGL 렌더링 컨텍스트)
    /// - WS_EX_TOPMOST | NOACTIVATE | TOOLWINDOW | TRANSPARENT | LAYERED
    /// - WDA_EXCLUDEFROMCAPTURE (캡처 차단)
    /// - WS_EX_TRANSPARENT (클릭 투과)
    /// - osu! 창 크기/위치 추종
    /// - 전용 렌더 스레드 (Application.Idle 메시지 루프 간섭 방지)
    /// </summary>
    public class OverlayForm : Form
    {
        GLControl glControl;
        IntPtr osuHwnd = IntPtr.Zero;
        Timer syncTimer;
        System.Diagnostics.Stopwatch renderStopwatch;
        double fpsCapInterval = 0; // 0 = unlimited
        OsuMemoryReader reader;
        OsuGlRenderer renderer;
        ControlPanel.OverlaySettings settings;

        // 맵 파싱 상태
        BeatmapData currentBeatmap;
        DifficultyValues currentDifficulty;
        HitObjectManagerOsu hom;
        HitBurst hitBurst;
        CursorRenderer cursorRenderer;
        FontRenderer fontRenderer;
        HudRenderer hudRenderer;
        List<OsuMemoryReader.HitObjectJudgement> pendingJudgements;
        string lastBeatmapPath;
        string lastBeatmapFolder;
        bool beatmapParsing = false; // 비동기 파싱 중 플래그
        string lastBeatmapFilename;

        // Difficulty Changer — mod 변경 감지용
        uint lastMods = 0xFFFFFFFF; // 초기값: 절대 매치 안하는 값

        // 창 크기 변경 감지용 — Resize/LoadBeatmap은 크기 변경 시에만
        int lastWindowW = 0, lastWindowH = 0;

        // HR 상태 추적 — HR 변경 시 맵 재파싱 (Y-flip)
        bool lastHR = false;

        // HUD edit mode 입력 처리 (드래그 + 단축키) — OnLoad에서 생성
        HudEditController hudEditController;

        // Difficulty Changer 수학 (mod + 오버라이드 → DifficultyValues) — 생성자에서 생성
        DifficultyController difficultyController;

        // 지연된 스킨/커서 재로드 — ControlPanelForm 스레드에서 직접 OpenGL 작업 불가
        // OnSyncTick(Render 스레드)에서 처리
        string pendingSkinReload = null;
        bool pendingCursorReload = false;

        // 비트맵 파싱 결과 — Task.Run에서 설정, 렌더 스레드에서 적용
        volatile bool pendingBeatmapApply = false;
        BeatmapData pendingBeatmapData;
        DifficultyValues pendingDifficultyData;

        public void ApplyFpsCap()
        {
            if (settings == null || settings.FpsCap <= 0)
            {
                fpsCapInterval = 0; // unlimited
                if (syncTimer != null) syncTimer.Interval = 1; // 1ms = 최대한 빠르게
            }
            else
            {
                fpsCapInterval = 1000.0 / settings.FpsCap;
                if (syncTimer != null)
                {
                    // Timer Interval을 FPS cap에 맞춤 — 최소 1ms
                    int interval = Math.Max(1, (int)Math.Round(fpsCapInterval));
                    syncTimer.Interval = interval;
                }
            }
        }

        /// <summary>
        /// 현재 맵 + mod + 설정으로 최종 난이도 계산 (DifficultyController 위임).
        /// 게임필드 크기는 리사이즈로 바뀌므로 호출 시점 값을 넘긴다.
        /// </summary>
        DifficultyValues ComputeEffectiveDifficulty()
        {
            if (difficultyController == null || currentBeatmap == null || renderer == null)
                return null;
            return difficultyController.Compute(currentBeatmap,
                renderer.GameField.Width, renderer.GameField.Ratio);
        }

        /// <summary>
        /// mod 변경 시 난이도 재계산.
        /// OnSyncTick에서 매 프레임 호출.
        /// HR 변경 시 맵 재파싱 (Y-flip), 그 외 mod 변경 시 UpdateDifficulty만.
        /// </summary>
        void CheckDifficultyUpdate()
        {
            if (currentBeatmap == null || settings == null || reader == null)
                return;

            uint currentMods = reader.MenuMods;
            bool currentHR = reader.IsHR;

            // HR 상태가 변경되면 맵 재파싱 (Y-flip 적용)
            if (currentHR != lastHR)
            {
                lastHR = currentHR;
                lastMods = currentMods;
                // 맵 재파싱 — HR flip 적용
                if (lastBeatmapPath != null)
                {
                    bool verticalFlip = currentHR;
                    currentBeatmap = BeatmapParser.Parse(lastBeatmapPath, verticalFlip);
                    currentDifficulty = ComputeEffectiveDifficulty();
                    if (currentDifficulty == null)
                        currentDifficulty = DifficultyCalculator.Calculate(currentBeatmap,
                            renderer.GameField.Width, renderer.GameField.Ratio);
                    if (hom != null)
                        hom.LoadBeatmap(currentBeatmap, currentDifficulty);
                }
                return;
            }

            if (currentMods == lastMods)
                return; // mod 변경 없음

            lastMods = currentMods;

            // 난이도 재계산
            DifficultyValues newDiff = ComputeEffectiveDifficulty();
            if (newDiff != null)
            {
                currentDifficulty = newDiff;
                if (hom != null)
                    hom.UpdateDifficulty(currentDifficulty);
            }
        }

        /// <summary>
        /// Difficulty Changer 설정 변경 시 즉시 재계산.
        /// ControlPanelForm에서 값 변경 시 호출.
        /// UpdateDifficulty로 Transformation만 재구성 (LoadBeatmap 전체 재생성 없음).
        /// </summary>
        public void RefreshDifficulty()
        {
            if (currentBeatmap == null || settings == null || reader == null)
                return;

            lastMods = 0xFFFFFFFF; // 강제 재계산
            DifficultyValues newDiff = ComputeEffectiveDifficulty();
            if (newDiff != null)
            {
                currentDifficulty = newDiff;
                if (hom != null)
                    hom.UpdateDifficulty(currentDifficulty);
            }
        }

        /// <summary>
        /// 현재 맵의 파싱된 AR 값 반환 (mod 미적용).
        /// ControlPanel Auto 버튼에서 사용.
        /// </summary>
        public float GetMapAR()
        {
            return difficultyController.GetMapAR(currentBeatmap);
        }

        /// <summary>
        /// Cursor Pack 변경 시 커서 텍스처 재로드.
        /// ControlPanelForm에서 호출.
        /// </summary>
        public void ReloadCursorPack()
        {
            if (cursorRenderer != null && settings != null)
            {
                // OnSyncTick에서 처리 — ControlPanelForm 스레드에서 OpenGL 작업 불가
                pendingCursorReload = true;
            }
        }

        /// <summary>
        /// osu! 설치 경로 반환 — ControlPanelForm 스킨 목록 스캔용.
        /// </summary>
        public string GetOsuInstallDir()
        {
            if (reader != null)
                return reader.OsuInstallDir;
            return null;
        }

        /// <summary>
        /// 스킨 변경 시 스킨 재로드.
        /// SkinManager.LoadSkin + TextureManager 캐시 클리어 + 맵 재로드 + 커서 재로드.
        /// </summary>
        public void ReloadSkin(string skinName)
        {
            if (renderer == null || reader == null) return;
            // OnSyncTick에서 처리 — ControlPanelForm 스레드에서 OpenGL 작업 불가
            pendingSkinReload = skinName;
        }

        /// <summary>
        /// OnSyncTick에서 호출 — pendingSkinReload/pendingCursorReload 처리.
        /// Render 스레드에서 OpenGL 컨텍스트가 바인딩되어 있으므로 안전.
        /// </summary>
        void ProcessPendingReloads()
        {
            // 스킨 재로드
            if (pendingSkinReload != null)
            {
                string skinName = pendingSkinReload;
                pendingSkinReload = null;

                // 렌더 스레드에서 이미 GL 컨텍스트 current — MakeCurrent 불필요

                string osuDir = reader.OsuInstallDir;
                string skinsFolder = System.IO.Path.Combine(osuDir, "Skins");

                // 스킨 로드
                SkinManager.LoadSkin(skinName, skinsFolder);

                // 사용자 스킨 폴더 설정
                if (skinName == "Default")
                    renderer.TextureManager.SetUserSkin(null);
                else
                    renderer.TextureManager.SetUserSkin(System.IO.Path.Combine(skinsFolder, skinName));

                // 텍스처 캐시 클리어
                renderer.TextureManager.ClearCache();

                // 맵 재로드 (새 스킨 텍스처로 HitObject 재생성)
                if (currentBeatmap != null && currentDifficulty != null)
                {
                    currentDifficulty = ComputeEffectiveDifficulty();
                    if (currentDifficulty == null)
                        currentDifficulty = DifficultyCalculator.Calculate(currentBeatmap,
                            renderer.GameField.Width, renderer.GameField.Ratio);
                    if (hom != null)
                        hom.LoadBeatmap(currentBeatmap, currentDifficulty);
                }

                // 커서 재로드
                if (cursorRenderer != null)
                {
                    cursorRenderer.SetCursorPack(settings.CursorPackEnabled, settings.CursorPackName);
                    cursorRenderer.Reload();
                }
            }

            // 커서 팩 재로드
            if (pendingCursorReload)
            {
                pendingCursorReload = false;

                // 렌더 스레드에서 이미 GL 컨텍스트 current — MakeCurrent 불필요

                if (cursorRenderer != null)
                {
                    cursorRenderer.SetCursorPack(settings.CursorPackEnabled, settings.CursorPackName);
                    cursorRenderer.Reload();
                }
            }
        }

        /// <summary>
        /// Auto 모드에서 실제 적용되는 CS (HR/EZ 반영) 반환.
        /// ControlPanel CS Auto 버튼 채움값.
        /// </summary>
        public float GetAutoCS()
        {
            return difficultyController.GetAutoCS(currentBeatmap);
        }

        /// <summary>
        /// DT 적용 시 표시 AR 반환 — PreEmpt 기반 역산.
        /// </summary>
        public float GetMapDtAR()
        {
            return difficultyController.GetMapDtAR(currentBeatmap);
        }

        /// <summary>
        /// HT 적용 시 표시 AR 반환 — PreEmpt 기반 역산.
        /// </summary>
        public float GetMapHtAR()
        {
            return difficultyController.GetMapHtAR(currentBeatmap);
        }

        // ── ControlPanelForm 상태 동기화용 속성 ──

        /// <summary>
        /// 현재 오버레이 상태 텍스트 — ControlPanelForm lblStatus에 표시.
        /// </summary>
        public string StatusText
        {
            get
            {
                if (reader == null) return "● Disconnected";
                switch (reader.Mode)
                {
                    case Offsets.Mode_Menu:        return "○ Menu";
                    case Offsets.Mode_Edit:        return "● Edit";
                    case Offsets.Mode_Play:        return "● Playing";
                    case Offsets.Mode_Exit:        return "○ Exit";
                    case Offsets.Mode_SelectEdit:  return "○ Select (Edit)";
                    case Offsets.Mode_SelectPlay:  return "○ Song Select";
                    case Offsets.Mode_Rank:        return "○ Ranking";
                    default:                       return "○ Unknown";
                }
            }
        }

        /// <summary>
        /// 현재 비트맵 표시용 텍스트 — ControlPanelForm lblBeatmap에 표시.
        /// </summary>
        public string BeatmapText
        {
            get
            {
                if (reader == null) return "";
                string folder = reader.BeatmapFolder ?? "";
                string diff = reader.BeatmapDifficultyName ?? "";
                if (string.IsNullOrEmpty(folder) && string.IsNullOrEmpty(diff))
                    return "(no map)";
                if (string.IsNullOrEmpty(diff))
                    return folder;
                return folder + " [" + diff + "]";
            }
        }

        /// <summary>
        /// 현재 mod가 적용된 effective CS — ControlPanelForm CS override 기준값.
        /// HR/EZ mod가 반영된 CS.
        /// </summary>
        public float LiveCS
        {
            get { return difficultyController.GetLiveCS(currentBeatmap); }
        }

        public OverlayForm(OsuMemoryReader reader, ControlPanel.OverlaySettings settings)
        {
            this.reader = reader;
            this.settings = settings;
            this.difficultyController = new DifficultyController(settings, reader);

            // Form 기본 설정
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;

            // GLControl 생성
            GraphicsMode mode = new GraphicsMode(32, 24, 0, 4); // alpha=8
            glControl = new GLControl(mode, 2, 0, GraphicsContextFlags.Default);
            glControl.Dock = DockStyle.Fill;
            glControl.BackColor = Color.Black;
            Controls.Add(glControl);

            // GLControl 자식 창에 클릭 투과 훅 설치 — LAYERED 없이 WM_NCHITTEST 처리
            glControl.HandleCreated += delegate
            {
                glControlHook = new ClickThroughHook();
                glControlHook.AssignHandle(glControl.Handle);
            };

            // 타이머: osu! 창 추종 + 렌더링
            syncTimer = new Timer();
            ApplyFpsCap();
            syncTimer.Tick += OnSyncTick;
        }

        protected override void OnLoad(EventArgs e)
        {
            // 렌더링 엔진 초기화 (base.OnLoad 이전에 수행)
            Console.Write("[Load] GLControl MakeCurrent... ");
            glControl.MakeCurrent();
            Console.WriteLine("OK");

            // default skin은 임베디드 리소스에서 로드 (defaultSkinFolder=null)
            string defaultSkin = null;
            Console.Write("[Load] OsuGlRenderer Initialize... ");
            renderer = new OsuGlRenderer(defaultSkin);
            renderer.Initialize(ClientSize.Width, ClientSize.Height);
            Console.WriteLine("OK (w=" + ClientSize.Width + " h=" + ClientSize.Height + ")");

            // 폰트 렌더러 + HUD 렌더러 초기화
            fontRenderer = new FontRenderer(renderer.TextureManager);
            hudRenderer = new HudRenderer(fontRenderer, renderer.SpriteManager, renderer.TextureManager);
            hudRenderer.SetSettings(settings);
            hudRenderer.SetReader(reader);
            hudEditController = new HudEditController(settings, hudRenderer);
            hudRenderer.SetEditState(hudEditController.State);

            // 스킨 매니저 초기화 — 설정된 스킨 로드 (기본값: Default)
            string skinName = settings != null ? settings.SkinName : "Default";
            string osuDir = reader != null ? reader.OsuInstallDir : null;
            string skinsFolder = osuDir != null ? System.IO.Path.Combine(osuDir, "Skins") : defaultSkin;
            SkinManager.LoadSkin(skinName, skinsFolder);
            if (skinName != "Default" && osuDir != null)
                renderer.TextureManager.SetUserSkin(System.IO.Path.Combine(skinsFolder, skinName));
            Console.WriteLine("[Load] SkinManager initialized (skin=" + skinName + ")");

            // 텍스처 미리 로드
            renderer.TextureManager.Load("hitcircle");
            renderer.TextureManager.Load("hitcircleoverlay");
            renderer.TextureManager.Load("approachcircle");

            base.OnLoad(e); // 이후 Load 이벤트 핸들러(StartOverlay) 실행
        }

        /// <summary>
        /// 현재 플레이 중인 맵 파일 파싱.
        /// 맵 변경 시(folder+filename 변경) 자동 재파싱.
        /// </summary>
        void TryParseCurrentBeatmap()
        {
            // 맵 경로 확인
            string beatmapPath = reader.CurrentBeatmapPath;

            if (beatmapPath == null)
            {
                return;
            }

            // 맵 변경 감지 (folder+filename)
            if (reader.BeatmapFolder == lastBeatmapFolder && reader.BeatmapOsuFilename == lastBeatmapFilename)
                return; // 같은 맵 — 재파싱 불필요

            // 이미 파싱 중이면 대기
            if (beatmapParsing)
                return;

            lastBeatmapFolder = reader.BeatmapFolder;
            lastBeatmapFilename = reader.BeatmapOsuFilename;
            lastBeatmapPath = beatmapPath;

            // 비동기 파싱
            beatmapParsing = true;
            string path = beatmapPath;
            bool verticalFlip = reader.IsHR;
            lastHR = verticalFlip;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var parsed = BeatmapParser.Parse(path, verticalFlip);
                    var diff = DifficultyCalculator.Calculate(parsed, renderer.GameField.Width, renderer.GameField.Ratio);

                    // 렌더 스레드에서 적용 — Invoke 대신 pendingBeatmap 데이터 전달
                    // (렌더 스레드가 GL 컨텍스트를 소유하므로 Invoke로 UI 스레드에서 GL 작업 불가)
                    pendingBeatmapData = parsed;
                    pendingDifficultyData = diff;
                    pendingBeatmapApply = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Beatmap] FAIL: " + ex.Message);
                    pendingBeatmapApply = false;
                    pendingBeatmapData = null;
                }
                finally
                {
                    beatmapParsing = false;
                }
            });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // 확장 스타일 설정
            int exStyle = WindowInterop.GetWindowLong(Handle, WindowInterop.GWL_EXSTYLE);
            WindowInterop.SetWindowLong(Handle, WindowInterop.GWL_EXSTYLE,
                WindowInterop.OverlayExStyle);

            // 불투명 레이어드 창
            WindowInterop.SetLayeredWindowAttributes(Handle, 0, 255, WindowInterop.LWA_ALPHA);

            // DWM: 불투명 배경이므로 블러 비하인드 비활성화, 프레임 확장 안 함
            WindowInterop.MARGINS margins = new WindowInterop.MARGINS();
            margins.Left = 0; margins.Right = 0; margins.Top = 0; margins.Bottom = 0;
            bool compEnabled;
            WindowInterop.DwmIsCompositionEnabled(out compEnabled);
            if (compEnabled)
            {
                WindowInterop.DwmExtendFrameIntoClientArea(Handle, ref margins);
            }

            // 스타일 변경 적용
            WindowInterop.SetWindowPos(Handle, WindowInterop.HWND_TOPMOST, 0, 0, 0, 0,
                WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE |
                WindowInterop.SWP_NOACTIVATE | WindowInterop.SWP_NOOWNERZORDER |
                WindowInterop.SWP_FRAMECHANGED);

            // 캡처 차단 (WDA_EXCLUDEFROMCAPTURE 전용, 폴백 없음)
            if (settings == null || settings.CaptureBlocked)
                CaptureBlock.Enable(Handle);
            else
                CaptureBlock.Disable(Handle);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 확장 스타일을 CreateParams에서 미리 설정
                cp.ExStyle = WindowInterop.OverlayExStyle;
                return cp;
            }
        }

        /// <summary>
        /// 오버레이 시작 — osu! 창 찾기, 동기화 타이머 시작.
        /// </summary>
        public void StartOverlay()
        {
            osuHwnd = WindowInterop.FindOsuWindow(reader.ProcessId);
            if (osuHwnd == IntPtr.Zero)
            {
                Console.WriteLine("[Overlay] osu! 창을 찾을 수 없음 (PID=" + reader.ProcessId + ")");
                return;
            }

            SyncToOsu();
            // 타이머 해상도 1ms로 설정 — 정밀한 FPS 측정/제한용
            WindowInterop.timeBeginPeriod(1);
            renderStopwatch = System.Diagnostics.Stopwatch.StartNew();
            // Application.Idle 기반 렌더링
            Application.Idle += OnApplicationIdle;
        }

        /// <summary>
        /// 오버레이 정지.
        /// </summary>
        public void StopOverlay()
        {
            Application.Idle -= OnApplicationIdle;
            // 타이머 해상도 복원
            WindowInterop.timeEndPeriod(1);
        }

        void OnApplicationIdle(object sender, EventArgs e)
        {
            while (NativeMethods.AppStillIdle())
            {
                // FPS cap
                if (fpsCapInterval > 0 && renderStopwatch != null)
                {
                    double elapsedMs = renderStopwatch.Elapsed.TotalMilliseconds;
                    if (elapsedMs < fpsCapInterval)
                    {
                        int waitMs = (int)(fpsCapInterval - elapsedMs);
                        if (waitMs > 0) System.Threading.Thread.Sleep(waitMs);
                        continue;
                    }
                    renderStopwatch.Restart();
                }

                try { OnSyncTick(null, EventArgs.Empty); }
                catch (Exception ex) { Console.WriteLine("[Idle] OnSyncTick exception: " + ex.Message + "\n" + ex.StackTrace); }
            }
        }

        void OnSyncTick(object sender, EventArgs e)
        {
            // edit 모드 → false 전환 감지 (하단에서 자동 저장에 사용)
            bool wasEditing = settings != null && settings.HudEditMode;

            // osu! 창 위치/크기 추종 — 10프레임에 한 번
            syncCounter++;
            if (syncCounter >= 10)
            {
                SyncToOsu();
                syncCounter = 0;
            }

            // 메모리 읽기
            reader.RefreshLiveValues();

            // Hidden override — HiddenOverride 설정이 켜져 있을 때만 HD 렌더링
            // (실제 osu!의 HD mod와 무관하게 컨트롤 패널 설정으로만 결정)
            bool newHiddenActive = (settings != null && settings.HiddenOverride);
            if (newHiddenActive != HitCircleOsu.HiddenActive)
            {
                HitCircleOsu.HiddenActive = newHiddenActive;
                // HD override 변경 시 맵 재로드 — approach circle 등 HD 관련 스프라이트 재생성
                if (currentBeatmap != null && currentDifficulty != null && hom != null)
                {
                    currentDifficulty = ComputeEffectiveDifficulty();
                    if (currentDifficulty == null)
                        currentDifficulty = DifficultyCalculator.Calculate(currentBeatmap,
                            renderer.GameField.Width, renderer.GameField.Ratio);
                    hom.LoadBeatmap(currentBeatmap, currentDifficulty);
                }
            }

            // HitObject 판정 데이터 읽기 — 3프레임에 한 번 (ReadProcessMemory 빈도 감소)
            hojCounter++;
            if (hojCounter >= 3 || pendingJudgements == null)
            {
                pendingJudgements = null;
                if (reader.Mode == Offsets.Mode_Play)
                {
                    pendingJudgements = reader.ReadHitObjectJudgements(500, 2000);
                    if (renderer != null)
                        renderer.PendingJudgements = pendingJudgements;
                }
                hojCounter = 0;
            }

            // 현재 맵 파싱 — 곡 선택창(SelectPlay)에서 파싱 → Play 진입 시 같은 맵이므로 자동 스킵
            // Retry 감지 — 시간이 크게 역행하면 맵 재로드 강제
            if (reader.Mode == Offsets.Mode_SelectPlay || reader.Mode == Offsets.Mode_Play)
            {
                if (lastFrameTime > 0 && reader.TimeMs < lastFrameTime - 2000 && currentBeatmap != null)
                {
                    // Retry — 맵 재로드 강제 (HitObject 상태 리셋)
                    Console.WriteLine("[Retry] time regression: " + lastFrameTime + " -> " + reader.TimeMs);
                    lastBeatmapFolder = null;
                    lastBeatmapFilename = null;
                    if (hitBurst != null) hitBurst.ResetForResume();
                    // hoCache 무효화 — retry 후 stale 포인터 방지
                    reader.InvalidateHoCache();
                }
                lastFrameTime = reader.TimeMs;
                TryParseCurrentBeatmap();
            }

            // 비트맵 파싱 결과 적용 — 렌더 스레드에서 (GL 컨텍스트 소유)
            if (pendingBeatmapApply)
            {
                pendingBeatmapApply = false;
                if (pendingBeatmapData != null)
                {
                    currentBeatmap = pendingBeatmapData;
                    currentDifficulty = pendingDifficultyData;
                    lastMods = reader.MenuMods;

                    if (hom == null)
                    {
                        hom = new HitObjectManagerOsu(renderer.SpriteManager, renderer.TextureManager, renderer);
                        renderer.HitObjectManager = hom;
                    }
                    hom.LoadBeatmap(currentBeatmap, currentDifficulty);

                    if (hitBurst == null)
                    {
                        hitBurst = new HitBurst(renderer.SpriteManager, renderer.TextureManager);
                        renderer.PostHomUpdateCallback = delegate(int timeMs)
                        {
                            if (pendingJudgements != null && pendingJudgements.Count > 0)
                                hitBurst.Update(pendingJudgements, timeMs);
                            hitBurst.CleanupExpired(timeMs);
                            if (cursorRenderer != null)
                            {
                                cursorRenderer.Update(reader.CursorX, reader.CursorY, timeMs,
                                    reader.BeatmapCS, reader.MenuMods, reader.Mode == Offsets.Mode_Play,
                                    settings.CursorAutoSize, settings.CursorSize);
                                cursorRenderer.Draw();
                            }
                        };
                    }
                    hitBurst.SetBeatmap(currentBeatmap.HitObjects);

                    if (cursorRenderer == null)
                    {
                        cursorRenderer = new CursorRenderer(renderer.SpriteManager, renderer.TextureManager, renderer.GameField);
                        cursorRenderer.SetCursorPack(settings.CursorPackEnabled, settings.CursorPackName);
                        cursorRenderer.Load();
                    }
                    else
                    {
                        cursorRenderer.Reload();
                    }

                    hitBurst.ReAddActive();

                    // HOM 오프셋 자동감지용 .osu StartTime 목록 주입 (HomMonitor 교차검증).
                    // reader 자체 파싱(ParseOsuFile)보다 신뢰성 높음 — OverlayForm이 이미 파싱한 결과 재사용.
                    // 맵이 바뀌면 reader가 감지된 오프셋을 무효화하여 재감지.
                    var starts = new List<int>(currentBeatmap.HitObjects.Count);
                    var types = new List<int>(currentBeatmap.HitObjects.Count);
                    foreach (var h in currentBeatmap.HitObjects)
                    {
                        starts.Add(h.StartTime);
                        types.Add((int)h.Type & 0xF);
                    }
                    reader.SetParsedStartTimes(starts, types, reader.BeatmapFolder + "/" + reader.BeatmapOsuFilename);

                    Console.WriteLine("[Beatmap] OK (" + currentBeatmap.HitObjects.Count + " objects)");
                }
                else
                {
                    currentBeatmap = null;
                }
                pendingBeatmapData = null;
                pendingDifficultyData = null;
            }

            // Difficulty Changer — mod 변경 시 난이도 재계산
            CheckDifficultyUpdate();

            // 지연된 스킨/커서 재로드 처리 (ControlPanelForm에서 요청)
            ProcessPendingReloads();

            // HUD 설정 업데이트 (매 프레임 — ControlPanelForm에서 변경 시 즉시 반영)
            if (hudRenderer != null && settings != null)
            {
                hudRenderer.SetSettings(settings);
            }

            // Play 모드 + 오디오 재생 중 + 설정에서 Enabled일 때만 오버레이 표시
            // edit 모드에서는 씬에 관계없이 항상 표시 (메뉴에서도 편집 가능)
            bool shouldShow = ((reader.Mode == Offsets.Mode_Play &&
                               reader.AudioState == Offsets.AudioState_Playing) ||
                               (settings != null && settings.HudEditMode)) &&
                               (settings == null || settings.Enabled);

            if (shouldShow)
            {
                if (!Visible)
                    ShowOverlay();
            }
            else
            {
                // UpdateHitSeen 호출 안 함 — hitSeen에 등록하면
                // shouldShow=true 후 Update에서 모든 판정이 스킵되어 HitBurst가 생성되지 않음.
                // 대신 Update에서 시간 범위 밖 판정은 자연스럽게 읽히지 않음.

                if (Visible)
                    HideOverlay();
            }

            // 렌더링
            if (Visible)
            {
                Render();
            }

            // CaptureBlock 실시간 토글
            // ── edit 모드 상호작용 동기화 ──
            // edit 모드면 클릭 투과 해제 (UI 조작 가능), 아니면 복원.
            // 부모 창: WS_EX_TRANSPARENT 토글
            // 자식 GLControl: ClickThroughHook.SetClickThrough 토글
            if (settings != null)
            {
                bool wantClickThrough = !settings.HudEditMode;
                // 상태가 변경된 경우에만 Win32 API 호출
                if (wantClickThrough != lastClickThroughState)
                {
                    ApplyClickThrough(Handle, wantClickThrough);
                    if (glControlHook != null)
                        glControlHook.SetClickThrough(wantClickThrough);
                    lastClickThroughState = wantClickThrough;
                }

                // 폴링 기반 드래그/단축키 (자식 창 메시지 라우팅 문제 우회)
                if (settings.HudEditMode && hudEditController != null)
                    hudEditController.Update(Handle, ClientSize);
            }

            // edit → false 전환 시 자동 저장 (NEWNEWOVERLAY main.cpp:205-210)
            if (wasEditing && settings != null && !settings.HudEditMode)
            {
                ControlPanel.SettingsSerializer.Save(settings);
            }
            if (settings != null)
            {
                bool wantBlock = settings.CaptureBlocked;
                // 상태가 변경된 경우에만 CaptureBlock API 호출 (매 프레임 방지)
                if (wantBlock != lastCaptureBlockState)
                {
                    bool isBlocked = CaptureBlock.IsEnabled(Handle);
                    if (wantBlock && !isBlocked)
                        CaptureBlock.Enable(Handle);
                    else if (!wantBlock && isBlocked)
                        CaptureBlock.Disable(Handle);
                    lastCaptureBlockState = wantBlock;
                }
            }

            frameCount++;
        }

        int frameCount = 0;
        int lastFrameTime = -1; // Retry 감지용
        int syncCounter = 0; // SyncToOsu 빈도 제어
        int hojCounter = 0; // ReadHitObjectJudgements 빈도 제어
        bool? lastClickThroughState = null; // ClickThrough 상태 캐싱 (매 프레임 Win32 API 호출 방지)
        bool? lastCaptureBlockState = null; // CaptureBlock 상태 캐싱

        void SyncToOsu()
        {
            if (osuHwnd == IntPtr.Zero) return;

            // 클라이언트 영역(실제 게임 영역) 기준 — 타이틀바/테두리 제외
            WindowInterop.RECT clientRect;
            if (!WindowInterop.GetClientRect(osuHwnd, out clientRect)) return;

            WindowInterop.POINT pt = new WindowInterop.POINT();
            WindowInterop.ClientToScreen(osuHwnd, ref pt);

            int clientX = pt.X;
            int clientY = pt.Y;
            int clientW = clientRect.Width;
            int clientH = clientRect.Height;

            // ── 렌터박싱(네이티브 렌더) 감지 시 게임 필드 영역 계산 ──
            // 렌터박싱 ON: osu! 창은 모니터 전체 해상도로 열리지만,
            //   게임 필드는 그 안에서 더 작은 해상도(WindowManager.Width×Height)로
            //   LetterboxPositionX/Y 비율에 따라 중앙(또는偏移)에 위치.
            //   오버레이는 이 게임 필드 영역에 정확히 맞춰야 함.
            //
            // 렌터박싱 OFF: 오버레이 = 클라이언트 영역 전체.

            int overlayX = clientX;
            int overlayY = clientY;
            int overlayW = clientW;
            int overlayH = clientH;
            int gameFieldW = clientW;  // GameField 계산용 너비
            int gameFieldH = clientH;  // GameField 계산용 높이

            if (reader.IsLetterboxing && reader.WindowWidth > 0 && reader.WindowHeight > 0)
            {
                // 렌터박싱: osu! OsuGlControl.ResetViewport() 공식 정확히 포팅
                //   excess = (DesktopResolution - WindowManager) / 2
                //   offset = excess * (100 ± LetterboxPosition) / 100
                //   Y축은 반전: 100 - LetterboxPositionY (위가 +)
                //   MenuHeight는 렌터박싱 시 Height에서 제외됨
                int menuH = 0; // 오버레이에서는 메뉴바 무시 (클라이언트 영역에 포함 안 됨)

                float excessX = (reader.DesktopWidth - reader.WindowWidth) / 2f;
                float excessY = (reader.DesktopHeight - reader.WindowHeight - menuH) / 2f;

                float offsetX = excessX * (100f + reader.LetterboxPositionX) / 100f;
                // osu! 소스는 OpenGL 좌표계(위가 +)로 100 - Y 이지만,
                // 화면 좌표(아래가 +)로 변환하려면 100 + Y
                float offsetY = excessY * (100f + reader.LetterboxPositionY) / 100f;

                overlayX = clientX + (int)offsetX;
                overlayY = clientY + (int)offsetY;
                // 1픽셀 여유 — osu! ClientSizeAdjustment로 인한 1px 차이 보정
                overlayW = reader.WindowWidth + 1;
                overlayH = reader.WindowHeight + 1;

                // GameField는 실제 렌더링 해상도 기준으로 계산
                gameFieldW = reader.WindowWidth;
                gameFieldH = reader.WindowHeight;
            }
            else
            {
                // 렌터박싱 OFF: GameField = 클라이언트 영역
                // 단, WindowManager 해상도가 클라이언트와 다르면(스케일됨) WM 기준 사용
                if (reader.WindowWidth > 0 && reader.WindowHeight > 0 &&
                    (reader.WindowWidth != clientW || reader.WindowHeight != clientH))
                {
                    gameFieldW = reader.WindowWidth;
                    gameFieldH = reader.WindowHeight;
                }
            }

            // 디버그: osu! 창 크기 (최초 1회)
            if (lastBeatmapFolder == null || lastBeatmapFolder.Length == 0)
            {
                Console.WriteLine($"[Sync] osu! client={clientW}x{clientH}" +
                    $" overlay={overlayW}x{overlayH}" +
                    $" gameField={gameFieldW}x{gameFieldH}" +
                    $" letterbox={reader.IsLetterboxing}");
                lastBeatmapFolder = " ";
            }

            // 오버레이 창을 게임 필드 영역에 정확히 맞춤
            if (Left != overlayX || Top != overlayY || Width != overlayW || Height != overlayH)
            {
                WindowInterop.SetWindowPos(Handle, WindowInterop.HWND_TOPMOST,
                    overlayX, overlayY, overlayW, overlayH,
                    WindowInterop.SWP_NOACTIVATE);
            }

            // 렌더링 엔진 리사이즈 — 게임 필드 크기가 실제로 변경되었을 때만
            if (renderer != null && glControl != null && (gameFieldW != lastWindowW || gameFieldH != lastWindowH))
            {
                lastWindowW = gameFieldW;
                lastWindowH = gameFieldH;
                // 렌더 스레드에서 이미 GL 컨텍스트 current — MakeCurrent 불필요
                renderer.Resize(gameFieldW, gameFieldH);

                // GameField 리사이즈 후 난이도 재계산 — SpriteDisplaySize가 GameField.Width 기준이므로
                if (currentBeatmap != null)
                {
                    DifficultyValues newDiff = ComputeEffectiveDifficulty();
                    if (newDiff != null)
                        currentDifficulty = newDiff;
                    else
                        currentDifficulty = DifficultyCalculator.Calculate(currentBeatmap,
                            renderer.GameField.Width, renderer.GameField.Ratio);
                    // UpdateDifficulty로 Transformation만 재구성 (LoadBeatmap 전체 재생성 없음)
                    if (hom != null)
                        hom.UpdateDifficulty(currentDifficulty);
                }
            }
        }

        void ShowOverlay()
        {
            WindowInterop.ShowWindow(Handle, WindowInterop.SW_SHOWNOACTIVATE);
        }

        void HideOverlay()
        {
            WindowInterop.ShowWindow(Handle, WindowInterop.SW_HIDE);
        }

        void Render()
        {
            // MakeCurrent 생략 — 단일 GL 컨텍스트이므로 매 프레임 호출 불필요
            // (OpenTK GLControl은 동일 스레드에서 이미 current 상태 유지)

            if (renderer != null)
            {
                // HUD 렌더링을 SpriteManager.Draw 전에 수행하도록 PreDrawCallback 설정
                if (hudRenderer != null && renderer.PreDrawCallback == null)
                {
                    renderer.PreDrawCallback = delegate(int timeMs)
                    {
                        hudRenderer.UpdateFrameTime();
                        hudRenderer.SetViewport(ClientSize.Width, ClientSize.Height);
                        hudRenderer.Render();
                    };
                }

                // HitObjectManager Update는 renderer.Render 내부에서 호출됨
                renderer.Render(reader.TimeMs);
            }
            else
            {
                OpenTK.Graphics.OpenGL.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                OpenTK.Graphics.OpenGL.GL.Clear(OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit);
            }

            glControl.SwapBuffers();
        }

        protected override void WndProc(ref Message m)
        {
            // WM_NCHITTEST → 클릭 투과 (edit 모드는 자식 GLControl에서 폴링으로 처리).
            if (m.Msg == WindowInterop.WM_NCHITTEST)
            {
                m.Result = (IntPtr)WindowInterop.HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }

        // GLControl 자식 창 클릭 투과 — NativeWindow 서브클래싱
        // WS_EX_LAYERED 없이 WM_NCHITTEST → HTTRANSPARENT로 클릭 투과 구현
        ClickThroughHook glControlHook;

        /// <summary>
        /// GLControl 자식 창에 WM_NCHITTEST 훅 설치.
        /// LAYERED 없이 클릭 투과 — DWM 합성 스터터링 방지.
        /// </summary>
        class ClickThroughHook : System.Windows.Forms.NativeWindow
        {
            bool clickThrough = true;

            public void SetClickThrough(bool enabled)
            {
                clickThrough = enabled;
            }

            protected override void WndProc(ref Message m)
            {
                // WM_NCHITTEST → HTTRANSPARENT (클릭 투과)
                if (m.Msg == WindowInterop.WM_NCHITTEST && clickThrough)
                {
                    m.Result = (IntPtr)WindowInterop.HTTRANSPARENT;
                    return;
                }

                base.WndProc(ref m);
            }
        }

        // ── click-through 동기화 (지정 창에 WS_EX_TRANSPARENT 추가/제거) ──
        // 부모 창과 자식 GLControl 창 모두에 적용.
        static void ApplyClickThrough(IntPtr hwnd, bool wantClickThrough)
        {
            bool isClickThrough = ClickThrough.IsEnabled(hwnd);
            if (wantClickThrough && !isClickThrough)
                ClickThrough.Enable(hwnd);
            else if (!wantClickThrough && isClickThrough)
                ClickThrough.Disable(hwnd);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 타이머 해상도 복원 (혹시 StopOverlay에서 안 됐을 경우)
                try { WindowInterop.timeEndPeriod(1); } catch { }
                if (renderer != null)
                    renderer.Dispose();
                if (syncTimer != null)
                    syncTimer.Dispose();
                if (glControl != null)
                    glControl.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}