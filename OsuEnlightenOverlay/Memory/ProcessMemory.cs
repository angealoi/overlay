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

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, int dwLength);

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

        public IntPtr Handle { get; private set; }
        public int ProcessId { get; private set; }
        public bool IsOpen { get { return Handle != IntPtr.Zero; } }

        // 재사용 버퍼 — 매번 new byte[] 할당 방지.
        // [ThreadStatic]로 스레드마다 독립 버퍼를 준다: 여러 스레드가 동시에 Read*를 불러도
        // 서로의 버퍼를 덮어쓰지 않는다 (E1 — 잠복 결함). 현재는 모든 호출이 UI 스레드지만,
        // 누가 Task에서 Read* 하나만 불러도 조용히 값이 섞이던 위험을 구조적으로 제거한다.
        // (인스턴스 필드엔 [ThreadStatic]가 안 먹으므로 static + 지연 초기화 — AobScanner의
        //  sharedBuffer와 동일 패턴. out 카운트(bytesRead)는 각 메서드의 로컬로 옮겼다.)
        [ThreadStatic] static byte[] tBuf4;
        [ThreadStatic] static byte[] tBuf2;
        [ThreadStatic] static byte[] tBuf1;
        [ThreadStatic] static byte[] tBuf8;
        static byte[] Buf4 { get { return tBuf4 ?? (tBuf4 = new byte[4]); } }
        static byte[] Buf2 { get { return tBuf2 ?? (tBuf2 = new byte[2]); } }
        static byte[] Buf1 { get { return tBuf1 ?? (tBuf1 = new byte[1]); } }
        static byte[] Buf8 { get { return tBuf8 ?? (tBuf8 = new byte[8]); } }

        /// <summary>
        /// 커밋된 **실행 가능** 영역만 순회 — AOB 코드 시그니처 스캔 전용.
        /// x86 코드 패턴은 실행 가능 페이지에만 존재하므로 데이터/GC 힙(읽기 전용·읽기쓰기)은
        /// 훑을 이유가 없다. 실측(osu! 809MB): 읽기 가능 전부 대비 실행 가능은 31%로,
        /// 모든 시그니처 매치가 이 영역 안에서 그대로 발견됨을 확인함(누락 0).
        /// </summary>
        public IEnumerable<MemoryRegion> EnumerateExecutableRegions()
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

                // 커밋된 실행 가능 영역만 (JIT 코드 + ngen 이미지)
                if (mbi.State == MEM_COMMIT)
                {
                    uint protect = mbi.Protect;
                    bool executable = (protect & PAGE_EXECUTE_READ) != 0 ||
                                      (protect & PAGE_EXECUTE_READWRITE) != 0 ||
                                      (protect & PAGE_EXECUTE) != 0;

                    if (executable)
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
        /// 바이트 배열 읽기 (미리 할당된 버퍼). 실패 시 false.
        /// 여러 필드를 한 번에 읽어 GetInt32/GetFloat/GetByte 등으로 추출하면
        /// ReadProcessMemory 호출 횟수를 대폭 줄일 수 있음.
        /// </summary>
        public bool ReadBytes(IntPtr address, byte[] buffer, int size)
        {
            int read;
            return ReadProcessMemory(Handle, address, buffer, size, out read);
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
            byte[] b = Buf4;
            int read;
            if (!ReadProcessMemory(Handle, address, b, 4, out read))
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToInt32(b, 0);
            return true;
        }

        /// <summary>
        /// 4바이트 uint32 읽기.
        /// </summary>
        public bool ReadUInt32(IntPtr address, out uint value)
        {
            byte[] b = Buf4;
            int read;
            if (!ReadProcessMemory(Handle, address, b, 4, out read))
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToUInt32(b, 0);
            return true;
        }

        /// <summary>
        /// 4바이트 float 읽기.
        /// </summary>
        public bool ReadFloat(IntPtr address, out float value)
        {
            byte[] b = Buf4;
            int read;
            if (!ReadProcessMemory(Handle, address, b, 4, out read))
            {
                value = 0f;
                return false;
            }
            value = BitConverter.ToSingle(b, 0);
            return true;
        }

        /// <summary>
        /// 8바이트 double 읽기.
        /// </summary>
        public bool ReadDouble(IntPtr address, out double value)
        {
            byte[] b = Buf8;
            int read;
            if (!ReadProcessMemory(Handle, address, b, 8, out read))
            {
                value = 0.0;
                return false;
            }
            value = BitConverter.ToDouble(b, 0);
            return true;
        }

        /// <summary>
        /// 2바이트 uint16 읽기.
        /// </summary>
        public bool ReadUInt16(IntPtr address, out ushort value)
        {
            byte[] b = Buf2;
            int read;
            if (!ReadProcessMemory(Handle, address, b, 2, out read))
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToUInt16(b, 0);
            return true;
        }

        /// <summary>
        /// 1바이트 읽기.
        /// </summary>
        public bool ReadByte(IntPtr address, out byte value)
        {
            byte[] b = Buf1;
            int read;
            if (!ReadProcessMemory(Handle, address, b, 1, out read))
            {
                value = 0;
                return false;
            }
            value = b[0];
            return true;
        }

        /// <summary>
        /// 32-bit 포인터 읽기 (4바이트).
        /// </summary>
        public bool ReadPointer(IntPtr address, out IntPtr value)
        {
            byte[] b = Buf4;
            int read;
            if (!ReadProcessMemory(Handle, address, b, 4, out read))
            {
                value = IntPtr.Zero;
                return false;
            }
            // unchecked: 0x80000000+ 주소가 Int32.MaxValue 초과로 오버플로 방지
            value = new IntPtr(unchecked((int)BitConverter.ToUInt32(b, 0)));
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