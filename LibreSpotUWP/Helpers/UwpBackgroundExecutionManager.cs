using LibreSpotUWP.Interfaces;
using LibreSpotUWP.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.ExtendedExecution;

namespace LibreSpotUWP.Helpers
{
    public class UwpBackgroundExecutionManager : IBackgroundExecutionManager
    {
        private ExtendedExecutionSession _session;
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private readonly object _sessionLock = new object();
        private bool _revoked;

        public async Task<bool> RequestKeepAliveAsync()
        {
            await _requestGate.WaitAsync();
            try
            {
                lock (_sessionLock)
                {
                    if (_session != null && !_revoked)
                        return true;
                }

                DisposeCurrentSession();
                var accessStatus = await BackgroundExecutionManager.RequestAccessAsync();

                if (accessStatus == BackgroundAccessStatus.AlwaysAllowed ||
                    accessStatus == BackgroundAccessStatus.AllowedSubjectToSystemPolicy ||
                    accessStatus == BackgroundAccessStatus.AllowedMayUseActiveRealTimeConnectivity ||
                    accessStatus == BackgroundAccessStatus.AllowedWithAlwaysOnRealTimeConnectivity)
                {
                    return await StartExtendedSessionAsync();
                }

                LogService.Warn($"Background access denied: {accessStatus}");
                return false;
            }
            catch (Exception ex)
            {
                DisposeCurrentSession();
                LogService.Warn($"Background execution access request failed: {ex.Message}");
                return false;
            }
            finally
            {
                _requestGate.Release();
            }
        }

        private async Task<bool> StartExtendedSessionAsync()
        {
            ExtendedExecutionSession candidate = null;
            try
            {
                candidate = new ExtendedExecutionSession
                {
                    Reason = ExtendedExecutionReason.Unspecified,
                    Description = "LibreSpotUWP is connected to Spotify."
                };

                candidate.Revoked += Session_Revoked;
                lock (_sessionLock)
                {
                    _session = candidate;
                    _revoked = false;
                }

                var result = await candidate.RequestExtensionAsync();

                lock (_sessionLock)
                {
                    if (result == ExtendedExecutionResult.Allowed &&
                        ReferenceEquals(_session, candidate) && !_revoked)
                    {
                        LogService.Info("Extended execution allowed.");
                        return true;
                    }
                }

                LogService.Warn($"Extended execution denied or revoked during request. result={result}.");
                ClearSession(candidate);
                return false;
            }
            catch (Exception ex)
            {
                LogService.Warn($"Extended execution request failed: {ex.Message}");
                if (candidate != null)
                    ClearSession(candidate);
                return false;
            }
        }

        public void StopKeepAlive()
        {
            var session = TakeCurrentSession();
            if (session != null)
            {
                session.Revoked -= Session_Revoked;
                session.Dispose();
                LogService.Info("Extended execution stopped.");
            }
        }

        private void Session_Revoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            LogService.Warn($"Extended execution session revoked. reason={args.Reason}");
            var session = sender as ExtendedExecutionSession;
            lock (_sessionLock)
            {
                if (!ReferenceEquals(_session, session))
                    return;
                _session = null;
                _revoked = true;
            }

            if (session != null)
            {
                session.Revoked -= Session_Revoked;
                session.Dispose();
            }
        }

        private void DisposeCurrentSession()
        {
            var session = TakeCurrentSession();
            if (session == null)
                return;
            session.Revoked -= Session_Revoked;
            session.Dispose();
        }

        private ExtendedExecutionSession TakeCurrentSession()
        {
            lock (_sessionLock)
            {
                var session = _session;
                _session = null;
                _revoked = false;
                return session;
            }
        }

        private void ClearSession(ExtendedExecutionSession session)
        {
            lock (_sessionLock)
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                    _revoked = false;
                }
            }

            session.Revoked -= Session_Revoked;
            session.Dispose();
        }
    }
}
