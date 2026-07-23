using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AimAssistPlugin.Sdk;

namespace AimAssistPlugin.Services;

/// <summary>
/// OsuEnlightenOverlay2(공유 메모리 writer) ↔ Reconstructor(이쪽, reader) 간 데이터 브릿지.
/// 이전 TosuService를 대체 — tosu 의존(프로세스 자동 시작, GitHub 자동 다운로드, HTTP polling) 전부 제거하고
/// OsuEnlightenOverlay2가 매 프레임 write한 MMF에서 스냅샷을 읽어 <see cref="LatestState"/>로 노출.
///
/// 데이터 흐름:
///   OsuEnlightenOverlay2.exe (UI 스레드)
///     ↓ RefreshLiveValues() 후 StateBroadcaster.WriteSnapshot() — 매 프레임
///   MMF "Global\ReconstructorState"
///     ↓ OpenExisting + Read<SharedState> — OTD 스레드에서 읽기
///   이 클래스의 LatestState (캐싱)
///
/// 스레드 안전:
///   - writer = OsuEnlightenOverlay2 단일 UI 스레드
///   - reader = Reconstructor(OTD 플러그인) 스레드, Consume()에서 호출
///   writer가 없을 때 MMF 자체가 없으므로 OpenExisting 실패 → LatestState = null → 안전.
///   torn read는 SharedState.Sequence 필드로 검증 — read 전/후 Sequence가 같으면 온전.
/// </summary>
public static class EnlightenService
{
    const string MapName = "Global\\ReconstructorState";

    // ── 백그라운드 상태 ──
    static CancellationTokenSource _cts = new();
    static Task? _monitorTask;

    // ── MMF 핸들 ──
    static MemoryMappedFile? _mmf;
    static MemoryMappedViewAccessor? _accessor;

    // ── 최신 스냅샷 ──
    // SharedState는 value type(56바이트)이라 참조 교체로 atomic publish하려면 박싱 필요.
    // volatile object?에 박싱된 스냅샷을 저장 — write는 매번 새 박스, read는 unbox.
    static volatile object? _latestBoxed;

    // 문자열 슬롯 read용 scratch 버퍼 (매 호출 new 방지)
    static readonly byte[] _stringAreaBuf = new byte[SharedStateLayout.StringAreaSize];

    // 캐시된 osu! 설치 경로 — BeatmapFolder/OsuFilename 합성용. 변경 시에만 갱신.
    static string? _cachedOsuInstallDir;
    static string? _cachedBeatmapFolder;
    static string? _cachedBeatmapOsuFilename;
    static int _lastDirSnapshotSeq = -1; // 문자열이 같은 Sequence에서 읽혔는지 검증

    public static bool IsRunning { get; private set; }

    /// <summary>
    /// 가장 최근에 읽은 스냅샷. MMF에 writer가 없거나 아직 읽기 전이면 null.
    /// Consume()에서 매번 읽음 — getter 자체가 MMF read를 트리거하지 않음 (백그라운드가 갱신).
    /// </summary>
    public static SharedState? LatestState => (_latestBoxed is SharedState s) ? s : (SharedState?)null;

    /// <summary>
    /// osu! 설치 디렉토리. writer(OsuEnlightenOverlay2)가 MMF에 같이 전달하므로 그것을 사용하고,
    /// 없거나 빈 문자열이면 여기서 직접 프로세스에서 추출.
    /// </summary>
    public static string? OsuInstallDir
    {
        get
        {
            // 1순위 — MMF에서 받은 값
            if (!string.IsNullOrEmpty(_cachedOsuInstallDir))
                return _cachedOsuInstallDir;
            // 2순위 — osu! 프로세스에서 직접 추출 (writer가 안 주거나 빈 경우 폴백)
            return ResolveOsuInstallDirFromProcess();
        }
    }

    /// <summary>
    /// 현재 맵의 Songs 폴더 안의 폴더명 (예: "12345 Artist - Title").
    /// </summary>
    public static string? BeatmapFolder => _cachedBeatmapFolder;

    /// <summary>
    /// 현재 맵의 .osu 파일명 (예: "Artist - Title ([Difficult]).osu").
    /// </summary>
    public static string? BeatmapOsuFilename => _cachedBeatmapOsuFilename;

