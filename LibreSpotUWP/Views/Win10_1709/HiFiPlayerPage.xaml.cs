using LibreSpotUWP.Helpers;
using LibreSpotUWP.Interfaces;
using LibreSpotUWP.Models;
using LibreSpotUWP.Services;
using System;
using System.Linq;
using Windows.System.UserProfile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;

namespace LibreSpotUWP.Views.Win10_1709
{
    /// <summary>Opt-in appliance-style presentation over the existing playback service.</summary>
    public sealed partial class HiFiPlayerPage : Page
    {
        private readonly PositionSeekInteraction _seek = new PositionSeekInteraction();
        private IMediaService Media => App.Media;
        private string _artKey;
        private uint _lastSecond = uint.MaxValue;
        private bool _updatingControls;
        private bool _syncingLockScreenToggle;
        private bool _visible;
        private bool _vinylPlaying;

        public HiFiPlayerPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _visible = true;
            _syncingLockScreenToggle = true;
            PortraitLockScreenToggle.IsEnabled = LandscapeLockScreenToggle.IsEnabled =
                UserProfilePersonalizationSettings.IsSupported();
            PortraitLockScreenToggle.IsOn = LandscapeLockScreenToggle.IsOn =
                UserSettings.HiFiLockScreenArtworkEnabled;
            _syncingLockScreenToggle = false;
            UpdateLayoutForSize(ActualWidth, ActualHeight);
            if (Media != null)
            {
                Media.MediaStateChanged += OnMediaStateChanged;
                UpdateState(Media.Current);
                if (UserSettings.HiFiLockScreenArtworkEnabled)
                    HiFiLockScreenArtworkService.Schedule(Media.Current?.Track?.Uri, Media.Current?.ArtworkUri);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _visible = false;
            PortraitVinylStoryboard.Stop();
            LandscapeVinylStoryboard.Stop();
            _vinylPlaying = false;
            if (Media != null)
                Media.MediaStateChanged -= OnMediaStateChanged;
            _seek.Cancel();
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutForSize(e.NewSize.Width, e.NewSize.Height);

        private void UpdateLayoutForSize(double width, double height)
        {
            bool landscape = width >= 620 && width > height;
            PortraitLayout.Visibility = landscape ? Visibility.Collapsed : Visibility.Visible;
            LandscapeLayout.Visibility = landscape ? Visibility.Visible : Visibility.Collapsed;

            double side = landscape
                ? Math.Max(120, Math.Min(width * 0.43, height - 36))
                : Math.Max(120, Math.Min(width - 40, height * 0.43));
            PortraitArt.Width = PortraitArt.Height = side;
            LandscapeArt.Width = LandscapeArt.Height = side;
            if (_vinylPlaying) UpdateVinylMotion();
        }

        private void OnMediaStateChanged(object sender, MediaState state)
        {
            UiWorkScheduler.RunLatest(this, Dispatcher, () => UpdateState(state));
        }

        private void UpdateState(MediaState state)
        {
            if (state == null)
            {
                SetText("Choose this device in Spotify", "", "", "");
                SetStatus("Ready · Choose this device in Spotify");
                SetArtwork(null);
                return;
            }

            string album = state.Metadata?.Album?.Name;
            if (string.IsNullOrWhiteSpace(album)) album = state.Track?.Album;
            string title = state.DisplayTitle;
            string artist = state.DisplayArtist;
            if (state.IsNarrationActive) album = "Spotify DJ";

            SetText(title, artist, album ?? string.Empty, Format(state.PositionMs),
                "-" + Format(Math.Max(0, state.DisplayDurationMs - state.PositionMs)));
            SetStatus(GetStatus(state));

            bool hasArtist = !string.IsNullOrWhiteSpace(state.Metadata?.Artists?.FirstOrDefault()?.Id);
            bool hasAlbum = !string.IsNullOrWhiteSpace(state.Metadata?.Album?.Id);
            PortraitArtistNavigation.IsEnabled = LandscapeArtistNavigation.IsEnabled = hasArtist;
            PortraitAlbumNavigation.IsEnabled = LandscapeAlbumNavigation.IsEnabled = hasAlbum;

            var art = !string.IsNullOrWhiteSpace(state.ArtworkUri) ? state.ArtworkUri : state.Track?.CoverUrl;
            var key = state.Track?.Uri + "|" + art;
            if (!string.Equals(key, _artKey, StringComparison.OrdinalIgnoreCase))
            {
                _artKey = key;
                SetArtwork(art);
            }

            _updatingControls = true;
            double volume = state.Volume * 100.0 / 65535.0;
            PortraitVolume.Value = LandscapeVolume.Value = volume;
            SetPlayState(state.IsPlaying);
            if (state.DisplayDurationMs > 0)
            {
                PortraitPosition.Maximum = LandscapePosition.Maximum = state.DisplayDurationMs;
                if (!_seek.IsDragging && state.PositionMs / 1000 != _lastSecond)
                {
                    _lastSecond = state.PositionMs / 1000;
                    PortraitPosition.Value = LandscapePosition.Value = Math.Min(state.PositionMs, state.DisplayDurationMs);
                    SetElapsed(Format(state.PositionMs));
                }
            }
            else
            {
                PortraitPosition.Maximum = LandscapePosition.Maximum = 1;
                PortraitPosition.Value = LandscapePosition.Value = 0;
            }
            PortraitPosition.IsEnabled = LandscapePosition.IsEnabled = !state.IsNarrationActive;
            _updatingControls = false;
        }

        private static string GetStatus(MediaState state)
        {
            if (state.IsRecoveringOnlinePlayback) return string.IsNullOrWhiteSpace(state.StatusMessage) ? "Reconnecting" : state.StatusMessage;
            if (state.IsOffline) return "Offline playback";
            if (state.IsSpotifyConnectRemote)
                return "Playing on " + (string.IsNullOrWhiteSpace(state.SpotifyConnectDeviceName) ? "another device" : state.SpotifyConnectDeviceName);
            switch (state.PlaybackState)
            {
                case LibrespotPlaybackState.Playing: return "Playing on this device";
                case LibrespotPlaybackState.Paused: return "Paused";
                case LibrespotPlaybackState.Loading: return "Connecting";
                default: return state.IsSessionConnected ? "Ready · Choose this device in Spotify" : "Ready";
            }
        }

        private void SetText(string title, string artist, string album, string elapsed, string duration = "")
        {
            PortraitTitle.Text = LandscapeTitle.Text = title;
            PortraitArtist.Text = LandscapeArtist.Text = artist;
            PortraitAlbum.Text = LandscapeAlbum.Text = album;
            SetElapsed(elapsed);
            PortraitDuration.Text = LandscapeDuration.Text = duration;
        }

        private void SetElapsed(string value) => PortraitElapsed.Text = LandscapeElapsed.Text = value;
        private void SetStatus(string value) => PortraitStatus.Text = LandscapeStatus.Text = value;

        private void SetPlayState(bool playing)
        {
            var symbol = playing ? Symbol.Pause : Symbol.Play;
            PortraitPlayIcon.Symbol = LandscapePlayIcon.Symbol = symbol;
            if (_vinylPlaying == playing) return;
            _vinylPlaying = playing;
            UpdateVinylMotion();
        }

        private void UpdateVinylMotion()
        {
            PortraitVinylStoryboard.Stop();
            LandscapeVinylStoryboard.Stop();
            if (!_visible || !_vinylPlaying) return;
            if (LandscapeLayout.Visibility == Visibility.Visible)
                LandscapeVinylStoryboard.Begin();
            else
                PortraitVinylStoryboard.Begin();
        }

        private void LockScreenToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_visible || _syncingLockScreenToggle) return;
            bool enabled = ((ToggleSwitch)sender).IsOn;
            if (enabled && !UserProfilePersonalizationSettings.IsSupported()) enabled = false;
            _syncingLockScreenToggle = true;
            PortraitLockScreenToggle.IsOn = LandscapeLockScreenToggle.IsOn = enabled;
            _syncingLockScreenToggle = false;
            UserSettings.HiFiLockScreenArtworkEnabled = enabled;
            if (enabled)
                HiFiLockScreenArtworkService.Schedule(Media?.Current?.Track?.Uri, Media?.Current?.ArtworkUri);
            else
                HiFiLockScreenArtworkService.Disable();
        }

