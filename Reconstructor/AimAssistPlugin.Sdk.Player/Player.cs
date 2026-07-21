using AimAssistPlugin.Services;

namespace AimAssistPlugin.Sdk.Player;

internal class Player
{
    // OsuEnlightenOverlay2에서 Mode==Play && AudioState==Playing일 때만 IsPlaying=1로 브로드캐스트.
    // 이전 TosuService.LatestResponse?.state.name == "play" 와 동일한 의미.
    public static bool IsPlaying => (EnlightenService.LatestState?.IsPlaying ?? 0) == 1;
}
