using System;
using System.IO;
using System.Text;

namespace OsuEnlightenOverlay.Helpers
{
    /// <summary>
    /// Console 출력을 로그 파일에도 기록.
    ///
    /// 이 앱은 WinExe라 콘솔이 붙지 않는다 — exe를 직접 실행하면 Console.WriteLine이
    /// 아무 데도 안 남는다(stdout 리다이렉트로 실행할 때만 보임). 메모리 오프셋이
    /// osu! 빌드/세션마다 달라지는 특성상 사용자가 겪은 상태를 그대로 받아봐야 하므로
    /// exe 옆에 로그 파일을 남긴다.
    ///
    /// AutoFlush: 프로세스가 강제 종료되어도 직전까지의 로그가 남아야 하므로 켠다.
    /// 출력은 초당 몇 줄 수준이라 비용은 무시할 만하다.
    /// </summary>
    internal static class Logger
    {
        static StreamWriter _file;
        static TextWriter _originalOut;

        /// <summary>로그 파일 경로 (exe 옆).</summary>
        public static string LogPath { get; private set; }

        /// <summary>
        /// 로그 파일을 열고 Console.Out을 콘솔+파일 듀얼 라이터로 교체.
        /// 실패해도 앱은 정상 동작해야 하므로 예외를 삼킨다.
        /// </summary>
        public static void Init()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                LogPath = Path.Combine(exeDir, "overlay.log");

                _file = new StreamWriter(LogPath, false, new UTF8Encoding(true));
                _file.AutoFlush = true;

                _originalOut = Console.Out;
                // 렌더 스레드와 맵 파싱 Task가 함께 쓰므로 동기화 래퍼로 감싼다.
                Console.SetOut(TextWriter.Synchronized(new TextWriterDual(_originalOut, _file)));

                Console.WriteLine("=== log start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            }
            catch
            {
                _file = null;
            }
        }

        /// <summary>로그 파일 닫기 및 Console.Out 복원.</summary>
        public static void Shutdown()
        {
            try
            {
                if (_originalOut != null)
                {
                    Console.SetOut(_originalOut);
                    _originalOut = null;
                }
                if (_file != null)
                {
                    _file.Flush();
                    _file.Close();
                    _file = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// 콘솔과 파일에 동시 출력하는 TextWriter 래퍼.
        /// 각 줄 앞에 경과 시각을 붙인다 — 버그 재현 시 순서/간격 파악용.
        /// </summary>
        private sealed class TextWriterDual : TextWriter
        {
            readonly TextWriter _console;
            readonly TextWriter _fileOut;
            bool atLineStart = true;

            public TextWriterDual(TextWriter console, TextWriter fileOut)
            {
                _console = console;
                _fileOut = fileOut;
            }

            public override Encoding Encoding { get { return Encoding.UTF8; } }

            void WriteStamp()
            {
                if (!atLineStart) return;
                atLineStart = false;
                try { _fileOut.Write(DateTime.Now.ToString("HH:mm:ss.fff") + " "); } catch { }
            }

            public override void Write(char value)
            {
                try { _console.Write(value); } catch { }
                WriteStamp();
                try { _fileOut.Write(value); } catch { }
                if (value == '\n') atLineStart = true;
            }

            public override void Write(string value)
            {
                try { _console.Write(value); } catch { }
                WriteStamp();
                try { _fileOut.Write(value); } catch { }
                if (value != null && value.EndsWith("\n")) atLineStart = true;
            }

            public override void WriteLine(string value)
            {
                try { _console.WriteLine(value); } catch { }
                WriteStamp();
                try { _fileOut.WriteLine(value); } catch { }
                atLineStart = true;
            }

            public override void WriteLine()
            {
                try { _console.WriteLine(); } catch { }
                WriteStamp();
                try { _fileOut.WriteLine(); } catch { }
                atLineStart = true;
            }

            public override void Write(char[] buffer, int index, int count)
            {
                try { _console.Write(buffer, index, count); } catch { }
                WriteStamp();
                try { _fileOut.Write(buffer, index, count); } catch { }
                if (count > 0 && buffer[index + count - 1] == '\n') atLineStart = true;
            }
        }
    }
}
