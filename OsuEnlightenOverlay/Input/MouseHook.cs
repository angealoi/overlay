using System;
using System.Runtime.InteropServices;
using System.Threading;
using OsuEnlightenOverlay.Overlay;

namespace OsuEnlightenOverlay.Input
{
    /// <summary>
    /// WH_MOUSE_LL hook — lame c_mouse_hook 포팅.
    /// 전용 스레드에서 메시지 펌프. transform 성공 시 원본 move를 삼킨다.
    /// </summary>
    internal sealed class MouseHook : IDisposable
    {
        public delegate bool TransformFn(WindowInterop.POINT pt, out WindowInterop.POINT result);

        const int WH_MOUSE_LL = 14;
        const int WM_MOUSEMOVE = 0x0200;
        const int HC_ACTION = 0;

        // MSLLHOOKSTRUCT.flags
        const uint LLMHF_INJECTED = 0x00000001;

        // Windows tablet pen/touch — MSDN "System Events and Mouse Messages"
        const uint MI_WP_SIGNATURE = 0xFF515700;
        const uint SIGNATURE_MASK = 0xFFFFFF00;

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public WindowInterop.POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll")]
        static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentThread();

        const int THREAD_PRIORITY_HIGHEST = 2;
        const uint WM_QUIT = 0x0012;

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public WindowInterop.POINT pt;
        }

        TransformFn _transform;
        Thread _thread;
        volatile bool _stop;
        volatile bool _ready;
        volatile bool _installed;
        uint _threadId;
        IntPtr _hook = IntPtr.Zero;
        LowLevelMouseProc _proc; // GC pin

        static MouseHook _active;

        public bool Installed { get { return _installed; } }

        public void SetTransform(TransformFn fn)
        {
            _transform = fn;
        }

        public bool Install()
        {
            if (_installed) return true;
            _stop = false;
            _ready = false;
            _thread = new Thread(HookThreadMain);
            _thread.IsBackground = true;
            _thread.Name = "MouseAimLL";
            _thread.Start();

            for (int i = 0; i < 200 && !_ready; i++)
                Thread.Sleep(5);

            return _installed;
        }

        public void Uninstall()
        {
            _stop = true;
            if (_threadId != 0)
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            if (_thread != null && _thread.IsAlive)
                _thread.Join(2000);

            _installed = false;
            _ready = false;
            _threadId = 0;
            _hook = IntPtr.Zero;
            if (_active == this)
                _active = null;
        }

        public void Dispose()
        {
            Uninstall();
        }

        IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode != HC_ACTION || lParam == IntPtr.Zero)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            var ms = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            uint extra = unchecked((uint)ms.dwExtraInfo.ToUInt64());

            // 우리가 SendInput 한 커서 — 재진입 방지
            if (extra == MouseInput.InjectExtraInfo)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            // 타블렛/펜/터치 또는 외부 inject(OTD Absolute SendInput 등) — mouse aim 강제 비활성
            bool fromPenOrTouch = (extra & SIGNATURE_MASK) == MI_WP_SIGNATURE;
            bool foreignInject = (ms.flags & LLMHF_INJECTED) != 0;
            if (fromPenOrTouch || foreignInject)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            if (_transform != null && (int)wParam == WM_MOUSEMOVE)
            {
                WindowInterop.POINT outPt;
                if (_transform(ms.pt, out outPt))
                    return (IntPtr)1; // eat original move
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        void HookThreadMain()
        {
            SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);
            _threadId = GetCurrentThreadId();
            _active = this;
            _proc = LowLevelProc;

            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                if (_active == this) _active = null;
                _installed = false;
                _ready = true;
                return;
            }

            _installed = true;
            _ready = true;

            MSG msg;
            while (!_stop && GetMessage(out msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            if (_active == this) _active = null;
            _installed = false;
        }
    }
}
