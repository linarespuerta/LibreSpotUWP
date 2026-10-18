using System;

namespace LibreSpotUWP.Services
{
    internal static class PlaybackRecoveryAuthorizationPolicy
    {
        public static bool CanUseOwnerAuthorization(string ownerAccountId, string activeSessionUser)
        {
            return !string.IsNullOrWhiteSpace(ownerAccountId) &&
                !string.IsNullOrWhiteSpace(activeSessionUser) &&
                string.Equals(ownerAccountId, activeSessionUser, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldResumeSavedPlayback(
            bool wasPlaying,
            string snapshotOwnerAccountId,
            string currentOwnerAccountId)
        {
            return wasPlaying && CanUseOwnerAuthorization(snapshotOwnerAccountId, currentOwnerAccountId);
        }
    }
}
