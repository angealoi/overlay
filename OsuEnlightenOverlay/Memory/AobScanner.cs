using System;
using System.Collections.Generic;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// 배치 스캔 요청 — 패턴 하나에 대한 입력과 결과.
    /// </summary>
    internal class AobScanRequest
    {
        public readonly byte[] Pattern;
        /// <summary>true인 자리만 Pattern과 비교. string 인덱싱보다 내부 루프가 훨씬 싸다.</summary>
        public readonly bool[] Compare;
        /// <summary>true면 모든 매치를 모은다. false면 첫 매치에서 이 요청은 끝.</summary>
        public readonly bool AllMatches;

        public readonly List<IntPtr> Results = new List<IntPtr>();
        public bool Done;

        public IntPtr First { get { return Results.Count > 0 ? Results[0] : IntPtr.Zero; } }

        public AobScanRequest(string patternString, bool allMatches)
        {
            AllMatches = allMatches;
            byte[] pat;
            string mask;
            AobScanner.ParsePattern(patternString, out pat, out mask);
            Pattern = pat;
            Compare = BuildCompare(mask);
        }

        static bool[] BuildCompare(string mask)
        {
            bool[] c = new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++)
                c[i] = mask[i] == 'F';
            return c;
        }
    }

    /// <summary>
    /// AOB (Array-Of-Bytes) 패턴 스캐너.
    /// osu! 의 전체 프로세스 메모리에서 패턴 + 마스크 매칭.
    /// JIT'd 코드는 모듈 영역이 아닌 동적 메모리에 있으므로 전체 스캔 필요.
    ///
    /// 성능(D1): 리전 버퍼를 재사용하고, 여러 시그니처를 한 번의 메모리 패스로 처리한다.
    /// 예전에는 리전마다 new byte[최대 100MB]를 잡고 시그니처마다 전체 메모리를 다시 읽어서
    /// 기동 시 전체 패스가 9회 돌았다.
    /// </summary>
    internal class AobScanner
    {
        const int MaxRegionSize = 100 * 1024 * 1024; // 100MB 초과 리전은 건너뜀

        // 리전 버퍼 재사용 — 스캔 사이에도 유지된다.
        // ThreadStatic이라 다른 스레드에서 스캔해도 버퍼가 섞이지 않는다.
        [ThreadStatic] static byte[] sharedBuffer;

        static byte[] GetBuffer(int size)
        {
            if (sharedBuffer == null || sharedBuffer.Length < size)
                sharedBuffer = new byte[size];
            return sharedBuffer;
        }

        /// <summary>
        /// 여러 패턴을 스캔. 결과는 각 request.Results에 담긴다.
        ///
        /// 2단계 폴백(D1 후속):
        ///   1단계 — PRIVATE(JIT 코드 힙) 실행 영역만 스캔. osu!는 이 메서드들을 런타임 JIT하므로
        ///           보통 여기서 전부 찾는다. 실행 영역 전체 대비 크게 줄어 ~40배 빠르다.
        ///   2단계 — 1단계에서 **하나도 못 찾은 패턴**만, 나머지 실행 영역(IMAGE/MAPPED = ngen/
        ///           ReadyToRun 네이티브 이미지)에서 다시 찾는다.
        /// 두 단계의 합집합은 "실행 가능한 모든 영역"이라, ngen되어 코드가 IMAGE로 가더라도
        /// 침묵 실패하지 않는다("PRIVATE만 스캔"의 41배는 취하되 정답성은 물리 법칙에 의존).
        /// </summary>
        public static void ScanBatch(ProcessMemory pm, IList<AobScanRequest> requests)
        {
            if (requests == null || requests.Count == 0) return;

            // VirtualQueryEx는 한 번만 — 두 단계가 같은 영역 목록을 공유한다.
            var regions = new List<ProcessMemory.MemoryRegion>(pm.EnumerateExecutableRegions());

            // 1단계: PRIVATE(JIT) 영역만
            ScanPass(pm, requests, regions, wantPrivate: true);

            // 1단계에서 매치가 하나도 없는 요청만 추린다.
            // (첫-매치 요청이면 Done, 전체-매치 요청이면 Results가 채워졌는지로 판정 — 둘 다 Results.Count.)
            List<AobScanRequest> unresolved = null;
            for (int r = 0; r < requests.Count; r++)
            {
                if (requests[r].Results.Count == 0)
                {
                    if (unresolved == null) unresolved = new List<AobScanRequest>();
                    unresolved.Add(requests[r]);
                }
            }

            if (unresolved == null)
                return; // 흔한 경로 — 전부 PRIVATE에서 찾음, IMAGE는 안 훑는다

            // 2단계: 나머지 실행 영역(IMAGE/MAPPED)에서 미해결 패턴만
            Console.WriteLine("[AOB] PRIVATE 스캔 후 미해결 " + unresolved.Count + "/" + requests.Count
                + " → IMAGE/MAPPED 폴백");
            ScanPass(pm, unresolved, regions, wantPrivate: false);
        }

        /// <summary>
        /// 요청들을 지정한 영역 종류(PRIVATE 또는 그 외)에서 한 패스로 스캔.
        /// 버킷·프리필터는 넘겨받은 요청 집합에서만 만든다 — 폴백 시 미해결 패턴만 대상.
        /// </summary>
        static void ScanPass(ProcessMemory pm, IList<AobScanRequest> requests,
            List<ProcessMemory.MemoryRegion> regions, bool wantPrivate)
        {
            if (requests.Count == 0) return;

            // 첫 고정 바이트로 버킷팅.
            // 실측(osu! 441MB): 전체 메모리 읽기는 ~24ms인데 패턴 하나를 훑는 매칭이 ~260ms다.
            // 즉 비용은 읽기가 아니라 매칭이 지배한다. 바이트 위치마다 패턴을 전부 검사하면
            // 패턴 수에 비례해 느려지므로, 첫 바이트가 다른 패턴은 아예 보지 않는다.
            List<AobScanRequest>[] byFirstByte = new List<AobScanRequest>[256];
            // 핫 루프용 빠른 기각 테이블 — byte[]가 List<>[]보다 캐시에 잘 맞고 로드가 싸다
            byte[] hasFirstByte = new byte[256];
            List<AobScanRequest> wildcardFirst = null;

            // 2바이트 프리필터 — 모든 요청의 첫 두 바이트가 고정일 때만 쓸 수 있다.
            // 첫 바이트만 보면 실행 영역에서 7.83%가 걸려 매번 전체 대조에 들어가는데,
            // 두 바이트를 보면 0.28%로 떨어진다(실측, 진입 28배 감소). 첫 바이트가
            // 와일드카드거나 둘째 바이트가 와일드카드인 패턴이 하나라도 있으면 안전하게 끈다.
            bool[] hasFirstTwo = new bool[65536];
            bool twoByteUsable = true; // 아래 루프에서 예외가 하나라도 나오면 꺼진다

            for (int r = 0; r < requests.Count; r++)
            {
                AobScanRequest req = requests[r];
                if (req.Compare.Length > 0 && req.Compare[0])
                {
                    int b = req.Pattern[0];
                    if (byFirstByte[b] == null) byFirstByte[b] = new List<AobScanRequest>();
                    byFirstByte[b].Add(req);
                    hasFirstByte[b] = 1;

                    if (req.Compare.Length > 1 && req.Compare[1])
                        hasFirstTwo[req.Pattern[0] | (req.Pattern[1] << 8)] = true;
                    else
                        twoByteUsable = false; // 둘째 바이트가 와일드카드 → 2바이트 필터 불가
                }
                else
                {
                    // 첫 바이트가 와일드카드면 버킷팅이 안 되므로 매 위치에서 검사해야 한다
                    if (wildcardFirst == null) wildcardFirst = new List<AobScanRequest>();
                    wildcardFirst.Add(req);
                    twoByteUsable = false;
                }
            }

            // 코드 시그니처는 실행 가능 페이지에만 있으므로 데이터/GC 힙은 훑지 않는다 (D1 후속).
            // 실측: 읽기 가능 전부(809MB) → 실행 가능만(250MB), 매치 누락 0.
            // 이 패스는 그중 wantPrivate로 지정된 종류(PRIVATE 또는 IMAGE/MAPPED)만 훑는다.
            for (int ri = 0; ri < regions.Count; ri++)
            {
                ProcessMemory.MemoryRegion region = regions[ri];
                if (region.IsPrivate != wantPrivate)
                    continue;

                // 남은 요청이 없으면 더 읽을 이유가 없다
                bool anyPending = false;
                for (int r = 0; r < requests.Count; r++)
                    if (!requests[r].Done) { anyPending = true; break; }
                if (!anyPending) return;

                int regionSize = (int)region.Size.ToInt64();
                if (regionSize <= 0 || regionSize > MaxRegionSize)
                    continue;

                byte[] buffer = GetBuffer(regionSize);
                if (!pm.ReadBytes(region.BaseAddress, buffer, regionSize))
                    continue;

                if (twoByteUsable)
                    ScanBufferTwoByte(buffer, regionSize, region.BaseAddress, byFirstByte, hasFirstTwo);
                else
                    ScanBuffer(buffer, regionSize, region.BaseAddress, byFirstByte, hasFirstByte, wildcardFirst);
            }
        }

        /// <summary>
        /// 2바이트 프리필터 버전 — 모든 패턴의 첫 두 바이트가 고정일 때만 호출된다.
        /// 위치마다 16비트 하나로 기각하므로 TryMatch 진입이 크게 줄어든다.
        /// </summary>
        static unsafe void ScanBufferTwoByte(byte[] buffer, int regionSize, IntPtr baseAddress,
            List<AobScanRequest>[] byFirstByte, bool[] hasFirstTwo)
        {
            int last = regionSize - 2;
            fixed (byte* pBuf = buffer)
            fixed (bool* pHas = hasFirstTwo)
            {
                for (int i = 0; i <= last; i++)
                {
                    if (!pHas[pBuf[i] | (pBuf[i + 1] << 8)]) continue;

                    // 드물게만 도달 — 첫 두 바이트가 어떤 패턴과 일치한 경우
                    List<AobScanRequest> bucket = byFirstByte[pBuf[i]];
                    if (bucket != null)
                        for (int k = 0; k < bucket.Count; k++)
                            TryMatch(buffer, regionSize, baseAddress, i, bucket[k]);
                }
            }
        }

        /// <summary>
        /// 버퍼 하나를 모든 패턴에 대해 한 번에 훑는다.
        /// 버퍼는 재사용이라 regionSize보다 클 수 있으므로 유효 범위를 명시적으로 받는다.
        ///
        /// 핫 루프는 수백 MB를 바이트 단위로 도는 곳이라 배열 경계 검사가 그대로 비용이 된다.
        /// unsafe 포인터로 검사를 없앤다 — 인덱스는 [0, regionSize)와 [0,255]로 이미 갇혀 있다.
        /// </summary>
        static unsafe void ScanBuffer(byte[] buffer, int regionSize, IntPtr baseAddress,
            List<AobScanRequest>[] byFirstByte, byte[] hasFirstByte, List<AobScanRequest> wildcardFirst)
        {
            // 첫 바이트가 와일드카드인 패턴이 있으면 빠른 기각을 쓸 수 없다
            if (wildcardFirst != null)
            {
                for (int i = 0; i < regionSize; i++)
                {
                    List<AobScanRequest> bucket = byFirstByte[buffer[i]];
                    if (bucket != null)
                        for (int k = 0; k < bucket.Count; k++)
                            TryMatch(buffer, regionSize, baseAddress, i, bucket[k]);
                    for (int k = 0; k < wildcardFirst.Count; k++)
                        TryMatch(buffer, regionSize, baseAddress, i, wildcardFirst[k]);
                }
                return;
            }

            fixed (byte* pBuf = buffer)
            fixed (byte* pHas = hasFirstByte)
            {
                for (int i = 0; i < regionSize; i++)
                {
                    if (pHas[pBuf[i]] == 0) continue;

                    // 여기부터는 드물게만 온다 — 첫 바이트가 어떤 패턴과 일치한 경우
                    List<AobScanRequest> bucket = byFirstByte[pBuf[i]];
                    for (int k = 0; k < bucket.Count; k++)
                        TryMatch(buffer, regionSize, baseAddress, i, bucket[k]);
                }
            }
        }

        /// <summary>
        /// 위치 i에서 패턴 전체를 대조. 버킷으로 들어온 경우 j=0은 이미 일치가 보장된다.
        /// </summary>
        static void TryMatch(byte[] buffer, int regionSize, IntPtr baseAddress, int i, AobScanRequest req)
        {
            if (req.Done) return;

            byte[] pattern = req.Pattern;
            bool[] compare = req.Compare;
            int patLen = pattern.Length;
            if (i + patLen > regionSize) return;

            for (int j = 0; j < patLen; j++)
            {
                if (compare[j] && buffer[i + j] != pattern[j])
                    return;
            }

            req.Results.Add(baseAddress + i);
            if (!req.AllMatches)
                req.Done = true;
        }

        /// <summary>
        /// 스캔이 끝난 요청에서 static field slot 주소로 해석.
        /// 매치 위치 + OperandSkip에 있는 4바이트 absolute address를 읽고 PostAdd를 더한다.
        /// 실패 시 IntPtr.Zero.
        /// </summary>
        public static IntPtr ResolveSlot(ProcessMemory pm, AobSignature sig, AobScanRequest req)
        {
            IntPtr match = req.First;
            if (match == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr slot;
            if (!pm.ReadPointer(match + sig.OperandSkip, out slot))
                return IntPtr.Zero;

            return slot + sig.PostAdd;
        }

        /// <summary>
        /// 패턴 문자열("5E 5F 5D C3 A1 ?? ?? ?? ?? 89 ?? 04")을 바이트 배열과 마스크로 변환.
        /// </summary>
        public static void ParsePattern(string patternString, out byte[] pattern, out string mask)
        {
            string[] parts = patternString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            pattern = new byte[parts.Length];
            char[] maskChars = new char[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "??" || parts[i] == "?")
                {
                    pattern[i] = 0;
                    maskChars[i] = '0';
                }
                else
                {
                    pattern[i] = Convert.ToByte(parts[i], 16);
                    maskChars[i] = 'F';
                }
            }
            mask = new string(maskChars);
        }
    }
}