        private void SetArtwork(string value)
        {
            PortraitArt.Source = LandscapeArt.Source = null;
            PortraitPlaceholder.Visibility = LandscapePlaceholder.Visibility = Visibility.Visible;
            if (!ImageUriHelper.TryCreateImageUri(value, out Uri uri)) return;

            try
            {
                var bitmap = new BitmapImage(uri);
                PortraitArt.Source = LandscapeArt.Source = bitmap;
                PortraitPlaceholder.Visibility = LandscapePlaceholder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // Missing artwork is a presentation fallback, never a playback failure.
            }
        }

        private void Artwork_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            var image = sender as Image;
            if (image == PortraitArt)
                PortraitPlaceholder.Visibility = Visibility.Visible;
            else if (image == LandscapeArt)
                LandscapePlaceholder.Visibility = Visibility.Visible;
        }

        private static string Format(uint milliseconds)
        {
            uint seconds = milliseconds / 1000;
            return (seconds / 60) + ":" + (seconds % 60).ToString("D2");
        }

        private void Position_PointerPressed(object sender, PointerRoutedEventArgs e) => _seek.BeginDrag();
        private void Position_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => CommitSeek(sender as Slider);
        private void Position_PointerReleased(object sender, PointerRoutedEventArgs e) => CommitSeek(sender as Slider);

        private void Position_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_updatingControls && _seek.IsDragging)
                SetElapsed(Format((uint)Math.Max(0, e.NewValue)));
        }

        private void CommitSeek(Slider slider)
        {
            uint position;
            if (slider != null && _seek.TryCommit((uint)Math.Max(0, slider.Value), out position))
                Media?.Seek(position);
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (Media?.Current == null) return;
            if (Media.Current.IsPlaying) await Media.PauseAsync();
            else await Media.ResumeAsync();
        }

        private void Previous_Click(object sender, RoutedEventArgs e) => Media?.Previous();
        private void Next_Click(object sender, RoutedEventArgs e) => Media?.Next();

        private void Volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_updatingControls) Media?.SetVolumeDebounced(e.NewValue);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ((Window.Current.Content as Frame)?.Content as MainPage)?.HidePlayer();
        }

        private void ClassicButton_Click(object sender, RoutedEventArgs e)
        {
            UserSettings.HiFiNowPlayingEnabled = false;
            Frame?.Navigate(NavigationHelper.GetPageType("Player"));
        }

        private void ArtistNavigation_Click(object sender, RoutedEventArgs e)
        {
            var artistId = Media?.Current?.Metadata?.Artists?.FirstOrDefault()?.Id;
            if (!string.IsNullOrWhiteSpace(artistId))
                ((Window.Current.Content as Frame)?.Content as MainPage)?.NavigateToArtist(artistId);
        }

        private void AlbumNavigation_Click(object sender, RoutedEventArgs e)
        {
            var albumId = Media?.Current?.Metadata?.Album?.Id;
            if (!string.IsNullOrWhiteSpace(albumId))
                ((Window.Current.Content as Frame)?.Content as MainPage)?.NavigateToAlbum(albumId);
        }
    }
}
