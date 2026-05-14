using System;
using System.Collections.ObjectModel;
using System.Linq;
using AudioClient.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioClient.GUI.ViewModels;

public partial class VideoListViewModel : ObservableObject
{
    public ObservableCollection<VideoPlayerItemViewModel> Videos { get; } = new();
    public ObservableCollection<VideoPlayerItemViewModel> ViewingVideos { get; } = new();

    public bool HasVideos => Videos.Count > 0;
    public bool HasViewingVideos => ViewingVideos.Count > 0;

    [ObservableProperty] private VideoPlayerItemViewModel? _expandedVideo;
    public bool HasExpandedVideo => ExpandedVideo != null;

    partial void OnExpandedVideoChanged(VideoPlayerItemViewModel? value)
        => OnPropertyChanged(nameof(HasExpandedVideo));

    public Action<string>? OnResumeRequested { get; set; }
    public Action<string>? OnPauseRequested { get; set; }
    public Action<string>? OnStopRequested { get; set; }
    public Action<string, float>? OnSeekRequested { get; set; }
    public Action<string, bool>? OnLoopChanged { get; set; }

    public void Update(System.Collections.Generic.List<VideoPlayerInfo> videos)
    {
        var hadVideos = HasVideos;
        var hadViewingVideos = HasViewingVideos;
        var incomingIds = videos.Select(v => v.Id).ToHashSet();

        for (var i = Videos.Count - 1; i >= 0; i--)
        {
            if (!incomingIds.Contains(Videos[i].Id))
            {
                ViewingVideos.Remove(Videos[i]);
                Videos.RemoveAt(i);
            }
        }

        foreach (var video in videos)
        {
            var existing = Videos.FirstOrDefault(v => v.Id == video.Id);
            if (existing == null)
                Videos.Add(new VideoPlayerItemViewModel(video, this));
            else
                existing.Update(video);
        }

        if (hadVideos != HasVideos)
            OnPropertyChanged(nameof(HasVideos));
        if (hadViewingVideos != HasViewingVideos)
            OnPropertyChanged(nameof(HasViewingVideos));
    }

    internal void RequestResume(VideoPlayerItemViewModel item)
    {
        OpenViewer(item);
        OnResumeRequested?.Invoke(item.Id);
    }

    internal void RequestPause(VideoPlayerItemViewModel item)
    {
        OnPauseRequested?.Invoke(item.Id);
    }

    internal void RequestStop(VideoPlayerItemViewModel item)
    {
        OnStopRequested?.Invoke(item.Id);
    }

    internal void RequestSeek(VideoPlayerItemViewModel item, float seconds)
    {
        OnSeekRequested?.Invoke(item.Id, seconds);
    }

    internal void RequestLoop(VideoPlayerItemViewModel item, bool loop)
    {
        OnLoopChanged?.Invoke(item.Id, loop);
    }

    internal void ToggleViewer(VideoPlayerItemViewModel item)
    {
        if (item.IsViewing)
            CloseViewer(item);
        else
            OpenViewer(item);
    }

    internal void CloseViewer(VideoPlayerItemViewModel item)
    {
        var hadViewingVideos = HasViewingVideos;
        if (!ViewingVideos.Remove(item))
            return;

        item.IsViewing = false;
        if (ExpandedVideo == item)
            CollapseExpandedVideo();
        if (hadViewingVideos != HasViewingVideos)
            OnPropertyChanged(nameof(HasViewingVideos));
    }

    internal void ExpandViewer(VideoPlayerItemViewModel item)
        => ExpandedVideo = item;

    [RelayCommand]
    private void CollapseExpandedVideo()
        => ExpandedVideo = null;

    private void OpenViewer(VideoPlayerItemViewModel item)
    {
        if (item.IsViewing)
            return;

        var hadViewingVideos = HasViewingVideos;
        item.IsViewing = true;
        ViewingVideos.Add(item);
        if (hadViewingVideos != HasViewingVideos)
            OnPropertyChanged(nameof(HasViewingVideos));
    }
}

public partial class VideoPlayerItemViewModel : ObservableObject
{
    private readonly VideoListViewModel _owner;
    private bool _isUpdatingFromModel;
    private string _title = "";
    private string _slotName = "";
    private string _url = "";
    private string _playbackUrl = "";
    private bool _isPlaying;
    private double _clipLength;

