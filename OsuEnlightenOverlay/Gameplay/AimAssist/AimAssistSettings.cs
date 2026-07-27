namespace OsuEnlightenOverlay.Gameplay.AimAssist
{
    /// <summary>마우스 aim assist 설정 — Release/Resync는 태블릿 절대좌표용이므로 제외.</summary>
    internal static class AimAssistSettings
    {
        public static float Strength = 1.6f;
        public static float Range = 4.0f;
        public static float Curviness = 0.6f;
        public static float MaxOffset = 70f;
        public static float AttackInertia = 100f;
        public static float DeadZone = 0.5f;
        public static float IdleGateWindow = 50f;
        public static float IdleThreshold = 3f;
    }
}
