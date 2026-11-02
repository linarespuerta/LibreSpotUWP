namespace LibreSpotUWP.Services
{
    // Re-arm the platform renderer after app resume, not inside the source's
    // Paused callback (which can run immediately before system suspension).
    internal static class PlatformSourcePauseRecoveryPolicy
    {
        public static bool ShouldRearmAfterResume(bool platformBackend, bool playbackRequested, bool disposed) =>
            platformBackend && playbackRequested && !disposed;
    }
}