    /// <summary>
    /// 백그라운드 모니터 시작 — AimAssistPlugin.Consume()에서 처음 호출될 때 1회 실행.
    /// OsuEnlightenOverlay2 프로세스를 감시하고 MMF를 열어 매 입력마다 갱신.
    /// </summary>
    public static void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorAsync(_cts.Token));
    }

    static async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // MMF가 열려 있지 않으면 시도 — OsuEnlightenOverlay2가 실행 중이어야 존재.
            if (_accessor == null)
            {
                TryOpenMmf();
                if (_accessor == null)
                {
                    // OsuEnlightenOverlay2가 안 떠 있으면 시작 시도
                    TryStartEnlighten();
                }
            }
            else
            {
                // MMF는 열려 있지만 writer 프로세스가 죽었을 수도 — writer가 dispose하면 MMF가 닫힘.
                // 이 경우 다음 read에서 예외 발생 → catch에서 핸들 정리 → 다시 OpenExisting 시도.
                try
                {
                    RefreshSnapshot();
                }
                catch
                {
                    // MMF가 사라졌거나 writer 종료 → 핸들 정리하고 다음 루프에서 재시도
                    CloseMmf();
                }
            }

            // 1ms 갱신 — TosuService와 동일 cadence. MMF read는 매우 싯다 (수십 ns).
            await Task.Delay(1, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// MMF가 열려 있으면 스냅샷을 한 번 읽어 <see cref="_latestState"/>에 publish.
    /// Sequence torn-read 검증 포함.
    /// </summary>
    static void RefreshSnapshot()
    {
        var acc = _accessor;
        if (acc == null) return;

        // Sequence 읽기 — write 전/후로 같은 값이면 온전한 스냅샷.
        acc.Read<int>(0, out int seqBefore);
        // Sequence가 홀수면 write 중 — 이번은 스킵 (다음 호출에서 읽음).
        if ((seqBefore & 1) != 0) return;

        // ── 임계 구간: 구조체 + 문자열 영역 모두 seqBefore/seqAfter 사이에 read ──
        // 이전엔 구조체 read만 검증 안에 두고, 문자열 read(ReadStringArea)를 검증 밖에서 해
        // writer가 검증 통과 후 문자열 영역을 덮어쓰는 torn read가 발생했다.
        // 맵 전환 시 folder/filename이 섞여 깨진 경로 → FormatException의 원인.
        // 모든 read를 seqBefore/seqAfter 사이에 두어 seqlock 정석으로 보호한다.
        SharedState state;
        acc.Read<SharedState>(0, out state);
        acc.ReadArray(SharedStateLayout.StringAreaOffset, _stringAreaBuf, 0, SharedStateLayout.StringAreaSize);

        // Sequence 재확인 — 이 임계 구간 동안 writer가 write를 시작했는지.
        // 홀수거나 값이 바뀌었으면 이번 스냅샷 폐기.
        acc.Read<int>(0, out int seqAfter);
        if (seqBefore != seqAfter) return;
        if ((seqAfter & 1) != 0) return;

        // 온전한 스냅샷 — 문자열 영역 파싱 (이제 torn read가 아님을 보장).
        _cachedBeatmapFolder      = ReadStringSlot(_stringAreaBuf, SharedStateLayout.BeatmapFolder);
        _cachedBeatmapOsuFilename = ReadStringSlot(_stringAreaBuf, SharedStateLayout.BeatmapOsuFilename);
        string? dir               = ReadStringSlot(_stringAreaBuf, SharedStateLayout.OsuInstallDir);
        if (!string.IsNullOrEmpty(dir)) _cachedOsuInstallDir = dir;

        _lastDirSnapshotSeq = seqAfter;

        _latestBoxed = state; // 박싱 — volatile 필드에 참조 교체로 atomic publish
    }

    /// <summary>
    /// scratch 버퍼에서 지정 슬롯의 UTF-8 문자열을 읽음.
    /// 형식: [4바이트 little-endian length][UTF-8 bytes]
    /// </summary>
    static string? ReadStringSlot(byte[] buf, SharedStateLayout.StringSlot slot)
    {
        int offset = slot.Offset;
        int length = buf[offset]
                   | (buf[offset + 1] << 8)
                   | (buf[offset + 2] << 16)
                   | (buf[offset + 3] << 24);

        if (length <= 0) return null;
        if (length > slot.Capacity) length = slot.Capacity;

        // 슬롯 데이터 시작 = offset + 4
        return Encoding.UTF8.GetString(buf, offset + 4, length);
    }

    static void TryOpenMmf()
    {
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            _accessor = _mmf.CreateViewAccessor(0, SharedStateLayout.TotalSize, MemoryMappedFileAccess.Read);
        }
        catch
        {
            // FileNotFoundException 등 — writer가 아직 안 띄움
            _mmf = null;
            _accessor = null;
        }
    }

    static void CloseMmf()
    {
        try { _accessor?.Dispose(); } catch { }
        try { _mmf?.Dispose(); } catch { }
        _accessor = null;
        _mmf = null;
        _latestBoxed = null;
    }

    /// <summary>
    /// OsuEnlightenOverlay2.exe를 플러그인 폴더에서 찾아 시작.
    /// TosuService.StartTosuAsync와 동일한 경로 탐지 로직 사용.
    /// </summary>
    static void TryStartEnlighten()
    {
        // 이미 실행 중이면 시작할 필요 없음
        if (Process.GetProcessesByName("OsuEnlightenOverlay").Length > 0) return;

        string text = Path.Combine(AppContext.BaseDirectory, "userdata");
        string pluginFolder = Directory.Exists(text)
            ? Path.Combine(text, "Plugins", "Reconstructor")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "OpenTabletDriver", "Plugins", "Reconstructor");
        if (!Directory.Exists(pluginFolder)) return;

        string exePath = Path.Combine(pluginFolder, "OsuEnlightenOverlay.exe");
        if (!File.Exists(exePath)) return;

        try
        {
            // UseShellExecute=true로 UAC 승격 다이얼로그 허용 — 오버레이는 requireAdministrator.
            // OTD가 비관리자로 떠 있는 경우, OsuEnlightenOverlay2는 별도 관리자 프로세스로 띄워져야
            // Global namespace MMF를 만들 수 있고 osu! 메모리를 읽을 수 있다.
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = pluginFolder,
                UseShellExecute = true,
            });
        }
        catch
        {
            // 시작 실패 — 다음 폴링에서 재시도
        }
    }

    /// <summary>
    /// osu! 프로세스에서 직접 설치 경로 추출. MMF에 경로가 없을 때 폴백.
    /// </summary>
    static string? ResolveOsuInstallDirFromProcess()
    {
        try
        {
            var proc = Process.GetProcessesByName("osu!").FirstOrDefault();
            if (proc == null) return null;
            try
            {
                // OTD는 비관리자일 수 있어 MainModule 접근이 거부될 수 있음 — try/catch.
                string exePath = proc.MainModule?.FileName ?? "";
                proc.Dispose();
                if (!string.IsNullOrEmpty(exePath))
                    return Path.GetDirectoryName(exePath);
            }
            catch { /* Win32Exception — 권한 부족 */ }
        }
        catch { }
        return null;
    }
}
