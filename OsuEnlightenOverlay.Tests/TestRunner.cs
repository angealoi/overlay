using System;
using System.IO;
using OpenTK;
using OsuEnlightenOverlay.Memory;
using OsuEnlightenOverlay.Gameplay.Beatmap;
using OsuEnlightenOverlay.Gameplay.Difficulty;

namespace OsuEnlightenOverlay.Tests
{
    /// <summary>
    /// 프레임워크 없는 자체 회귀 테스트 (G9). osu!/GL/WinForms 없이 돌 수 있는 순수 로직만 —
    /// AOB 패턴 파싱, 난이도 수학(AR→PreEmpt), 비트맵 파서. 실패 시 exit code 1.
    /// nuget 의존을 피하려 콘솔 러너로 구현. `dotnet run` 또는 exe 직접 실행.
    /// </summary>
    internal static class TestRunner
    {
        static int passed = 0;
        static int failed = 0;

        static void Check(bool cond, string name)
        {
            if (cond) { passed++; }
            else { failed++; Console.WriteLine("  [FAIL] " + name); }
        }

        static void Eq(int actual, int expected, string name)
        {
            Check(actual == expected, name + " (기대 " + expected + ", 실제 " + actual + ")");
        }

        static int Main()
        {
            Console.WriteLine("=== OsuEnlightenOverlay 회귀 테스트 (G9) ===");

            TestParsePattern();
            TestDifficulty();
            TestBeatmapParser();

            Console.WriteLine();
            Console.WriteLine("통과 " + passed + " / 실패 " + failed);
            if (failed > 0)
            {
                Console.WriteLine("[X] 테스트 실패");
                return 1;
            }
            Console.WriteLine("[OK] 전부 통과");
            return 0;
        }

        // AobScanner.ParsePattern — 패턴 문자열 → 바이트 배열 + 마스크
        static void TestParsePattern()
        {
            Console.WriteLine("[ParsePattern]");
            byte[] pat; string mask;
            AobScanner.ParsePattern("5E 5F ?? A1", out pat, out mask);
            Eq(pat.Length, 4, "길이 4");
            Check(pat[0] == 0x5E && pat[1] == 0x5F && pat[2] == 0x00 && pat[3] == 0xA1, "바이트 값 5E 5F 00 A1");
            Check(mask == "FF0F", "마스크 FF0F (기대 FF0F, 실제 " + mask + ")");

            // 단일 물음표(?)도 와일드카드
            AobScanner.ParsePattern("48 ? 04", out pat, out mask);
            Check(mask == "F0F", "단일 ? 와일드카드 (실제 " + mask + ")");
        }

        // DifficultyCalculator — 표준 osu! AR→PreEmpt 상수 (AR5=1200, AR9=600, AR0=1800, AR10=450)
        static void TestDifficulty()
        {
            Console.WriteLine("[Difficulty AR→PreEmpt]");
            Eq(Pre(5), 1200, "AR5 → 1200ms");
            Eq(Pre(9), 600, "AR9 → 600ms");
            Eq(Pre(0), 1800, "AR0 → 1800ms");
            Eq(Pre(10), 450, "AR10 → 450ms");

            // FadeIn은 H1에서 상수 400으로 고정됨
            DifficultyValues dv = DifficultyCalculator.CalculateWithValues(9, 4, 8, 512f, 1.0f);
            Eq(dv.FadeIn, 400, "FadeIn 상수 400 (H1)");
        }

        static int Pre(double ar)
        {
            return DifficultyCalculator.CalculateWithValues(ar, 4, 8, 512f, 1.0f).PreEmpt;
        }

        // BeatmapParser — 최소 .osu 파싱 + HR 세로 반전
        static void TestBeatmapParser()
        {
            Console.WriteLine("[BeatmapParser]");
            string osu =
                "osu file format v14\n\n" +
                "[General]\nMode: 0\n\n" +
                "[Difficulty]\nApproachRate:9\nCircleSize:4\nOverallDifficulty:8\nSliderMultiplier:1.4\nSliderTickRate:1\n\n" +
                "[HitObjects]\n" +
                "256,100,1000,1,0\n" +
                "100,150,2000,2,0,L|300:150,1,200\n";

            string path = Path.Combine(Path.GetTempPath(), "ueo_test_" + Guid.NewGuid().ToString("N") + ".osu");
            try
            {
                File.WriteAllText(path, osu);

                BeatmapData b = BeatmapParser.Parse(path, false);
                Eq(b.HitObjects.Count, 2, "히트오브젝트 2개");
                if (b.HitObjects.Count >= 1)
                {
                    Eq((int)b.HitObjects[0].Position.X, 256, "첫 객체 X=256");
                    Eq((int)b.HitObjects[0].Position.Y, 100, "첫 객체 Y=100 (flip 없음)");
                    Eq(b.HitObjects[0].StartTime, 1000, "첫 객체 StartTime=1000");
                }
                Check((int)Math.Round(b.ApproachRate) == 9, "ApproachRate 9 파싱");

                // HR 세로 반전: y → 384 - y  ⇒ 100 → 284
                BeatmapData bf = BeatmapParser.Parse(path, true);
                if (bf.HitObjects.Count >= 1)
                    Eq((int)bf.HitObjects[0].Position.Y, 284, "HR flip: Y 100 → 284");
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
