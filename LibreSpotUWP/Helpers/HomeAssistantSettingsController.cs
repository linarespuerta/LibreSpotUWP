using LibreSpotUWP.Services;
using LibreSpotUWP.Services.HomeAssistant;
using System;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LibreSpotUWP.Helpers
{
    // The Home Assistant section is identical on the three Settings variants, so its behaviour lives
    // here once. The secret is write-only: it is saved to the credential vault and never shown again.
    internal sealed class HomeAssistantSettingsController
    {
        private readonly ToggleSwitch _toggle;
        private readonly TextBox _url;
        private readonly PasswordBox _secret;
        private readonly Button _save;
        private readonly TextBlock _status;
        private bool _attached;
        private bool _loading;

        public HomeAssistantSettingsController(ToggleSwitch toggle, TextBox url, PasswordBox secret, Button save, TextBlock status)
        {
            _toggle = toggle;
            _url = url;
            _secret = secret;
            _save = save;
            _status = status;
        }

        private static HomeAssistantLinkService Link => App.HomeAssistantLink;

        public async void Load()
        {
            _loading = true;
            try
            {
                _toggle.IsOn = UserSettings.HomeAssistantLinkEnabled;
                _url.Text = UserSettings.HomeAssistantLinkUrl;
                _secret.Password = string.Empty;
                if (!_attached)
                {
                    _toggle.Toggled += OnToggled;
                    _save.Click += OnSave;
                    if (Link != null) Link.StatusChanged += OnStatusChanged;
                    _attached = true;
                }
                ShowStatus(Link?.Status);
                var hasSecret = Link != null && await Link.HasSecretAsync();
                _secret.PlaceholderText = hasSecret
                    ? "Saved. Enter a new secret only to replace it."
                    : "Paste the secret shown by Home Assistant";
            }
            catch (Exception ex) { LogService.Warn($"[Settings] Home Assistant section failed to load: HRESULT=0x{ex.HResult:X8}."); }
            finally { _loading = false; }
        }

        public void Unload()
        {
            if (!_attached) return;
            _toggle.Toggled -= OnToggled;
            _save.Click -= OnSave;
            if (Link != null) Link.StatusChanged -= OnStatusChanged;
            _attached = false;
        }

        private async void OnToggled(object sender, RoutedEventArgs e)
        {
            if (_loading || Link == null) return;
            UserSettings.HomeAssistantLinkEnabled = _toggle.IsOn;
            LogService.Info($"[Settings] Home Assistant link {(_toggle.IsOn ? "enabled" : "disabled")}.");
            await ApplyAsync();
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            if (Link == null) return;
            string url, secret, error;
            if (!HaLinkPolicy.TryNormalizeUrl(_url.Text, out url, out error))
            {
                _status.Text = error;
                return;
            }
            var typedSecret = _secret.Password;
            if (!string.IsNullOrWhiteSpace(typedSecret))
            {
                if (!HaLinkPolicy.TryNormalizeSecret(typedSecret, out secret, out error))
                {
                    _status.Text = error;
                    return;
                }
                await Link.SaveSecretAsync(secret);
                _secret.Password = string.Empty;
                _secret.PlaceholderText = "Saved. Enter a new secret only to replace it.";
            }
            else if (!await Link.HasSecretAsync())
            {
                _status.Text = "Enter the secret shown by Home Assistant.";
                return;
            }

            UserSettings.HomeAssistantLinkUrl = url;
            _url.Text = url;
            _loading = true;
            _toggle.IsOn = true;
            _loading = false;
            UserSettings.HomeAssistantLinkEnabled = true;
            LogService.Info("[Settings] Home Assistant link settings saved.");
            await ApplyAsync();
        }

        private async Task ApplyAsync()
        {
            try { await Link.ApplySettingsAsync(); }
            catch (Exception ex)
            {
                LogService.Warn($"[Settings] Home Assistant link could not be applied: HRESULT=0x{ex.HResult:X8}.");
                _status.Text = "Could not start the Home Assistant link.";
            }
        }

        private async void OnStatusChanged(object sender, HomeAssistantLinkStatusInfo info)
        {
            try { await _status.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ShowStatus(info)); }
            catch (Exception ex) { LogService.Info($"[Settings] Home Assistant status update skipped: HRESULT=0x{ex.HResult:X8}."); }
        }

        private void ShowStatus(HomeAssistantLinkStatusInfo info)
        {
            _status.Text = info?.Text ?? HaLinkPolicy.Describe(HaLinkStatus.Disabled, HaLinkFailure.None);
        }
    }
}
