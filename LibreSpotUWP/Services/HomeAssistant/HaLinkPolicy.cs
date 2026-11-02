using System;

namespace LibreSpotUWP.Services.HomeAssistant
{
    internal enum HaLinkStatus
    {
        Disabled,
        NotConfigured,
        Connecting,
        Connected,
        // Retrying after a network failure or a recoverable close.
        Waiting,
        // The secret was refused. No retries until the settings change.
        AuthFailed,
        // Home Assistant refused this app's protocol or identity. No retries until the settings change or an update.
        Rejected
    }

    internal enum HaLinkFailure
    {
        None,
        Network,
        Unauthorized,
        Replaced,
        UnsupportedVersion,
        HelloRequired,
        TooLarge,
        InvalidIdentity,
        ClosedByServer
    }

    // Connection decisions that need no WinRT: close-code classification, retry policy, backoff,
    // and validation of what the user types in Settings.
    internal static class HaLinkPolicy
    {
        public const string LinkPath = "/api/lumia_hifi/v1/link";
        public const int MinimumSecretLength = 20;
        public const int MaximumSecretLength = 200;
        public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);
        // A replaced link means another client holds the same secret; retrying fast would make both flap.
        public static readonly TimeSpan ReplacedRetryDelay = TimeSpan.FromMinutes(1);

        public static HaLinkFailure ClassifyClose(int code)
        {
            switch (code)
            {
                case 4001: return HaLinkFailure.Replaced;
                case 4002: return HaLinkFailure.UnsupportedVersion;
                case 4003: return HaLinkFailure.HelloRequired;
                case 4004: return HaLinkFailure.TooLarge;
                case 4005: return HaLinkFailure.InvalidIdentity;
                default: return HaLinkFailure.ClosedByServer;
            }
        }

        public static bool ShouldRetry(HaLinkFailure failure)
        {
            switch (failure)
            {
                case HaLinkFailure.Unauthorized:
                case HaLinkFailure.UnsupportedVersion:
                case HaLinkFailure.HelloRequired:
                case HaLinkFailure.InvalidIdentity:
                    return false;
                default:
                    return true;
            }
        }

        public static HaLinkStatus StatusAfter(HaLinkFailure failure)
        {
            if (failure == HaLinkFailure.Unauthorized) return HaLinkStatus.AuthFailed;
            return ShouldRetry(failure) ? HaLinkStatus.Waiting : HaLinkStatus.Rejected;
        }

        // Exponential backoff from 1 s to 5 min with +/-20 % jitter. `attempt` counts failures since the
        // last successful hello; `jitter` is a random value in [0, 1).
        public static TimeSpan RetryDelay(int attempt, double jitter, HaLinkFailure failure = HaLinkFailure.Network)
        {
            if (attempt < 0) attempt = 0;
            if (jitter < 0 || jitter >= 1 || double.IsNaN(jitter)) jitter = 0.5;
            var seconds = FirstRetryDelay.TotalSeconds * Math.Pow(2, Math.Min(attempt, 20));
            seconds = Math.Min(seconds, MaximumRetryDelay.TotalSeconds);
            if (failure == HaLinkFailure.Replaced) seconds = Math.Max(seconds, ReplacedRetryDelay.TotalSeconds);
            seconds *= 0.8 + 0.4 * jitter;
            return TimeSpan.FromSeconds(Math.Min(seconds, MaximumRetryDelay.TotalSeconds * 1.2));
        }

        public static string Describe(HaLinkStatus status, HaLinkFailure failure)
        {
            switch (status)
            {
                case HaLinkStatus.Disabled: return "Off.";
                case HaLinkStatus.NotConfigured: return "Enter the address and secret shown by Home Assistant.";
                case HaLinkStatus.Connecting: return "Connecting...";
                case HaLinkStatus.Connected: return "Connected.";
                case HaLinkStatus.AuthFailed: return "Home Assistant rejected the secret. Enter the current one.";
                case HaLinkStatus.Rejected:
                    switch (failure)
                    {
                        case HaLinkFailure.UnsupportedVersion: return "Home Assistant uses a different protocol version. Update the integration or the app.";
                        case HaLinkFailure.InvalidIdentity: return "Home Assistant rejected this device's identity. Check the device name.";
                        default: return "Home Assistant closed the link because of a protocol error.";
                    }
                default:
                    switch (failure)
                    {
                        case HaLinkFailure.Replaced: return "Another device connected with the same secret. Retrying later.";
                        case HaLinkFailure.Network: return "Home Assistant is unreachable. Retrying.";
                        default: return "Disconnected. Retrying.";
                    }
            }
        }

        // Accepts what the config flow shows (ws://host:8123/api/lumia_hifi/v1/link) and the forms people
        // tend to type instead: an http(s) base URL, or a host and port without the path.
        public static bool TryNormalizeUrl(string text, out string normalized, out string error)
        {
            normalized = null;
            error = null;
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0) { error = "Enter the address shown by Home Assistant."; return false; }
            if (value.IndexOf("://", StringComparison.Ordinal) < 0) value = "ws://" + value;

            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host))
            {
                error = "The address is not valid.";
                return false;
            }

            string scheme;
            switch (uri.Scheme.ToLowerInvariant())
            {
                case "ws":
                case "http":
                    scheme = "ws";
                    break;
                case "wss":
                case "https":
                    scheme = "wss";
                    break;
                default:
                    error = "Use a ws://, wss://, http:// or https:// address.";
                    return false;
            }
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "The address must not contain a user name, query or fragment.";
                return false;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.Length == 0) path = LinkPath;
            else if (!string.Equals(path, LinkPath, StringComparison.Ordinal))
            {
                error = "The address path must be " + LinkPath + ".";
                return false;
            }

            var builder = new UriBuilder(scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port, path);
            normalized = builder.Uri.AbsoluteUri;
            return true;
        }

        public static bool TryNormalizeSecret(string text, out string normalized, out string error)
        {
            normalized = null;
            error = null;
            var value = (text ?? string.Empty).Trim();
            if (value.Length < MinimumSecretLength || value.Length > MaximumSecretLength)
            {
                error = "The secret should be the long code shown by Home Assistant.";
                return false;
            }
            foreach (var character in value)
            {
                if (character <= ' ' || character > '~')
                {
                    error = "The secret contains characters Home Assistant never generates.";
                    return false;
                }
            }
            normalized = value;
            return true;
        }
    }
}
