using System;
using System.Runtime.InteropServices;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// 레터박싱(osu! UI: "Render at native resolution") 관련 값 읽기 — 오버레이를
    /// osu!의 실제 게임 필드 영역에 맞추기 위해 필요한 값만 읽는다.
    ///
    /// osu!의 세 가지 표시 모드:
    ///   LB OFF + Fullscreen OFF → 창모드. 게임 필드 = 클라이언트 영역
    ///   LB OFF + Fullscreen ON  → exclusive fullscreen. 게임 필드 = 클라이언트 영역
    ///   LB ON                   → borderless fullscreen(데스크톱 해상도)으로 열고 그 안에
    ///                             선택 해상도로 렌더, 나머지는 여백. LetterboxPositionX/Y
    ///                             (-100~100, +가 오른쪽/아래)로 그 영역을 움직인다.
    ///                             → 이 경우에만 위치 계산이 필요하다.
    ///
    /// 두 출처를 합친다:
    ///   ConfigManager Dictionary  = Letterboxing, LetterboxPositionX/Y, Fullscreen,
    ///                               Width/Height, WidthFullscreen/HeightFullscreen
    ///   Win32 MonitorFromWindow   = 모니터 네이티브 해상도
    ///
    /// ── WindowManager 객체를 쓰지 않는 이유 ──
    /// 렌더 해상도는 WindowManager 객체에도 있고 그쪽이 더 직접적이다. 하지만 그 static
    /// 슬롯을 찾는 AOB 패턴은 SetScreenSize() 내부 코드이고, 이 메서드는 사용자가 해상도를
    /// "바꿀 때"만 호출된다. 즉 그냥 켜기만 한 osu!에서는 JIT되지 않아 패턴이 메모리에
    /// 아예 존재하지 않는다. 실측: 옵션을 건드리지 않은 10개 세션에서 85/85 스캔 실패 →
    /// WindowWidth=0 → OverlayForm의 letterbox 분기가 통째로 죽어 오버레이가 게임 필드
    /// 대신 화면 전체를 덮었다. 반면 Config Dictionary는 96/96 성공했고, 아래 Fullscreen
    /// 분기와 함께 읽으면 WindowManager와 값이 정확히 일치한다(실측: 창모드 1280x960,
    /// 전체화면 1280x1024 양쪽 다 일치).
    ///
    /// ── Bindable 포인터를 캐싱하지 않는 이유 ──
    /// GC 압축이 Bindable 객체를 옮기면 캐싱한 포인터가 낡는다. 실측: 객체가 통째로 0이
    /// 되면서 LetterboxPositionX/Y가 1로, Letterboxing이 False로 뒤집혔다. 해제된 블록
    /// 헤더를 double로 읽으면 정확히 1.0이 나오는데, 이 값은 정수이고 유효 범위 안이라
    /// 값 기반 검사(범위/정수)를 전부 통과한다 — 값만 보고는 낡음을 막을 수 없다.
    /// 그래서 entry 인덱스만 캐싱하고 값은 매 프레임 static 슬롯부터 다시 타고 내려가
    /// 읽는다. 모든 hop이 GC 루트에서 시작하므로 압축과 구조적으로 무관해진다.
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

        IntPtr configDictSlot = IntPtr.Zero;  // ConfigManager Dictionary static slot

        /// <summary>추적하는 Config 키 하나 — entry 인덱스만 들고 있다(포인터 아님).</summary>
        class ConfigKey
        {
            public readonly string Name;
            public int Index = -1;
            public ConfigKey(string name) { Name = name; }
        }

        readonly ConfigKey keyLetterboxing = new ConfigKey("Letterboxing");
        readonly ConfigKey keyLbPosX       = new ConfigKey("LetterboxPositionX");
        readonly ConfigKey keyLbPosY       = new ConfigKey("LetterboxPositionY");
        readonly ConfigKey keyFullscreen   = new ConfigKey("Fullscreen");
        readonly ConfigKey keyWidth        = new ConfigKey("Width");
        readonly ConfigKey keyHeight       = new ConfigKey("Height");
        readonly ConfigKey keyWidthFs      = new ConfigKey("WidthFullscreen");
        readonly ConfigKey keyHeightFs     = new ConfigKey("HeightFullscreen");

        readonly ConfigKey[] allKeys;

        /// <summary>실제 렌더링 너비 (Fullscreen 여부에 따라 선택된 Config 해상도)</summary>
        public int WindowWidth { get; private set; }
        /// <summary>실제 렌더링 높이</summary>
        public int WindowHeight { get; private set; }
        /// <summary>레터박싱 여부 (osu! UI: "Render at native resolution")</summary>
        public bool IsLetterboxing { get; private set; }
        /// <summary>레터박스 수평 위치 (-100~100, +가 오른쪽)</summary>
        public int LetterboxPositionX { get; private set; }
        /// <summary>레터박스 수직 위치 (-100~100, +가 아래)</summary>
        public int LetterboxPositionY { get; private set; }
        /// <summary>모니터 실제 네이티브 해상도 너비 (Win32 API)</summary>
        public int DesktopWidth { get; private set; }
        /// <summary>모니터 실제 네이티브 해상도 높이 (Win32 API)</summary>
        public int DesktopHeight { get; private set; }

        public ResolutionReader(ProcessMemory pm)
        {
            this.pm = pm;
            allKeys = new[]
            {
                keyLetterboxing, keyLbPosX, keyLbPosY, keyFullscreen,
                keyWidth, keyHeight, keyWidthFs, keyHeightFs
            };
        }

        /// <summary>ConfigManager Dictionary static slot 스캔 (tosu configurationAddr 방식).</summary>
        /// <summary>
        /// 기동 시 배치 스캔(D1) 결과를 받아 slot 해석 — 전체 메모리를 다시 읽지 않는다.
        /// </summary>
        public void ApplyScan(AobScanRequest req)
        {
            configDictSlot = AobScanner.ResolveSlot(pm, Signatures.ConfigDictionary, req);
        }

        // ────────────────────── Dictionary 접근 (캐싱 없음) ──────────────────────

        /// <summary>
        /// Dictionary.entries 배열 주소를 static 루트부터 새로 해석한다.
        /// 매 프레임 호출된다 — 캐싱하지 않는 것이 요점이다(GC 압축 면역).
        /// </summary>
        IntPtr ResolveEntries()
        {
            if (configDictSlot == IntPtr.Zero) return IntPtr.Zero;

            IntPtr dictObj;
            if (!pm.ReadPointer(configDictSlot, out dictObj) || dictObj == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr entries;
            if (!pm.ReadPointer(dictObj + Offsets.Dict_Entries, out entries))
                return IntPtr.Zero;

            return entries;
        }

        IntPtr EntryAt(IntPtr entries, int index)
        {
            return entries + 0x08 + Offsets.Dict_EntryStride * index;
        }

        bool TryResolveBindable(IntPtr entries, ConfigKey key, out IntPtr bindable)
        {
            bindable = IntPtr.Zero;
            if (entries == IntPtr.Zero || key.Index < 0) return false;
            return pm.ReadPointer(EntryAt(entries, key.Index) + Offsets.Dict_EntryValue, out bindable)
                && bindable != IntPtr.Zero;
        }

        /// <summary>BindableInt 읽기 — int를 double로 담으므로 +0x04에서 double로 읽는다.</summary>
        bool TryReadInt(IntPtr entries, ConfigKey key, out int value)
        {
            value = 0;
            IntPtr obj;
            if (!TryResolveBindable(entries, key, out obj)) return false;

            double d;
            if (!pm.ReadDouble(obj + Offsets.BindableInt_Value, out d)) return false;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < int.MinValue || d > int.MaxValue)
                return false;

            value = (int)d;
            return true;
        }

        /// <summary>
        /// BindableBool 읽기 — +0x0C가 현재값. (+0x0D는 기본값이라 건드리지 않는다:
        /// 실측 Fullscreen이 0C=0/0D=1, Letterboxing이 0C=1/0D=0으로 osu! 기본값과 맞았다.)
        /// </summary>
        bool TryReadBool(IntPtr entries, ConfigKey key, out bool value)
        {
            value = false;
            IntPtr obj;
            if (!TryResolveBindable(entries, key, out obj)) return false;

            byte v;
            if (!pm.ReadByte(obj + Offsets.BindableBool_Value, out v)) return false;
            value = v != 0;
            return true;
        }

        // ────────────────────── 인덱스 스캔 / 검증 ──────────────────────

        long lastScanTicks;
        long lastVerifyTicks;

        /// <summary>
        /// Dictionary를 순회하며 관심 키의 entry 인덱스를 찾는다.
        /// osu! ConfigManager는 시작 시 채워지고 이후 키가 추가되지 않으므로 인덱스는
        /// 안정적이다 — 그래서 매 프레임이 아니라 필요할 때만 돈다.
        /// </summary>
        void ScanConfigDictionary()
        {
            if (configDictSlot == IntPtr.Zero) return;

            IntPtr dictObj;
            if (!pm.ReadPointer(configDictSlot, out dictObj) || dictObj == IntPtr.Zero) return;

            IntPtr entries;
            if (!pm.ReadPointer(dictObj + Offsets.Dict_Entries, out entries) || entries == IntPtr.Zero) return;

            // osu! ConfigManager는 ~234개 entry를 가짐
            int count;
            if (!pm.ReadInt32(dictObj + Offsets.Dict_Count, out count) || count <= 0 || count > 500) return;

            foreach (ConfigKey k in allKeys) k.Index = -1;

            int found = 0;
            for (int i = 0; i < count && found < allKeys.Length; i++)
            {
                IntPtr keyPtr;
                if (!pm.ReadPointer(EntryAt(entries, i) + Offsets.Dict_EntryKey, out keyPtr) || keyPtr == IntPtr.Zero)
                    continue;

                string name = pm.ReadSharpString(keyPtr);
                if (string.IsNullOrEmpty(name)) continue;

                foreach (ConfigKey k in allKeys)
                {
                    if (k.Index >= 0 || k.Name != name) continue;
                    k.Index = i;
                    found++;
                    break;
                }
            }

            if (found != allKeys.Length)
                Console.WriteLine("[Resolution] Config 키 " + found + "/" + allKeys.Length + "개만 발견 (count=" + count + ")");
        }

        /// <summary>
        /// 캐싱한 인덱스가 여전히 그 키를 가리키는지 확인한다. Dictionary가 재해시되면
        /// 인덱스가 밀릴 수 있다 — 실측된 적은 없지만 확인이 싸므로 1초에 한 번 한다.
        /// </summary>
        bool VerifyIndices(IntPtr entries)
        {
            if (entries == IntPtr.Zero) return false;

            foreach (ConfigKey k in allKeys)
            {
                if (k.Index < 0) return false;

                IntPtr keyPtr;
                if (!pm.ReadPointer(EntryAt(entries, k.Index) + Offsets.Dict_EntryKey, out keyPtr)
                    || keyPtr == IntPtr.Zero)
                    return false;

                if (pm.ReadSharpString(keyPtr) != k.Name) return false;
            }
            return true;
        }

        // ────────────────────── 매 프레임 갱신 ──────────────────────

        /// <summary>매 프레임 Resolution 관련 live 값 갱신.</summary>
        public void Refresh()
        {
            IntPtr entries = ResolveEntries();

            bool needScan = false;
            foreach (ConfigKey k in allKeys)
                if (k.Index < 0) { needScan = true; break; }

            if (!needScan && DateTime.UtcNow.Ticks - lastVerifyTicks > TimeSpan.TicksPerSecond)
            {
                lastVerifyTicks = DateTime.UtcNow.Ticks;
                needScan = !VerifyIndices(entries);
            }

            // 스캔은 234개 entry의 문자열을 읽으므로 매 프레임 돌면 안 된다 —
            // 실패해도 1초에 한 번만 재시도한다.
            if (needScan && DateTime.UtcNow.Ticks - lastScanTicks > TimeSpan.TicksPerSecond)
            {
                lastScanTicks = DateTime.UtcNow.Ticks;
                ScanConfigDictionary();
                entries = ResolveEntries();
            }

            bool b;
            if (TryReadBool(entries, keyLetterboxing, out b)) IsLetterboxing = b;

            int v;
            if (TryReadInt(entries, keyLbPosX, out v)) LetterboxPositionX = v;
            if (TryReadInt(entries, keyLbPosY, out v)) LetterboxPositionY = v;

            // 렌더 해상도: 전체화면이면 *Fullscreen 키, 아니면 창모드 키를 쓴다.
            //
            // 이 분기가 없으면 전체화면에서 틀린다. 실측(1.log 02:25:13): 전체화면
            // 1280x1024인 순간에도 Width/Height는 창모드 값 1440x1080을 그대로 들고
            // 있었다. Fullscreen을 보고 골라야 WindowManager 값과 일치한다.
            bool fullscreen;
            if (TryReadBool(entries, keyFullscreen, out fullscreen))
            {
                int w, h;
                if (TryReadInt(entries, fullscreen ? keyWidthFs : keyWidth, out w) &&
                    TryReadInt(entries, fullscreen ? keyHeightFs : keyHeight, out h) &&
                    w > 0 && h > 0)
                {
                    WindowWidth = w;
                    WindowHeight = h;
                }
            }

            RefreshDesktopResolution();
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