    public string Id { get; }
    public string Title => _title;
    public string SlotName => _slotName;
    public string Url => _url;
    public string PlaybackUrl => _playbackUrl;
    public bool IsPlaying => _isPlaying;
    public bool CanSeek => _clipLength > 0 && !double.IsInfinity(_clipLength) && !double.IsNaN(_clipLength);
    public double ClipLength => _clipLength;
    public string StateText => IsPlaying ? "Playing" : "Paused";
    public string PlayPauseToolTip => IsPlaying ? "Pause" : "Play";
    public string PositionText => FormatPosition(Position, ClipLength);
    public double SeekMaximum => CanSeek ? ClipLength : 1;
    public string UrlDisplay => string.IsNullOrWhiteSpace(Url) ? "No URL" : Url;

    [ObservableProperty] private float _position;
    [ObservableProperty] private bool _isLooping;
    [ObservableProperty] private bool _isViewing;
    [ObservableProperty] private double _volume = 100;

    public string ViewButtonText => IsViewing ? "Close" : "View";

    public VideoPlayerItemViewModel(VideoPlayerInfo info, VideoListViewModel owner)
    {
        _owner = owner;
        Id = info.Id;
        _title = string.IsNullOrWhiteSpace(info.Title) ? "Untitled video" : info.Title;
        _slotName = info.SlotName;
        _url = info.Url;
        _playbackUrl = info.PlaybackUrl;
        _isPlaying = info.IsPlaying;
        _clipLength = info.ClipLength;
        _isUpdatingFromModel = true;
        Position = info.Position;
        IsLooping = info.IsLooping;
        _isUpdatingFromModel = false;
    }

    public void Update(VideoPlayerInfo info)
    {
        _isUpdatingFromModel = true;

        SetIfChanged(ref _title, string.IsNullOrWhiteSpace(info.Title) ? "Untitled video" : info.Title, nameof(Title));
        SetIfChanged(ref _slotName, info.SlotName, nameof(SlotName));
        SetIfChanged(ref _url, info.Url, nameof(Url), nameof(UrlDisplay));
        SetIfChanged(ref _playbackUrl, info.PlaybackUrl, nameof(PlaybackUrl));
        SetIfChanged(ref _isPlaying, info.IsPlaying, nameof(IsPlaying), nameof(StateText), nameof(PlayPauseToolTip));

        var canSeekBefore = CanSeek;
        if (Math.Abs(_clipLength - info.ClipLength) > 0.001)
        {
            _clipLength = info.ClipLength;
            OnPropertyChanged(nameof(ClipLength));
            OnPropertyChanged(nameof(SeekMaximum));
            OnPropertyChanged(nameof(PositionText));
        }
        if (canSeekBefore != CanSeek)
            OnPropertyChanged(nameof(CanSeek));

        Position = info.Position;
        IsLooping = info.IsLooping;

        _isUpdatingFromModel = false;
    }

    [RelayCommand]
    private void Resume() => _owner.RequestResume(this);

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (IsPlaying)
            _owner.RequestPause(this);
        else
            _owner.RequestResume(this);
    }

    [RelayCommand]
    private void ToggleViewer() => _owner.ToggleViewer(this);

    [RelayCommand]
    private void CloseViewer() => _owner.CloseViewer(this);

    [RelayCommand]
    private void Expand() => _owner.ExpandViewer(this);

    [RelayCommand]
    private void Pause() => _owner.RequestPause(this);

    [RelayCommand]
    private void Stop() => _owner.RequestStop(this);

    partial void OnPositionChanged(float value)
    {
        OnPropertyChanged(nameof(PositionText));
        if (!_isUpdatingFromModel && CanSeek)
            _owner.RequestSeek(this, value);
    }

    partial void OnIsLoopingChanged(bool value)
    {
        if (!_isUpdatingFromModel && CanSeek)
            _owner.RequestLoop(this, value);
    }

    partial void OnIsViewingChanged(bool value)
        => OnPropertyChanged(nameof(ViewButtonText));

    private static string FormatPosition(float position, double length)
    {
        if (length <= 0 || double.IsInfinity(length) || double.IsNaN(length))
            return $"{FormatTime(position)} / --:--";

        return $"{FormatTime(position)} / {FormatTime(length)}";
    }

    private void SetIfChanged<T>(ref T field, T value, params string[] propertyNames)
    {
        if (Equals(field, value))
            return;

        field = value;
        foreach (var propertyName in propertyNames)
            OnPropertyChanged(propertyName);
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}
