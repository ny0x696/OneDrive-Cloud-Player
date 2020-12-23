﻿using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Views;
using LibVLCSharp.Platforms.UWP;
using LibVLCSharp.Shared;
using OneDrive_Cloud_Player.Models;
using OneDrive_Cloud_Player.Models.Interfaces;
using OneDrive_Cloud_Player.Services.Helpers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Input;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace OneDrive_Cloud_Player.ViewModels
{
    /// <summary>
    /// Main view model
    /// </summary>
    public class VideoPlayerPageViewModel : ViewModelBase, INotifyPropertyChanged, IDisposable, INavigable
    {
        private readonly ApplicationDataContainer localMediaVolumeLevelSetting;
        private readonly INavigationService _navigationService;
        private readonly GraphHelper graphHelper = GraphHelper.Instance();
        /// <summary>
        /// Fires every two minutes to indicate the OneDrive download URL has expired.
        /// </summary>
        private readonly Timer reloadIntervalTimer = new Timer(120000);
        /// <summary>
        /// Single-shot timer to hide the filename shortly after playing a video.
        /// </summary>
        private readonly Timer fileNameOverlayTimer = new Timer(5000);
        private MediaWrapper MediaWrapper = null;
        private bool InvalidOneDriveSession = false;
        private MediaPlayer mediaPlayer;
        private int MediaListIndex;

        public bool IsSeeking { get; set; }
        private LibVLC LibVLC { get; set; }

        /// <summary>
        /// Gets the commands for the initialization
        /// </summary>
        public ICommand InitializeLibVLCCommand { get; }
        public ICommand StartedDraggingThumbCommand { get; }
        public ICommand StoppedDraggingThumbCommand { get; }
        public ICommand ChangePlayingStateCommand { get; }
        public ICommand SeekedCommand { get; }
        public ICommand ReloadCurrentMediaCommand { get; }
        public ICommand StopMediaCommand { get; }
        public ICommand KeyDownEventCommand { get; }
        public ICommand SeekBackwardCommand { get; }
        public ICommand SeekForewardCommand { get; }
        public ICommand PlayPreviousVideoCommand { get; }
        public ICommand PlayNextVideoCommand { get; }

        private long timeLineValue;

        public long TimeLineValue
        {
            get { return timeLineValue; }
            set
            {
                timeLineValue = value;
                RaisePropertyChanged("TimeLineValue");
            }
        }

        private long videoLength;

        public long VideoLength
        {
            get { return videoLength; }
            set
            {
                videoLength = value;
                RaisePropertyChanged("VideoLength");
            }
        }

        private int mediaVolumeLevel;

        public int MediaVolumeLevel
        {
            get { return mediaVolumeLevel; }
            set
            {
                SetMediaVolume(value);
                mediaVolumeLevel = value;
                RaisePropertyChanged("MediaVolumeLevel");
            }
        }

        private string volumeButtonFontIcon = "\xE992";

        public string VolumeButtonFontIcon
        {
            get { return volumeButtonFontIcon; }
            set
            {
                volumeButtonFontIcon = value;
                RaisePropertyChanged("VolumeButtonFontIcon");
            }
        }

        private string fileName;

        public string FileName
        {
            get { return fileName; }
            private set
            {
                fileName = value;
                RaisePropertyChanged("FileName");
            }
        }

        private Visibility fileNameOverlayVisiblity;

        public Visibility FileNameOverlayVisiblity
        {
            get { return fileNameOverlayVisiblity; }
            set
            {
                fileNameOverlayVisiblity = value;
                RaisePropertyChanged("FileNameOverlayVisiblity");
            }
        }

        private string playPauseButtonFontIcon = "\xE768";

        public string PlayPauseButtonFontIcon
        {
            get { return playPauseButtonFontIcon; }
            set
            {
                playPauseButtonFontIcon = value;
                RaisePropertyChanged("PlayPauseButtonFontIcon");
            }
        }

        private string mediaControlGridVisibility = "Visible";

        public string MediaControlGridVisibility
        {
            get { return mediaControlGridVisibility; }
            set
            {
                mediaControlGridVisibility = value;
                RaisePropertyChanged("MediaControlGridVisibility");
            }
        }

        private Visibility visibilityPreviousMediaBtn;

        public Visibility VisibilityPreviousMediaBtn
        {
            get { return visibilityPreviousMediaBtn; }
            set
            {
                visibilityPreviousMediaBtn = value;
                RaisePropertyChanged("VisibilityPreviousMediaBtn");
            }
        }

        private Visibility visibilityNextMediaBtn;

        public Visibility VisibilityNextMediaBtn
        {
            get { return visibilityNextMediaBtn; }
            set
            {
                visibilityNextMediaBtn = value;
                RaisePropertyChanged("VisibilityNextMediaBtn");
            }
        }

        /// <summary>
        /// Gets the media player
        /// </summary>
        public MediaPlayer MediaPlayer
        {
            get => mediaPlayer;
            set => Set(nameof(MediaPlayer), ref mediaPlayer, value);
        }

        /// <summary>
        /// Initialized a new instance of <see cref="MainViewModel"/> class
        /// </summary>
        public VideoPlayerPageViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
            InitializeLibVLCCommand = new RelayCommand<InitializedEventArgs>(InitializeLibVLC);
            StartedDraggingThumbCommand = new RelayCommand(StartedDraggingThumb, CanExecuteCommand);
            StoppedDraggingThumbCommand = new RelayCommand(StoppedDraggingThumb, CanExecuteCommand);
            ChangePlayingStateCommand = new RelayCommand(ChangePlayingState, CanExecuteCommand);
            SeekedCommand = new RelayCommand(Seeked, CanExecuteCommand);
            ReloadCurrentMediaCommand = new RelayCommand(ReloadCurrentMedia, CanExecuteCommand);
            StopMediaCommand = new RelayCommand(StopMedia, CanExecuteCommand);
            KeyDownEventCommand = new RelayCommand<KeyEventArgs>(KeyDownEvent);
            SeekBackwardCommand = new RelayCommand<double>(SeekBackward);
            SeekForewardCommand = new RelayCommand<double>(SeekForeward);
            PlayPreviousVideoCommand = new RelayCommand(PlayPreviousVideo, CanExecuteCommand);
            PlayNextVideoCommand = new RelayCommand(PlayNextVideo, CanExecuteCommand);

            this.localMediaVolumeLevelSetting = ApplicationData.Current.LocalSettings;

            // Sets the MediaVolume setting to 100 when its not already set
            // before in the setting. (This is part of an audio workaround).
            if (localMediaVolumeLevelSetting.Values["MediaVolume"] is null)
            {
                localMediaVolumeLevelSetting.Values["MediaVolume"] = 100;
            }
        }

        private bool CanExecuteCommand()
        {
            return true;
        }

        /// <summary>
        /// Gets called every time when navigated to this page.
        /// </summary>
        /// <param name="eventArgs"></param>
        private async void InitializeLibVLC(InitializedEventArgs eventArgs)
        {
            // Reset properties.
            VideoLength = 0;
            PlayPauseButtonFontIcon = "\xE768";

            // Create LibVLC instance and subscribe to events.
            LibVLC = new LibVLC(eventArgs.SwapChainOptions);
            MediaPlayer = new MediaPlayer(LibVLC);

            MediaPlayer.Playing += MediaPlayer_Playing;
            MediaPlayer.Paused += MediaPlayer_Paused;
            MediaPlayer.TimeChanged += MediaPlayer_TimeChanged;

            // Subscribe to the timer events and start the reloadInterval timer.
            fileNameOverlayTimer.Elapsed += FileNameOverlayTimer_Elapsed;
            reloadIntervalTimer.Elapsed += ReloadIntervalTimer_Elapsed;
            reloadIntervalTimer.Start();

            // Finally, play the media.
            await PlayMedia();
        }

        private async void MediaPlayer_Playing(object sender, EventArgs e)
        {
            Debug.WriteLine(DateTime.Now.ToString("hh:mm:ss.fff") + ": Media is playing");
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                MediaVolumeLevel = (int)this.localMediaVolumeLevelSetting.Values["MediaVolume"];
                Debug.WriteLine(DateTime.Now.ToString("hh:mm:ss.fff") + ": Set volume in container: " + this.localMediaVolumeLevelSetting.Values["MediaVolume"]);
                Debug.WriteLine(DateTime.Now.ToString("hh:mm:ss.fff") + ": Set volume in our property: " + MediaVolumeLevel);
                Debug.WriteLine(DateTime.Now.ToString("hh:mm:ss.fff") + ": Actual volume: " + MediaPlayer.Volume);
                //Sets the max value of the seekbar.
                VideoLength = MediaPlayer.Length;

                PlayPauseButtonFontIcon = "\xE769";
            });
        }

        private async void MediaPlayer_Paused(object sender, EventArgs e)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                PlayPauseButtonFontIcon = "\xE768";
            });
        }

        private async void MediaPlayer_TimeChanged(object sender, EventArgs e)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                // Updates the value of the seekbar on TimeChanged event
                // when the user is not seeking.
                if (!IsSeeking)
                {
                    // Sometimes the MediaPlayer is already null upon
                    // navigating away and this still gets called.
                    if (MediaPlayer != null)
                    {
                        TimeLineValue = MediaPlayer.Time;
                    }
                }
            });
        }

        private void ReloadIntervalTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            InvalidOneDriveSession = true;
        }

        private async void FileNameOverlayTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                FileNameOverlayVisiblity = Visibility.Collapsed;
            });
        }

        /// <summary>
        /// Retrieves the download url of the media file to be played.
        /// </summary>
        /// <param name="MediaWrapper"></param>
        /// <returns></returns>
        private async Task<string> RetrieveDownloadURLMedia(MediaWrapper mediaWrapper)
        {
            var driveItem = await graphHelper.GetItemInformationAsync(mediaWrapper.DriveId, mediaWrapper.CachedDriveItem.ItemId);

            //Retrieve a temporary download URL from the drive item.
            return (string)driveItem.AdditionalData["@microsoft.graph.downloadUrl"];
        }

        /// <summary>
        /// Plays the media and starts a timer to temporarily show the filename
        /// of the file being played.
        /// </summary>
        /// <param name="startTime"></param>
        /// <returns></returns>
        private async Task PlayMedia(long startTime = 0)
        {
            CheckPreviousNextMediaInList();

            FileName = MediaWrapper.CachedDriveItem.Name;

            FileNameOverlayVisiblity = Visibility.Visible;

            fileNameOverlayTimer.AutoReset = false;
            fileNameOverlayTimer.Start();

            string mediaDownloadURL = await RetrieveDownloadURLMedia(MediaWrapper);
            // Play the OneDrive file.
            MediaPlayer.Play(new Media(LibVLC, new Uri(mediaDownloadURL)));

            if (MediaPlayer is null)
            {
                Debug.WriteLine("Error: Could not set start time.");
                return;
            }

            MediaPlayer.Time = startTime;
        }

        private void SetMediaVolume(int volumeLevel)
        {
            if (MediaPlayer is null)
            {
                Debug.WriteLine("Error: Could not set the volume.");
                return; // Return when the MediaPlayer is null so it does not cause exception.
            }
            this.localMediaVolumeLevelSetting.Values["MediaVolume"] = volumeLevel; // Set the new volume in the MediaVolume setting.
            MediaPlayer.Volume = volumeLevel;
            UpdateVolumeButtonFontIcon(volumeLevel);
        }

        //TODO: Better alternative than this.
        /// <summary>
        /// Updates the icon of the volume button to a icon that fits by the volume level.
        /// </summary>
        private void UpdateVolumeButtonFontIcon(int volumeLevel)
        {
            if (volumeLevel <= 25 && !VolumeButtonFontIcon.Equals("\xE992"))
            {
                VolumeButtonFontIcon = "\xE992";
            }
            else if (volumeLevel > 25 && volumeLevel <= 50 && !VolumeButtonFontIcon.Equals("\xE993"))
            {
                VolumeButtonFontIcon = "\xE993";
            }
            else if (volumeLevel > 50 && volumeLevel <= 75 && !VolumeButtonFontIcon.Equals("\xE994"))
            {
                VolumeButtonFontIcon = "\xE994";
            }
            else if (volumeLevel > 75 && !VolumeButtonFontIcon.Equals("\xE995"))
            {
                VolumeButtonFontIcon = "\xE995";
            }
        }

        /// <summary>
        /// Sets the IsSeekig boolean on true so the seekbar value does not get updated.
        /// </summary>
        public void StartedDraggingThumb()
        {
            IsSeeking = true;
        }

        /// <summary>
        /// Sets the IsIseeking boolean on false so the seekbar value can gets updates again.
        /// </summary>
        public void StoppedDraggingThumb()
        {
            IsSeeking = false;
        }

        /// <summary>
        /// Sets the time of the media with the time of the seekbar value.
        /// </summary>
        private void Seeked()
        {
            SetVideoTime(TimeLineValue);
        }

        /// <summary>
        /// Seek backwards in the media by given miliseconds.
        /// </summary>
        /// <param name="ms"></param>
        public void SeekBackward(double ms)
        {
            SetVideoTime(MediaPlayer.Time - ms);
        }

        /// <summary>
        /// Seek foreward in the media by given miliseconds.
        /// </summary>
        /// <param name="ms"></param>
        public void SeekForeward(double ms)
        {
            SetVideoTime(MediaPlayer.Time + ms);
        }

        /// <summary>
        /// Sets the time of the media with the given time.
        /// </summary>
        /// <param name="time"></param>
        private void SetVideoTime(double time)
        {
            if (InvalidOneDriveSession)
            {
                Debug.WriteLine(" + OneDrive link expired.");
                Debug.WriteLine("   + Reloading media.");
                ReloadCurrentMedia();
            }
            MediaPlayer.Time = Convert.ToInt64(time);
        }

        //TODO: Implement a Dialog system that shows a dialog when there is an error.
        /// <summary>
        /// Tries to restart the media that is currently playing.
        /// </summary>
        private async void ReloadCurrentMedia()
        {
            await PlayMedia(TimeLineValue);
            reloadIntervalTimer.Stop(); //In case a user reloads with the reload button. Stop the timer so we dont get multiple running.
            InvalidOneDriveSession = false;
            reloadIntervalTimer.Start();
        }

        /// <summary>
        /// Changes the media playing state from paused to playing and vice versa. 
        /// </summary>
        private void ChangePlayingState()
        {
            bool isPlaying = MediaPlayer.IsPlaying;

            if (isPlaying)
            {
                MediaPlayer.SetPause(true);
            }
            else if (!isPlaying)
            {
                MediaPlayer.SetPause(false);
            }
        }

        public void StopMedia()
        {
            MediaPlayer.Stop();
            TimeLineValue = 0;
            Dispose();
            // Go back to the last page.
            _navigationService.GoBack();
        }

        /// <summary>
        /// Gets called when a user presses a key when the videoplayer page is open.
        /// </summary>
        /// <param name="e"></param>
        private void KeyDownEvent(KeyEventArgs keyEvent)
        {
            CoreVirtualKeyStates shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift);

            if (!shift.HasFlag(CoreVirtualKeyStates.Down))
                switch (keyEvent.VirtualKey)
                {
                    case VirtualKey.Space:
                        ChangePlayingState();
                        break;
                    case VirtualKey.Left:
                        SeekBackward(5000);
                        break;
                    case VirtualKey.Right:
                        SeekForeward(5000);
                        break;
                    case VirtualKey.J:
                        SeekBackward(10000);
                        break;
                    case VirtualKey.L:
                        SeekForeward(10000);
                        break;
                }

            if (shift.HasFlag(CoreVirtualKeyStates.Down))
            {
                switch (keyEvent.VirtualKey)
                {
                    case VirtualKey.N:
                        PlayNextVideo();
                        break;
                    case VirtualKey.P:
                        PlayPreviousVideo();
                        break;
                }
            }
        }

        /// <summary>
        /// Play the previous video in the list.
        /// </summary>
        private async void PlayPreviousVideo()
        {
            if ((MediaListIndex - 1) < 0)
            {
                return;
            }

            MediaWrapper.CachedDriveItem = App.Current.MediaItemList[--MediaListIndex];
            await PlayMedia();
        }

        /// <summary>
        /// Play the next video in the list.
        /// </summary>
        private async void PlayNextVideo()
        {
            if ((MediaListIndex + 1) >= App.Current.MediaItemList.Count)
            {
                return;
            }

            MediaWrapper.CachedDriveItem = App.Current.MediaItemList[++MediaListIndex];
            await PlayMedia();
        }

        /// <summary>
        /// Checks if there is an upcoming or a previous media file available and
        /// change the visibility status of the previous / next buttons accordingly.
        /// </summary>
        private void CheckPreviousNextMediaInList()
        {
            if ((MediaListIndex - 1) < 0)
            {
                VisibilityPreviousMediaBtn = Visibility.Collapsed;
            }
            else
            {
                VisibilityPreviousMediaBtn = Visibility.Visible;
            }

            if ((MediaListIndex + 1) >= App.Current.MediaItemList.Count)
            {
                VisibilityNextMediaBtn = Visibility.Collapsed;
            }
            else
            {
                VisibilityNextMediaBtn = Visibility.Visible;
            }
        }

        /// <summary>
        /// Called upon navigating to the videoplayer page and is used for
        /// passing arguments from the main page to the video player page.
        /// </summary>
        /// <param name="parameter"></param>
        public void Activate(object mediaWrapper)
        {
            // Set the field so the playmedia method can use it.
            MediaWrapper = (MediaWrapper)mediaWrapper;
            MediaListIndex = App.Current.MediaItemList.IndexOf(MediaWrapper.CachedDriveItem);

            if (MediaListIndex < 0)
            {
                throw new InvalidOperationException(String.Format("Object of type '{0}' not found.", (MediaWrapper.CachedDriveItem).GetType()));
            }
        }

        /// <summary>
        /// Called upon navigating away from the view associated with this viewmodel.
        /// </summary>
        /// <param name="parameter"></param>
        public void Deactivate(object parameter)
        {

        }

        /// <summary>
        /// Cleaning
        /// </summary>
        public void Dispose()
        {
            // Unsubscribe from event handlers.
            MediaPlayer.Playing -= MediaPlayer_Playing;
            MediaPlayer.Paused -= MediaPlayer_Paused;
            MediaPlayer.TimeChanged -= MediaPlayer_TimeChanged;
            reloadIntervalTimer.Elapsed -= ReloadIntervalTimer_Elapsed;
            fileNameOverlayTimer.Elapsed -= FileNameOverlayTimer_Elapsed;

            // Dispose of the LibVLC instance and the mediaplayer.
            var mediaPlayer = MediaPlayer;
            MediaPlayer = null;
            mediaPlayer?.Dispose();
            LibVLC?.Dispose();
            LibVLC = null;
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~VideoPlayerPageViewModel()
        {
            Dispose();
        }
    }
}
