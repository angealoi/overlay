using System;
using System.Runtime.InteropServices;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// "Render at Native Resolution" 관련 값 읽기 — WindowManager + ConfigManager Dictionary.
    ///
    /// 두 출처를 합친다:
    ///   WindowManager 객체 = 실제 렌더링 해상도 (Width/Height/SpriteRes)
    ///   ConfigManager Dictionary = 사용자 설정 (Letterboxing, LetterboxPosition, ...)
    ///   Win32 MonitorFromWindow = 모니터 네이티브 해상도
    ///
    /// osu!에서 해상도 변경은 메뉴/송셀렉트에서만 가능하므로 Play 중에는 Dictionary를
    /// 재스캔하지 않는다. Dictionary rehash로 Bindable 포인터가 낡으면 값이 유효 범위를
    /// 벗어나므로, 그때 포인터를 무효화해 다음 비-Play 프레임에 재스캔한다.
    /// </summary>
    internal class ResolutionReader
    {
        // Win32 API — 모니터 네이티브 해상도 가져오기
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        const uint MONITOR_DEFAULTTONEAREST = 2;

        readonly ProcessMemory pm;

        IntPtr windowManagerSlot = IntPtr.Zero;
        IntPtr configDictSlot = IntPtr.Zero;  // ConfigManager Dictionary 포인터

        // Config Dictionary에서 찾은 Bindable 객체 포인터들 (최초 1회 스캔 후 캐싱)
        IntPtr bindableFullscreen = IntPtr.Zero;
        IntPtr bindableLetterboxing = IntPtr.Zero;
        IntPtr bindableWidth = IntPtr.Zero;
        IntPtr bindableHeight = IntPtr.Zero;
        IntPtr bindableWidthFullscreen = IntPtr.Zero;
        IntPtr bindableHeightFullscreen = IntPtr.Zero;
        IntPtr bindableLetterboxPositionX = IntPtr.Zero;
        IntPtr bindableLetterboxPositionY = IntPtr.Zero;

        /// <summary>실제 렌더링 너비 (WindowManager.Width)</summary>
        public int WindowWidth { get; private set; }
        /// <summary>실제 렌더링 높이 (WindowManager.Height, MenuHeight 제외 전)</summary>
        public int WindowHeight { get; private set; }
        /// <summary>스프라이트 기준 해상도 (보통 768)</summary>
        public int SpriteRes { get; private set; }
        /// <summary>설정된 너비 (ConfigManager.sWidth, 창 모드)</summary>
        public int ConfigWidth { get; private set; }
        /// <summary>풀스크린 여부</summary>
        public bool IsFullscreen { get; private set; }
        /// <summary>레터박싱 여부 (osu! UI: "Render at native resolution")</summary>
        public bool IsLetterboxing { get; private set; }
        /// <summary>레터박스 수평 위치 (-100~100, 0=중앙)</summary>
        public int LetterboxPositionX { get; private set; }
        /// <summary>레터박스 수직 위치 (-100~100, 0=중앙)</summary>
        public int LetterboxPositionY { get; private set; }
        /// <summary>네이티브 해상도로 렌더링 중인지 (WindowManager == InitialDesktopResolution)</summary>
        public bool IsNativeResolution { get; private set; }
        /// <summary>화면 비율 (Height / 480)</summary>
        public float Ratio { get; private set; }
        /// <summary>와이드스크린 오프셋 (NonWidescreenOffsetX)</summary>
        public int NonWidescreenOffsetX { get; private set; }
        /// <summary>GameField ScaleFactor</summary>
        public float GameFieldScale { get; private set; }
        /// <summary>설정된 전체화면 너비 (ConfigManager.sWidthFullscreen)</summary>
        public int ConfigWidthFullscreen { get; private set; }
        /// <summary>설정된 전체화면 높이 (ConfigManager.sHeightFullscreen)</summary>
        public int ConfigHeightFullscreen { get; private set; }
        /// <summary>설정된 창 모드 높이 (ConfigManager.sHeight)</summary>
        public int ConfigHeight { get; private set; }
        /// <summary>모니터 실제 네이티브 해상도 너비 (Win32 API)</summary>
        public int DesktopWidth { get; private set; }
        /// <summary>모니터 실제 네이티브 해상도 높이 (Win32 API)</summary>
        public int DesktopHeight { get; private set; }

        public ResolutionReader(ProcessMemory pm)
        {
            this.pm = pm;
        }

        /// <summary>
        /// Render at Native Resolution 관련 static slot 스캔.
        /// WindowManager + ConfigManager Dictionary (tosu 방식).
        /// </summary>
        public void ScanSlots()
        {
            // WindowManager static slot
            windowManagerSlot = AobScanner.ResolveSlot(pm, Signatures.WindowManager);

            // ConfigManager Dictionary 포인터 (tosu configurationAddr 패턴)
            configDictSlot = AobScanner.ResolveSlot(pm, Signatures.ConfigDictionary);
        }

        /// <summary>
        /// ConfigManager Dictionary에서 키 이름으로 Bindable 객체를 찾아 캐싱.
        /// 값이 무효하면 재스캔 (Dictionary rehash 대응).
        /// tosu의 configOffsets + configValue 방식과 동일.
        /// </summary>
        void ScanConfigDictionary()
        {
            if (configDictSlot == IntPtr.Zero) return;

            // tosu 방식: configurationAddr 패턴의 OperandSkip(+6)은
            //   8B 0D [imm32]  → mov ecx, [static_slot]
            // ResolveSlot은 [match+6]의 4바이트 = static_slot 주소를 반환.
            //   readPointer(static_slot) = Dictionary 객체 포인터.
            IntPtr dictObj;
            if (!pm.ReadPointer(configDictSlot, out dictObj) || dictObj == IntPtr.Zero)
                return;

            // Dictionary.entries = [dictObj + 0x08]
            IntPtr entries;
            if (!pm.ReadPointer(dictObj + Offsets.Dict_Entries, out entries) || entries == IntPtr.Zero)
                return;

            // Dictionary.count = [dictObj + 0x1C]
            // osu! ConfigManager는 ~234개 entry를 가짐 (tosu configList의 ~40개보다 훨씬 많음)
            int count;
            if (!pm.ReadInt32(dictObj + Offsets.Dict_Count, out count) || count <= 0 || count > 500)
                return;

            for (int i = 0; i < count; i++)
            {
                IntPtr entry = entries + 0x08 + Offsets.Dict_EntryStride * i;

                IntPtr keyPtr;
                if (!pm.ReadPointer(entry + Offsets.Dict_EntryKey, out keyPtr) || keyPtr == IntPtr.Zero)
                    continue;

                string key = pm.ReadSharpString(keyPtr);
                if (string.IsNullOrEmpty(key))
                    continue;

                IntPtr bindable;
                if (!pm.ReadPointer(entry + Offsets.Dict_EntryValue, out bindable) || bindable == IntPtr.Zero)
                    continue;

                switch (key)
                {
                    case "Fullscreen":
                        bindableFullscreen = bindable;
                        break;
                    case "Letterboxing":
                        bindableLetterboxing = bindable;
                        break;
                    case "Width":
                        bindableWidth = bindable;
                        break;
                    case "Height":
                        bindableHeight = bindable;
                        break;
                    case "WidthFullscreen":
                        bindableWidthFullscreen = bindable;
                        break;
                    case "HeightFullscreen":
                        bindableHeightFullscreen = bindable;
                        break;
                    case "LetterboxPositionX":
                        bindableLetterboxPositionX = bindable;
                        break;
                    case "LetterboxPositionY":
                        bindableLetterboxPositionY = bindable;
                        break;
                }
            }
        }

        // BindableInt 유효 범위 — 벗어나면 Dictionary rehash 등으로 포인터가 낡은 것으로 간주.
        const double DimensionMin = 1, DimensionMax = 9999;      // 해상도 픽셀
        const double LetterboxMin = -200, LetterboxMax = 200;    // 레터박스 위치 (-100~100 + 여유)

        /// <summary>
        /// BindableInt(+0x04 = double) 읽기.
        /// 값이 [min,max] 밖이거나 NaN/Inf면 bindable 포인터를 무효화해
        /// 다음 스캔에서 재해석하도록 하고 false 반환 (기존 값 유지).
        /// </summary>
        bool TryReadBindableInt(ref IntPtr bindable, double min, double max, out int value)
        {
            value = 0;
            if (bindable == IntPtr.Zero) return false;

            double val;
            if (!pm.ReadDouble(bindable + Offsets.BindableInt_Value, out val))
                return false;

            if (double.IsNaN(val) || double.IsInfinity(val) || val < min || val > max)
            {
                bindable = IntPtr.Zero; // 무효 → 재스캔 트리거
                return false;
            }

            value = (int)val;
            return true;
        }

        /// <summary>
        /// 매 프레임 Resolution 관련 live 값 갱신.
        /// WindowManager.Width/Height + Config Dictionary에서 FS/LB/W/H.
        /// </summary>
        /// <param name="mode">현재 OsuModes — Play 중에는 Dictionary 재스캔을 건너뛴다.</param>
        public void Refresh(int mode)
        {
            // WindowManager 객체에서 Width/Height/SpriteRes 읽기
            if (windowManagerSlot != IntPtr.Zero)
            {
                IntPtr wmObj;
                if (pm.ReadPointer(windowManagerSlot, out wmObj) && wmObj != IntPtr.Zero)
                {
                    int w, h, sr;
                    if (pm.ReadInt32(wmObj + Offsets.WindowManager_Width, out w))
                        WindowWidth = w;
                    if (pm.ReadInt32(wmObj + Offsets.WindowManager_Height, out h))
                        WindowHeight = h;
                    if (pm.ReadInt32(wmObj + Offsets.WindowManager_SpriteRes, out sr))
                        SpriteRes = sr;
                }
            }

            // Config Dictionary 스캔 — Play 모드에서는 해상도 변경 불가하므로 스킵
            // osu!에서 해상도 변경은 메뉴/송셀렉트에서만 가능
            // Mode: 0=Menu, 1=Edit, 2=Play, 5=SelectPlay
            bool needRescan = (mode != Offsets.Mode_Play) &&
                              (bindableFullscreen == IntPtr.Zero ||
                               bindableWidth == IntPtr.Zero ||
                               bindableWidthFullscreen == IntPtr.Zero);
            if (needRescan)
                ScanConfigDictionary();

            // ConfigManager.sFullscreen (BindableBool, +0x0C = byte)
            if (bindableFullscreen != IntPtr.Zero)
            {
                byte val;
                if (pm.ReadByte(bindableFullscreen + Offsets.BindableBool_Value, out val))
                    IsFullscreen = val != 0;
            }

            // ConfigManager.sLetterboxing (BindableBool, +0x0C = byte)
            if (bindableLetterboxing != IntPtr.Zero)
            {
                byte val;
                if (pm.ReadByte(bindableLetterboxing + Offsets.BindableBool_Value, out val))
                    IsLetterboxing = val != 0;
            }

            // ConfigManager 해상도 설정 (BindableInt) — 유효 범위 밖이면 포인터 무효화 → 재스캔
            int v;
            if (TryReadBindableInt(ref bindableWidth, DimensionMin, DimensionMax, out v)) ConfigWidth = v;
            if (TryReadBindableInt(ref bindableHeight, DimensionMin, DimensionMax, out v)) ConfigHeight = v;
            if (TryReadBindableInt(ref bindableWidthFullscreen, DimensionMin, DimensionMax, out v)) ConfigWidthFullscreen = v;
            if (TryReadBindableInt(ref bindableHeightFullscreen, DimensionMin, DimensionMax, out v)) ConfigHeightFullscreen = v;
            if (TryReadBindableInt(ref bindableLetterboxPositionX, LetterboxMin, LetterboxMax, out v)) LetterboxPositionX = v;
            if (TryReadBindableInt(ref bindableLetterboxPositionY, LetterboxMin, LetterboxMax, out v)) LetterboxPositionY = v;

            // 파생 값 계산
            if (WindowHeight > 0)
            {
                Ratio = (float)WindowHeight / Offsets.WindowManager_DefaultHeight;
                NonWidescreenOffsetX = Math.Max(0, (int)((WindowWidth - (WindowHeight * 4f / 3f)) / 2));

                if (SpriteRes > 0)
                    GameFieldScale = (float)WindowHeight / SpriteRes;
            }

            RefreshDesktopResolution();

            // Native resolution 판별:
            // 1. 풀스크린 + 레터박싱 OFF → 전체화면 네이티브 해상도로 렌더링
            // 2. 레터박싱 ON → 항상 모니터 네이티브 해상도로 렌더링 (중앙에 작게 표시)
            // 3. 창 모드 + WM 해상도 == 데스크탑 해상도 → borderless 네이티브
            if (IsFullscreen && !IsLetterboxing)
                IsNativeResolution = true;
            else if (IsLetterboxing)
                IsNativeResolution = true;  // 레터박싱 = 네이티브 해상도로 렌더링
            else if (!IsFullscreen && DesktopWidth > 0 && DesktopHeight > 0)
                IsNativeResolution = (WindowWidth == DesktopWidth && WindowHeight == DesktopHeight);
            else
                IsNativeResolution = false;
        }

        /// <summary>
        /// 모니터 실제 네이티브 해상도 (Win32 API, osu! 창이 있는 모니터).
        /// 최초 1회 또는 창이 없을 때만 갱신.
        /// </summary>
        void RefreshDesktopResolution()
        {
            if (DesktopWidth != 0 || pm.ProcessId <= 0) return;

            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(pm.ProcessId);
                IntPtr hwnd = proc.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    IntPtr hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                    MONITORINFO mi = new MONITORINFO();
                    mi.Size = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        DesktopWidth = mi.Monitor.Right - mi.Monitor.Left;
                        DesktopHeight = mi.Monitor.Bottom - mi.Monitor.Top;
                    }
                }
                proc.Dispose();
            }
            catch { }
        }
    }
}
