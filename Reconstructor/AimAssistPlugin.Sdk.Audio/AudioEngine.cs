using AimAssistPlugin.Services;

namespace AimAssistPlugin.Sdk.Audio;

internal class AudioEngine
{
    // OsuEnlightenOverlay2의 reader.TimeMs — 오디오 재생 시간(ms).
    // 이전 TosuService.LatestPreciseResponse?.currentTime 과 동일.
    public static int GetTime()
    {
        return EnlightenService.LatestState?.TimeMs ?? 0;
    }
}
