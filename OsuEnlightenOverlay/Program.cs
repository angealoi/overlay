using System;
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
                Console.ReadLine();
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
}