using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OsuEnlightenOverlay.Memory
{
    /// <summary>
    /// ReadProcessMemory 기반 32-bit 프로세스 메모리 읽기 래퍼.
    /// osu! stable (.NET 4, 32-bit) 의 메모리를 읽기 전용으로 접근.
    /// </summary>
    internal class ProcessMemory : IDisposable
    {
        const int PROCESS_VM_READ = 0x0010;
        const int PROCESS_QUERY_INFORMATION = 0x0400;
        const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "K32EnumProcesses")]
        static extern bool EnumProcesses(uint[] lpidProcess, int cb, out int lpcbNeeded);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageName")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "K32EnumProcessModules")]
        static extern bool EnumProcessModules(IntPtr hProcess, IntPtr[] lphModule, int cb, out int lpcbNeeded);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "K32GetModuleFileNameEx")]
        static extern int GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, System.Text.StringBuilder lpFilename, int nSize);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "K32GetModuleInformation")]
        static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO lpmodinfo, int cb);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, int dwLength);

        [StructLayout(LayoutKind.Sequential)]
        struct MODULEINFO
        {
            public IntPtr lpBaseOfDll;
            public int SizeOfImage;
            public IntPtr EntryPoint;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        const uint MEM_COMMIT = 0x1000;
        const uint PAGE_EXECUTE = 0x10;
        const uint PAGE_EXECUTE_READ = 0x20;
        const uint PAGE_EXECUTE_READWRITE = 0x40;
        const uint PAGE_READWRITE = 0x04;
        const uint PAGE_READONLY = 0x02;

        public IntPtr Handle { get; private set; }
        public int ProcessId { get; private set; }
        public bool IsOpen { get { return Handle != IntPtr.Zero; } }

        int bytesRead; // out _ 대체용

        // 재사용 버퍼 — 매번 new byte[] 할당 방지
        byte[] buf4 = new byte[4];
        byte[] buf2 = new byte[2];
        byte[] buf1 = new byte[1];
        byte[] buf8 = new byte[8];

        /// <summary>
        /// 전체 프로세스 메모리에서 커밋된 실행 가능 영역을 순회.
        /// JIT'd 코드를 포함한 모든 메모리 영역을 스캔하기 위함.
        /// </summary>
        public IEnumerable<MemoryRegion> EnumerateReadableRegions()
        {
            long address = 0;
            MEMORY_BASIC_INFORMATION mbi;
            int size = Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION));

            while (true)
            {
                IntPtr addrPtr = new IntPtr(address);
                if (!VirtualQueryEx(Handle, addrPtr, out mbi, size))
                    break;

                long regionSize = mbi.RegionSize.ToInt64();
                if (regionSize <= 0)
                    break;

                long baseAddr = mbi.BaseAddress.ToInt64();

                // 커밋된 영역만
                if (mbi.State == MEM_COMMIT)
                {
                    uint protect = mbi.Protect;
                    // 읽기 가능 + 실행 가능 영역 (JIT 코드 포함)
                    bool readable = (protect & PAGE_READWRITE) != 0 ||
                                    (protect & PAGE_READONLY) != 0 ||
                                    (protect & PAGE_EXECUTE_READ) != 0 ||
                                    (protect & PAGE_EXECUTE_READWRITE) != 0 ||
                                    (protect & PAGE_EXECUTE) != 0;

                    if (readable)
                    {
                        yield return new MemoryRegion
                        {
                            BaseAddress = new IntPtr(baseAddr),
                            Size = new IntPtr(regionSize)
                        };
                    }
                }

                // 다음 영역으로 이동
                long next = baseAddr + regionSize;
                if (next <= address)
                    break;
                address = next;

                // 32-bit 주소 공간 한계 (2GB user space)
                if (address > 0x7FFFFFFF)
                    break;
            }
        }

        public struct MemoryRegion
        {
            public IntPtr BaseAddress;
            public IntPtr Size;
        }

        /// <summary>
        /// osu! 프로세스를 실행 파일 이름(osu!.exe)으로 찾아서 오픈.
        /// QueryFullProcessImageName 사용 — 32-bit/64-bit 상관없이 작동.
        /// </summary>
        public bool OpenOsu()
        {
            // Process.GetProcessesByName 사용 (WOW64에서도 32-bit 프로세스 찾기 가능)
            var procs = System.Diagnostics.Process.GetProcessesByName("osu!");
            if (procs.Length == 0)
                return false;

            int pid = procs[0].Id;
            procs[0].Dispose();

            ProcessId = pid;
            Handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, ProcessId);
            return Handle != IntPtr.Zero;
        }

        /// <summary>
        /// PID로 직접 프로세스 오픈.
        /// </summary>
        public bool OpenByPid(int pid)
        {
            ProcessId = pid;
            // x64dbg가 attach 중이어도 ReadProcessMemory + VirtualQueryEx 가능해야 함
            Handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
            if (Handle == IntPtr.Zero)
                Handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (Handle == IntPtr.Zero)
                Handle = OpenProcess(PROCESS_VM_READ, false, pid);
            return Handle != IntPtr.Zero;
        }

        /// <summary>
        /// osu!.exe 모듈의 베이스 주소와 크기를 반환.
        /// </summary>
        public bool GetModuleInfo(out IntPtr baseAddress, out int moduleSize)
        {
            baseAddress = IntPtr.Zero;
            moduleSize = 0;

            if (!IsOpen) return false;

            IntPtr[] modules = new IntPtr[1024];
            int cb = modules.Length * IntPtr.Size;
            int needed;

            if (!EnumProcessModules(Handle, modules, cb, out needed))
                return false;

            int count = needed / IntPtr.Size;
            for (int i = 0; i < count; i++)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(260);
                GetModuleFileNameEx(Handle, modules[i], sb, sb.Capacity);

                if (sb.ToString().EndsWith("osu!.exe", StringComparison.OrdinalIgnoreCase))
                {
                    MODULEINFO modInfo;
                    if (GetModuleInformation(Handle, modules[i], out modInfo, Marshal.SizeOf(typeof(MODULEINFO))))
                    {
                        baseAddress = modInfo.lpBaseOfDll;
                        moduleSize = modInfo.SizeOfImage;
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// 바이트 배열 읽기 (미리 할당된 버퍼). 실패 시 false.
        /// 여러 필드를 한 번에 읽어 GetInt32/GetFloat/GetByte 등으로 추출하면
        /// ReadProcessMemory 호출 횟수를 대폭 줄일 수 있음.
        /// </summary>
        public bool ReadBytes(IntPtr address, byte[] buffer, int size)
        {
            return ReadProcessMemory(Handle, address, buffer, size, out bytesRead);
        }

        public static int GetInt32(byte[] buf, int offset)
        {
            return BitConverter.ToInt32(buf, offset);
        }

        public static uint GetUInt32(byte[] buf, int offset)
        {
            return BitConverter.ToUInt32(buf, offset);
        }

        public static float GetFloat(byte[] buf, int offset)
        {
            return BitConverter.ToSingle(buf, offset);
        }

        public static ushort GetUInt16(byte[] buf, int offset)
        {
            return BitConverter.ToUInt16(buf, offset);
        }

        public static byte GetByte(byte[] buf, int offset)
        {
            return buf[offset];
        }

        public static IntPtr GetPointer(byte[] buf, int offset)
        {
            return new IntPtr(unchecked((int)BitConverter.ToUInt32(buf, offset)));
        }

        /// <summary>
        /// 4바이트 int32 읽기.
        /// </summary>
        public bool ReadInt32(IntPtr address, out int value)
        {
            if (!ReadProcessMemory(Handle, address, buf4, 4, out bytesRead))
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToInt32(buf4, 0);
            return true;
        }

        /// <summary>
        /// 4바이트 uint32 읽기.
        /// </summary>
        public bool ReadUInt32(IntPtr address, out uint value)
        {
            if (!ReadProcessMemory(Handle, address, buf4, 4, out bytesRead))
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToUInt32(buf4, 0);
            return true;
        }

        /// <summary>
        /// 4바이트 float 읽기.
        /// </summary>
        public bool ReadFloat(IntPtr address, out float value)
        {
            if (!ReadProcessMemory(Handle, address, buf4, 4, out bytesRead))
            {
                value = 0f;
                return false;
            }
            value = BitConverter.ToSingle(buf4, 0);
            return true;
        }

        /// <summary>
        /// 8바이트 double 읽기.
        /// </summary>
        public bool ReadDouble(IntPtr address, out double value)
        {
            if (!ReadProcessMemory(Handle, address, buf8, 8, out bytesRead))
            {
                value = 0.0;
                return false;
            }
            value = BitConverter.ToDouble(buf8, 0);
            return true;
        }

        /// <summary>
        /// 2바이트 uint16 읽기.
        /// </summary>
        public bool ReadUInt16(IntPtr address, out ushort value)
        {
            if (!ReadProcessMemory(Handle, address, buf2, 2, out bytesRead))
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToUInt16(buf2, 0);
            return true;
        }

        /// <summary>
        /// 1바이트 읽기.
        /// </summary>
        public bool ReadByte(IntPtr address, out byte value)
        {
            if (!ReadProcessMemory(Handle, address, buf1, 1, out bytesRead))
            {
                value = 0;
                return false;
            }
            value = buf1[0];
            return true;
        }

        /// <summary>
        /// 32-bit 포인터 읽기 (4바이트).
        /// </summary>
        public bool ReadPointer(IntPtr address, out IntPtr value)
        {
            if (!ReadProcessMemory(Handle, address, buf4, 4, out bytesRead))
            {
                value = IntPtr.Zero;
                return false;
            }
            // unchecked: 0x80000000+ 주소가 Int32.MaxValue 초과로 오버플로 방지
            value = new IntPtr(unchecked((int)BitConverter.ToUInt32(buf4, 0)));
            return true;
        }

        /// <summary>
        /// .NET String 읽기 (32-bit CLR 레이아웃).
        /// +0x00: MethodTable (4바이트)
        /// +0x04: length (int)
        /// +0x08: UTF-16 chars
        /// </summary>
        public string ReadSharpString(IntPtr stringPtr)
        {
            if (stringPtr == IntPtr.Zero) return null;

            int length;
            if (!ReadInt32(stringPtr + 0x4, out length))
                return null;

            if (length <= 0 || length > 65536)
                return null;

            byte[] buf = new byte[length * 2];
            if (!ReadBytes(stringPtr + 0x8, buf, length * 2))
                return null;

            return System.Text.Encoding.Unicode.GetString(buf);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                CloseHandle(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }
}