using System;
using System.Drawing;
using OsuEnlightenOverlay.ControlPanel;
using OsuEnlightenOverlay.Rendering;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// HUD edit 모드 입력 처리 — 드래그 이동 + 키보드 단축키.
    ///
    /// 폴링(GetAsyncKeyState/GetCursorPos)을 쓰는 이유: 자식 GLControl 창이
    /// 마우스/키 메시지를 가로채므로 OverlayForm.WndProc으로는 잡을 수 없음.
    ///
    /// 창(Handle)과 클라이언트 크기는 프레임마다 주입받는다 — 이 클래스는
    /// Form을 몰라야 오버레이 창 수명주기와 독립적으로 테스트/재사용 가능.
    /// </summary>
    internal class HudEditController
    {
        readonly OverlaySettings settings;
        readonly HudRenderer hudRenderer;
        readonly OverlayEditState editState = new OverlayEditState();

        // 단축키 엣지 검출용 래치.
        bool s_tab, s_up, s_down, s_esc, s_s, s_x, s_y;
        bool lbuttonPrev;

        /// <summary>HudRenderer가 하이라이트 렌더링에 참조하는 편집 상태.</summary>
        public OverlayEditState State { get { return editState; } }

        public HudEditController(OverlaySettings settings, HudRenderer hudRenderer)
        {
            this.settings = settings;
            this.hudRenderer = hudRenderer;
        }

        /// <summary>
        /// edit 모드 한 프레임 처리 — HudEditMode일 때만 호출할 것.
        /// </summary>
        public void Update(IntPtr overlayHandle, Size clientSize)
        {
            HandleShortcuts();
            HandleMouse(overlayHandle, clientSize);
        }

        // ── edit 모드 마우스 폴링 (NEWNEWOVERLAY WndProc 드래그 대응) ──
        void HandleMouse(IntPtr overlayHandle, Size clientSize)
        {
            bool lbuttonDown = (WindowInterop.GetAsyncKeyState(WindowInterop.VK_LBUTTON) & 0x8000) != 0;

            if (!editState.Dragging)
            {
                // 버튼 누름 엣지 — HUD 위에서 클릭 시 드래그 시작.
                bool wasDown = lbuttonPrev;
                lbuttonPrev = lbuttonDown;
                if (lbuttonDown && !wasDown)
                    TryStartDrag(overlayHandle);
            }
            else
            {
                lbuttonPrev = lbuttonDown;
                if (lbuttonDown)
                {
                    // 드래그 진행.
                    UpdateDragPosition(ClientCursorPoint(overlayHandle), clientSize);
                }
                else
                {
                    // 버튼 뗌 — 드래그 종료.
                    UpdateDragPosition(ClientCursorPoint(overlayHandle), clientSize);
                    editState.Reset();
                }
            }

            // HUD 위면 크기조정 커서 표시.
            int hit = editState.Dragging ? editState.DragElement : HitTestHud(ClientCursorPoint(overlayHandle));
            if (hit >= 0)
                WindowInterop.SetCursor(WindowInterop.IDC_SIZEALL_Handle);
        }

        // 화면 좌표 → 오버레이 클라이언트 좌표.
        Point ClientCursorPoint(IntPtr overlayHandle)
        {
            WindowInterop.POINT p;
            WindowInterop.GetCursorPos(out p);
            WindowInterop.MapWindowPoints(IntPtr.Zero, overlayHandle, ref p, 1);
            return new Point(p.X, p.Y);
        }

        void TryStartDrag(IntPtr overlayHandle)
        {
            Point pt = ClientCursorPoint(overlayHandle);
            int element = HitTestHud(pt);
            if (element < 0) return;

            settings.HudEditSelected = element;
            settings.HudUseCustomPos[element] = true;

            RectangleF r = hudRenderer.HudRects[element];
            editState.Dragging = true;
            editState.DragElement = element;
            editState.DragOffsetX = pt.X - r.X;
            editState.DragOffsetY = pt.Y - r.Y;
            editState.DragStartX = r.X;
            editState.DragStartY = r.Y;
        }

        // ── edit 모드 마우스 히트테스트 (NEWNEWOVERLAY HitTestHud) ──
        // HudRects를 뒤→앞 순회, 6px 패딩. 히트 시 인덱스, 아니면 -1.
        int HitTestHud(Point clientPoint)
        {
            if (hudRenderer == null) return -1;
            RectangleF[] rects = hudRenderer.HudRects;
            float pad = EditConstants.HighlightPad;
            for (int i = 3; i >= 0; i--)
            {
                RectangleF r = rects[i];
                if (r.Width <= 0.0f) continue;  // 미렌더링(0폭 default rect 포함, I-감사 #23)
                if (clientPoint.X >= r.X - pad && clientPoint.X <= r.Right + pad &&
                    clientPoint.Y >= r.Y - pad && clientPoint.Y <= r.Bottom + pad)
                    return i;
            }
            return -1;
        }

        // ── 드래그 위치 업데이트 (NEWNEWOVERLAY UpdateDragPosition) ──
        // axis-lock + center-snap + 클라이언트 영역 clamp.
        void UpdateDragPosition(Point clientPoint, Size clientSize)
        {
            if (editState.DragElement < 0) return;

            float clientW = clientSize.Width;
            float clientH = clientSize.Height;
            RectangleF r = hudRenderer.HudRects[editState.DragElement];
            float hudW = r.Width;
            float hudH = r.Height;
            float maxX = Math.Max(0.0f, clientW - hudW);
            float maxY = Math.Max(0.0f, clientH - hudH);

            float posX = clientPoint.X - editState.DragOffsetX;
            float posY = clientPoint.Y - editState.DragOffsetY;

            // axis lock: 비주축을 lock 시작 시 캡처한 HUD 중심에 고정.
            // X = 수평 전용(y 고정), Y = 수직 전용(x 고정).
            if (editState.Lock == OverlayEditState.AxisLock.Horizontal)
                posY = editState.LockCenterY - hudH * 0.5f;
            else if (editState.Lock == OverlayEditState.AxisLock.Vertical)
                posX = editState.LockCenterX - hudW * 0.5f;

            // center snap: HUD의 시각적 중심이 화면 수평 중심에 정렬되도록 흡착 (x 축만).
            if (settings.HudEditSnap)
            {
                float centerX = clientW * 0.5f - hudW * 0.5f;
                if (Math.Abs(posX - centerX) < EditConstants.SnapThreshold)
                    posX = centerX;
            }

            posX = Math.Max(0.0f, Math.Min(posX, maxX));
            posY = Math.Max(0.0f, Math.Min(posY, maxY));

            int i = editState.DragElement;
            settings.HudUseCustomPos[i] = true;
            // 정규화 좌표(0.0~1.0)로 저장 — 모든 해상도에서 호환
            settings.HudPositionX[i] = (clientW > 0) ? posX / clientW : 0;
            settings.HudPositionY[i] = (clientH > 0) ? posY / clientH : 0;
        }

        // ── edit 모드 키보드 단축키 (NEWNEWOVERLAY HandleEditShortcuts) ──
        // GetAsyncKeyState 폴링 + bool 래치로 엣지 검출.
        void HandleShortcuts()
        {
            if (!settings.HudEditMode) return;

            bool KeyDown(int vk) { return (WindowInterop.GetAsyncKeyState(vk) & 0x8000) != 0; }
            bool KeyPressed(int vk, ref bool prev)
            {
                bool down = (WindowInterop.GetAsyncKeyState(vk) & 0x8000) != 0;
                bool pressed = down && !prev;
                prev = down;
                return pressed;
            }

            // 기본 선택 — 단축키 즉시 동작을 위해 첫 활성 HUD 자동 선택.
            void EnsureSelection()
            {
                int sel = settings.HudEditSelected;
                if (sel >= 0 && sel < 4 && settings.HudEnabled[sel]) return;
                sel = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (settings.HudEnabled[i]) { sel = i; break; }
                }
                settings.HudEditSelected = sel;
            }
            EnsureSelection();

            bool shift = KeyDown(WindowInterop.VK_SHIFT);

            // Tab: 활성 HUD 순환 선택 (Shift = 역방향).
            if (KeyPressed(WindowInterop.VK_TAB, ref s_tab))
            {
                int dir = shift ? -1 : 1;
                int sel = settings.HudEditSelected;
                for (int step = 0; step < 4; step++)
                {
                    sel = (sel + dir + 4) % 4;
                    if (settings.HudEnabled[sel])
                    {
                        settings.HudEditSelected = sel;
                        break;
                    }
                }
            }

            // Up/Down: 선택 HUD 폰트 크기 조정 (Shift = ×10).
            int fontStep = shift ? EditConstants.FontSizeShiftStep : EditConstants.FontSizeStep;
            if (KeyPressed(WindowInterop.VK_UP, ref s_up) && settings.HudEditSelected >= 0)
            {
                int i = settings.HudEditSelected;
                settings.HudFontSizes[i] = Math.Min(settings.HudFontSizes[i] + fontStep, EditConstants.FontSizeMax);
            }
            if (KeyPressed(WindowInterop.VK_DOWN, ref s_down) && settings.HudEditSelected >= 0)
            {
                int i = settings.HudEditSelected;
                settings.HudFontSizes[i] = Math.Max(settings.HudFontSizes[i] - fontStep, EditConstants.FontSizeMin);
            }

            // Esc: edit 모드 종료.
            if (KeyPressed(WindowInterop.VK_ESCAPE, ref s_esc))
            {
                settings.HudEditMode = false;
            }

            // S: center snap 토글. X/Y: axis lock 토글(sticky, 재누름 시 해제).
            if (KeyPressed((int)'S', ref s_s))
            {
                settings.HudEditSnap = !settings.HudEditSnap;
            }
            // lock 시작 시 HUD의 현재 중심 캡처 — 드래그 시작 모서리가 아닌 실제 위치 기준.
            void CaptureLockCenter()
            {
                if (editState.DragElement < 0) return;
                RectangleF r = hudRenderer.HudRects[editState.DragElement];
                editState.LockCenterX = r.X + r.Width * 0.5f;
                editState.LockCenterY = r.Y + r.Height * 0.5f;
            }
            if (KeyPressed((int)'X', ref s_x))
            {
                bool engaging = editState.Lock != OverlayEditState.AxisLock.Horizontal;
                editState.Lock = engaging ? OverlayEditState.AxisLock.Horizontal
                                          : OverlayEditState.AxisLock.None;
                if (engaging) CaptureLockCenter();
            }
            if (KeyPressed((int)'Y', ref s_y))
            {
                bool engaging = editState.Lock != OverlayEditState.AxisLock.Vertical;
                editState.Lock = engaging ? OverlayEditState.AxisLock.Vertical
                                          : OverlayEditState.AxisLock.None;
                if (engaging) CaptureLockCenter();
            }
        }
    }
}
