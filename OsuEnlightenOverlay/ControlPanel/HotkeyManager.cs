using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OsuEnlightenOverlay.ControlPanel
{
    /// <summary>
    /// 핫키 관리 — RegisterHotKey/UnregisterHotKey.
    /// F9: 패널 토글, F10: 종료.
    /// </summary>
    public class HotkeyManager : IDisposable
    {
        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int WM_HOTKEY = 0x0312;
        const uint VK_F9 = 0x78;
        const uint VK_F10 = 0x79;
        const int ID_F9 = 9001;
        const int ID_F10 = 9002;

        IntPtr handle;
        public Action OnTogglePanel;
        public Action OnExit;

        public HotkeyManager(IntPtr handle)
        {
            this.handle = handle;
        }

        public void Register()
        {
            RegisterHotKey(handle, ID_F9, 0, VK_F9);
            RegisterHotKey(handle, ID_F10, 0, VK_F10);
        }

        public void Unregister()
        {
            UnregisterHotKey(handle, ID_F9);
            UnregisterHotKey(handle, ID_F10);
        }

        /// <summary>
        /// WndProc에서 호출 — WM_HOTKEY 처리.
        /// </summary>
        public bool ProcessMessage(Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == ID_F9)
                {
                    if (OnTogglePanel != null) OnTogglePanel();
                    return true;
                }
                if (id == ID_F10)
                {
                    if (OnExit != null) OnExit();
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            Unregister();
        }
    }
}