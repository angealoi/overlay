using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// 테스트 프로젝트가 internal 핵심 로직(AobScanner/BeatmapParser/DifficultyCalculator)에
// 접근할 수 있게 한다 (G9 — 회귀 테스트).
[assembly: InternalsVisibleTo("OsuEnlightenOverlay.Tests")]

[assembly: AssemblyTitle("OsuEnlightenOverlay")]
[assembly: AssemblyDescription("osu! Enlighten & Difficulty Changer Overlay")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("OsuEnlightenOverlay")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("12345678-1234-1234-1234-123456789012")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]