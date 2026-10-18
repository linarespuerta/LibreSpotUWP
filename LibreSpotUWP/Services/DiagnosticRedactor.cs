using System.Text.RegularExpressions;

namespace LibreSpotUWP.Services
{
    internal static class DiagnosticRedactor
    {
        private static readonly Regex JsonSecretRegex = new Regex("(\"(?:access[_-]?token|refresh[_-]?token|client[_-]?secret|authorization|auth[_-]?blob|audio[_-]?key|private[_-]?key|code)\"\\s*:\\s*\")[^\"]*(\")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex KeyValueSecretRegex = new Regex("\\b((?:access[_-]?token|refresh[_-]?token|client[_-]?secret|authorization|auth[_-]?blob|audio[_-]?key|private[_-]?key|code)\\s*[:=]\\s*)[^&\\s,;}\\]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BearerTokenRegex = new Regex("\\bBearer\\s+[A-Za-z0-9._~+/-]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex WindowsUserPathRegex = new Regex("C:\\\\Users\\\\[^\\\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SpotifyAuthenticatedAsRegex = new Regex("(Authenticated as ')[^']+(')", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SpotifyConnectedUserRegex = new Regex("(Connected as user:\\s*)\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Redact(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            message = JsonSecretRegex.Replace(message, "$1[redacted]$2");
            message = BearerTokenRegex.Replace(message, "Bearer [redacted]");
            message = KeyValueSecretRegex.Replace(message, "$1[redacted]");
            message = WindowsUserPathRegex.Replace(message, "C:\\Users\\[user]");
            message = SpotifyAuthenticatedAsRegex.Replace(message, "$1[redacted]$2");
            return SpotifyConnectedUserRegex.Replace(message, "$1[redacted]");
        }
    }
}
