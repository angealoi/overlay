using System;
using System.Collections.Generic;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// AOB (Array-Of-Bytes) 패턴 스캐너.
    /// osu! 의 전체 프로세스 메모리에서 패턴 + 마스크 매칭.
    /// JIT'd 코드는 모듈 영역이 아닌 동적 메모리에 있으므로 전체 스캔 필요.
    /// </summary>
    internal class AobScanner
    {
        /// <summary>
        /// 전체 프로세스 메모리에서 단일 매치 검색.
        /// </summary>
        public static IntPtr Scan(ProcessMemory pm, byte[] pattern, string mask)
        {
            foreach (ProcessMemory.MemoryRegion region in pm.EnumerateReadableRegions())
            {
                int regionSize = (int)region.Size.ToInt64();
                if (regionSize <= 0 || regionSize > 100 * 1024 * 1024) // 100MB 제한
                    continue;

                byte[] buffer = new byte[regionSize];
                if (!pm.ReadBytes(region.BaseAddress, buffer, regionSize))
                    continue;

                for (int i = 0; i <= buffer.Length - pattern.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (mask[j] == 'F' && buffer[i + j] != pattern[j])
                        {
                            found = false;
                            break;
                        }
                    }
                    if (found)
                        return region.BaseAddress + i;
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 전체 프로세스 메모리에서 다중 매치 검색.
        /// </summary>
        public static List<IntPtr> ScanAll(ProcessMemory pm, byte[] pattern, string mask)
        {
            List<IntPtr> results = new List<IntPtr>();

            foreach (ProcessMemory.MemoryRegion region in pm.EnumerateReadableRegions())
            {
                int regionSize = (int)region.Size.ToInt64();
                if (regionSize <= 0 || regionSize > 100 * 1024 * 1024)
                    continue;

                byte[] buffer = new byte[regionSize];
                if (!pm.ReadBytes(region.BaseAddress, buffer, regionSize))
                    continue;

                for (int i = 0; i <= buffer.Length - pattern.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (mask[j] == 'F' && buffer[i + j] != pattern[j])
                        {
                            found = false;
                            break;
                        }
                    }
                    if (found)
                        results.Add(region.BaseAddress + i);
                }
            }
            return results;
        }

        /// <summary>
        /// 패턴 문자열("5E 5F 5D C3 A1 ?? ?? ?? ?? 89 ?? 04")을 바이트 배열과 마스크로 변환.
        /// </summary>
        public static void ParsePattern(string patternString, out byte[] pattern, out string mask)
        {
            string[] parts = patternString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            pattern = new byte[parts.Length];
            mask = new string(' ', parts.Length);

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "??" || parts[i] == "?")
                {
                    pattern[i] = 0;
                    mask = mask.Remove(i, 1).Insert(i, "0");
                }
                else
                {
                    pattern[i] = Convert.ToByte(parts[i], 16);
                    mask = mask.Remove(i, 1).Insert(i, "F");
                }
            }
        }
    }
}