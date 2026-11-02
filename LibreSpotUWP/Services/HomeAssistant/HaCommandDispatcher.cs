using System;
using System.Threading.Tasks;

namespace LibreSpotUWP.Services.HomeAssistant
{
    // What the link may do to playback. The app implements it over IMediaService, using the same
    // methods as the lock-screen media controls, so Home Assistant adds no new playback path.
    internal interface IHaPlaybackControl
    {
        Task PlayAsync();
        Task PauseAsync();
        void Next();
        void Previous();
        void Seek(uint positionMs);
        Task SetVolumeAsync(ushort volume);
        Task SetShuffleAsync(bool enabled);
        Task SetRepeatAsync(int mode);
    }

    internal static class HaCommandDispatcher
    {
        // Home Assistant gives up after 5 s; answer "busy" before that so it gets a real reason.
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

        // Returns null on success, otherwise the ack error code ("busy" or "failed").
        public static async Task<string> ExecuteAsync(IHaPlaybackControl control, HaCommand command, TimeSpan timeout, Action<string> log = null)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (command == null) throw new ArgumentNullException(nameof(command));
            Task work;
            try { work = Start(control, command); }
            catch (Exception ex)
            {
                log?.Invoke("Home Assistant command " + command.Kind + " failed: HRESULT=0x" + ex.HResult.ToString("X8") + ".");
                return "failed";
            }

            var finished = await Task.WhenAny(work, Task.Delay(timeout)).ConfigureAwait(false);
            if (finished != work)
            {
                log?.Invoke("Home Assistant command " + command.Kind + " did not finish in " + timeout.TotalSeconds + " s.");
                ObserveLater(work, log, command.Kind);
                return "busy";
            }
            try
            {
                await work.ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                log?.Invoke("Home Assistant command " + command.Kind + " failed: HRESULT=0x" + ex.HResult.ToString("X8") + ".");
                return "failed";
            }
        }

        private static Task Start(IHaPlaybackControl control, HaCommand command)
        {
            switch (command.Kind)
            {
                case HaCommandKind.Play: return control.PlayAsync() ?? Task.CompletedTask;
                case HaCommandKind.Pause: return control.PauseAsync() ?? Task.CompletedTask;
                case HaCommandKind.Next: control.Next(); return Task.CompletedTask;
                case HaCommandKind.Previous: control.Previous(); return Task.CompletedTask;
                case HaCommandKind.Seek: control.Seek(command.PositionMs); return Task.CompletedTask;
                case HaCommandKind.VolumeSet: return control.SetVolumeAsync(command.Volume) ?? Task.CompletedTask;
                case HaCommandKind.ShuffleSet: return control.SetShuffleAsync(command.Shuffle) ?? Task.CompletedTask;
                case HaCommandKind.RepeatSet: return control.SetRepeatAsync(command.RepeatMode) ?? Task.CompletedTask;
                default: throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown command");
            }
        }

        // A late failure must not surface as an unobserved task exception.
        private static void ObserveLater(Task work, Action<string> log, HaCommandKind kind)
        {
            work.ContinueWith(
                t => log?.Invoke("Late failure of Home Assistant command " + kind + ": HRESULT=0x" + t.Exception.InnerException?.HResult.ToString("X8") + "."),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
