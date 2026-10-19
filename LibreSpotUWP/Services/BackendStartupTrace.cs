using System;
using System.IO;
using System.Text;

namespace LibreSpotUWP.Services
{
    // Rare startup/switch checkpoints, not playback telemetry. Flush each line
    // before the following native/WinRT call so abrupt termination cannot hide it
    // behind LogService's asynchronous batch queue.
    internal sealed class BackendStartupTrace
    {
        internal const int MaximumBytes = 64 * 1024;
        private readonly object _sync = new object();
        private readonly string _path;
        private readonly string _launch;
        private long _sequence;

        public BackendStartupTrace(string path, string launch)
        {
            _path = path;
            _launch = launch;
        }

        public bool Write(string stage, Exception failure = null)
        {
            // Only code-owned identifiers are accepted. Never persist arbitrary
            // exception messages, device IDs, URLs, credentials or PCM here.
            if (string.IsNullOrEmpty(stage) || stage.Length > 96) return false;
            foreach (char c in stage)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-')) return false;
            try
            {
                lock (_sync)
                {
                    var line = DateTimeOffset.UtcNow.ToString("O") + " launch=" + _launch +
                        " sequence=" + (++_sequence) + " stage=" + stage;
                    if (failure != null)
                        line += " error=" + failure.GetType().FullName + " HRESULT=0x" + failure.HResult.ToString("X8");
                    var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                    using (var file = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                    {
                        if (file.Length + bytes.Length > MaximumBytes) file.SetLength(0);
                        file.Seek(0, SeekOrigin.End);
                        file.Write(bytes, 0, bytes.Length);
                        file.Flush(true);
                    }
                }
                return true;
            }
            catch { return false; } // Diagnostic failure must not break playback.
        }
    }
}
