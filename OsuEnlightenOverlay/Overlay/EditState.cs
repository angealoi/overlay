using System;

namespace OsuEnlightenOverlay.Overlay
{
    /// <summary>
    /// HUD edit-layout 드래그 상태 — NEWNEWOVERLAY edit_state.h 포팅.
    /// WndProc(드래그 처리)와 단축키 핸들러가 공유. 단일 인스턴스 사용.
    /// </summary>
    internal class OverlayEditState
    {
        public enum AxisLock { None, Horizontal, Vertical }

        public bool Dragging = false;
        public int DragElement = -1;       // 드래그 중인 HUD 인덱스 (0..3)
        public float DragOffsetX = 0.0f;   // 드래그 시작 시 커서→HUD 좌상단 오프셋
        public float DragOffsetY = 0.0f;
        public float DragStartX = 0.0f;    // 드래그 시작 시 HUD 좌상단 (axis-lock 기준점)
        public float DragStartY = 0.0f;

        public AxisLock Lock = AxisLock.None;
        public float LockCenterX = 0.0f;   // axis lock 시작 시 캡처한 HUD 중심
        public float LockCenterY = 0.0f;

        public void Reset()
        {
            Dragging = false;
            DragElement = -1;
            DragOffsetX = DragOffsetY = 0.0f;
            DragStartX = DragStartY = 0.0f;
        }
    }
}
