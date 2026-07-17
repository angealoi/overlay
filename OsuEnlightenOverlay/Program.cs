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
    }
}