using System;
using System.IO;
using System.Text;

namespace OsuEnlightenOverlay.Helpers
{
    /// <summary>
    /// Console 출력을 로그 파일에 동시 기록하는 간단 로거.
    /// Init() 호출 후 모든 Console.WriteLine/Write 는 파일에도 기록됨.
    /// </summary>
    internal static class Logger
    {
        private static StreamWriter _logWriter;
        private static TextWriterDual _dual;

        /// <summary>
        /// 로그 파일을 열고 Console.Out 을 듀얼 라이터로 교체.
        /// </summary>
        public static void Init()
        {
            // 로그 파일 저장 비활성화 — 콘솔 출력만 사용
        }

        /// <summary>
        /// 로그 파일 닫기 및 Console.Out 복원.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                if (_dual != null)
                {
                    Console.SetOut(_dual.Original);
                    _dual = null;
                }
                if (_logWriter != null)
                {
                    _logWriter.Flush();
                    _logWriter.Close();
                    _logWriter = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// 콘솔과 파일에 동시 출력하는 TextWriter 래퍼.
        /// </summary>
        private sealed class TextWriterDual : TextWriter
        {
            public readonly TextWriter Original;
            private readonly TextWriter _file;

            public TextWriterDual(TextWriter console, TextWriter file)
            {
                Original = console;
                _file = file;
            }

            public override Encoding Encoding => Original.Encoding;

            public override void Write(char value)
            {
                Original.Write(value);
                try { _file.Write(value); } catch { }
            }

            public override void Write(string value)
            {
                Original.Write(value);
                try { _file.Write(value); } catch { }
            }

            public override void WriteLine(string value)
            {
                Original.WriteLine(value);
                try { _file.WriteLine(value); } catch { }
            }

            public override void WriteLine()
            {
                Original.WriteLine();
                try { _file.WriteLine(); } catch { }
            }

            public override void Write(char[] buffer, int index, int count)
            {
                Original.Write(buffer, index, count);
                try { _file.Write(buffer, index, count); } catch { }
            }
        }
    }
}