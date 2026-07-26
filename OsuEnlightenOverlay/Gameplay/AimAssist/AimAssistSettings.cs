namespace OsuEnlightenOverlay.Gameplay.AimAssist
{
    /// <summary>Reconstructor AimAssistSettings와 동일 기본값/의미.</summary>
    internal static class AimAssistSettings
    {
        public static float Strength = 1.6f;
        public static float Range = 4.0f;
        public static float Curviness = 0.6f;
        public static float MaxOffset = 70f;
        public static float AttackInertia = 100f;
        public static float ReleaseInertia = 15f;
        public static float DeadZone = 0.5f;
        public static float IdleGateWindow = 50f;
        public static float IdleThreshold = 3f;
        public static float ResyncFactor = 0.6f;
    }
}
