using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OsuEnlightenOverlay.Memory;
using OsuEnlightenOverlay.Overlay;
using OsuEnlightenOverlay.ControlPanel;
using OsuEnlightenOverlay.Helpers;

namespace OsuEnlightenOverlay
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // High-DPI 인식 (G8) — 반드시 어떤 창/비주얼 초기화보다 먼저.
            // 이 오버레이는 osu!(DPI-aware) 창의 클라이언트 좌표를 읽어 자기 창을 그 위에 맞춘다.
            // 프로세스가 DPI-unaware이면 스케일된 디스플레이(예: 125/150%)에서 Win32가 좌표를
            // 가상화(축소)해 돌려줘 오버레이가 어긋나거나 비트맵 확대로 흐릿해진다. 프로세스를
            // DPI-aware로 만들어 물리 픽셀 좌표계를 osu!와 일치시킨다. PerMonitorV2→System 폴백.
            DpiAwareness.Enable();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // exe 옆 overlay.log 에 기록 — WinExe라 콘솔이 없어 이게 유일한 진단 경로
            Logger.Init();

            Console.WriteLine("=== osu! Enlighten Overlay ===");
            Console.WriteLine();

            // 1. 메모리 리더 초기화
            Console.WriteLine("[1] osu! 프로세스 연결 중...");
            OsuMemoryReader reader = new OsuMemoryReader();
            if (!reader.Initialize())
            {
                Console.WriteLine("[!] osu! 프로세스를 찾을 수 없거나 메모리 읽기 실패.");
                Console.WriteLine("    osu! 가 실행 중인지 확인하세요.");
                // WinExe라 콘솔이 없어 Console.ReadLine()은 즉시 null 반환(무의미)이었다 (G1).
                // 사용자가 실제로 볼 수 있도록 MessageBox로 안내한 뒤 종료한다.
                MessageBox.Show(
                    "osu! 프로세스를 찾을 수 없거나 메모리 읽기에 실패했습니다.\n" +
                    "osu!가 실행 중인지 확인한 뒤 다시 실행하세요.",
                    "osu! Enlighten Overlay",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Logger.Shutdown();
                return;
            }
            Console.WriteLine("[OK] 메모리 리더 초기화 성공");
            Console.WriteLine();

            // DPI 배율 경고 (G8) — 이 오버레이는 100% 배율에서만 osu! 위에 정확히 정렬된다.
            // DPI-aware 상태(위 DpiAwareness.Enable)에서 시스템 DPI를 조회해 96(=100%)이 아니면
            // 시작 시 경고한다. 차단이 아닌 안내 — 사용자가 계속 진행할 수 있다.
            int sysDpi = DpiAwareness.GetSystemDpi();
            if (sysDpi != 96)
            {
                int pct = (int)Math.Round(sysDpi * 100.0 / 96.0);
                Console.WriteLine("[!] DPI 배율 " + pct + "% 감지 — 100% 권장");
                MessageBox.Show(
                    "Windows 디스플레이 배율이 100%가 아닙니다 (현재 약 " + pct + "%).\n" +
                    "이 오버레이는 배율 100%에서만 osu! 위에 정확히 정렬됩니다.\n\n" +
                    "설정 → 시스템 → 디스플레이 → '배율'을 100%로 변경한 뒤 다시 실행하세요.\n" +
                    "(이대로 계속하면 오버레이가 어긋날 수 있습니다.)",
                    "osu! Enlighten Overlay — DPI 경고",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 설정 로드
            OverlaySettings settings = new OverlaySettings();
            SettingsSerializer.Load(settings);

            // 2. 오버레이 창 생성 (독립 창 — 컨트롤 패널과 별개)
            Console.WriteLine("[2] 오버레이 창 생성 중...");
            OverlayForm overlay = new OverlayForm(reader, settings);
            overlay.Load += delegate
            {
                Console.WriteLine("[OK] 오버레이 창 생성됨");
                Console.WriteLine("    - 창 스타일: TOPMOST|NOACTIVATE|TOOLWINDOW|TRANSPARENT|LAYERED");
                Console.WriteLine("    - 캡처 차단: WDA_EXCLUDEFROMCAPTURE");
                Console.WriteLine("    - 클릭 투과: WS_EX_TRANSPARENT + WM_NCHITTEST");
                Console.WriteLine("    - 배경: 불투명 검정 (GL.ClearColor(0,0,0,1))");
                Console.WriteLine();
                // StartOverlay는 Show() 후에 Program.cs에서 명시적으로 호출
            };

            // 오버레이를 독립 창으로 표시 (메인 폼이 아님)
            // Load 이벤트에서 StartOverlay 호출하지 않음 — Show() 완료 후 명시적으로 호출
            overlay.Show();

            // 3. 컨트롤 패널 생성 (메인 폼)
            Console.WriteLine("[3] 컨트롤 패널 생성 중...");
            ControlPanelForm panel = new ControlPanelForm(settings, overlay);
            panel.FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                // 컨트롤 패널 종료 시 오버레이도 종료
                overlay.Close();
            };

            Application.ApplicationExit += delegate
            {
                reader.Dispose();
                Logger.Shutdown();
            };

            // 컨트롤 패널을 메인 폼으로 실행
            // 오버레이 시작 — 컨트롤 패널 생성 후 (모든 UI 초기화 완료 후)
            overlay.StartOverlay();
            Console.WriteLine("[OK] osu! 창 추종 시작");
            Console.WriteLine("[3] 실행 중 — F10 또는 컨트롤 패널 종료로 exit");
            Console.WriteLine("    Play 모드 진입 시 오버레이 표시, 메뉴에서는 숨김");
            Console.WriteLine();

            Application.Run(panel);
        }
    }

    /// <summary>
    /// 프로세스 DPI 인식 설정 (G8). 어떤 창보다 먼저 호출해야 한다.
    /// PerMonitorV2(Win10 1703+) → PerMonitor(Win8.1+) → System(Vista+) 순으로 폴백.
    /// </summary>
    internal static class DpiAwareness
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
        static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        [DllImport("shcore.dll")]
        static extern int SetProcessDpiAwareness(int value); // 2=PerMonitor, 1=System

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        public static void Enable()
        {
            // 각 API는 구 Windows에서 없을 수 있으므로(EntryPointNotFound) try로 감싼다.
            try { if (SetProcessDpiAwarenessContext(PerMonitorAwareV2)) return; } catch { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }   // S_OK=0
            try { SetProcessDPIAware(); } catch { }
        }

        [DllImport("user32.dll")]
        static extern uint GetDpiForSystem();  // Win10 1607+

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("gdi32.dll")]
        static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        const int LOGPIXELSX = 88;

        /// <summary>
        /// 시스템(주 모니터) DPI. 100% 배율 = 96. Enable() 이후(DPI-aware 상태)에 호출해야
        /// 실제 값이 나온다(unaware면 항상 96). 알 수 없으면 96(=100%, 경고 안 함)을 반환.
        /// </summary>
        public static int GetSystemDpi()
        {
            try { uint d = GetDpiForSystem(); if (d > 0) return (int)d; } catch { }
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                int dpi = GetDeviceCaps(hdc, LOGPIXELSX);
                ReleaseDC(IntPtr.Zero, hdc);
                if (dpi > 0) return dpi;
            }
            return 96;
        }
    }
}