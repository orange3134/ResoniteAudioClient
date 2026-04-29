using System;
using System.Collections.ObjectModel;
using System.Linq;
using AudioClient.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioClient.GUI.ViewModels;

public partial class VideoListViewModel : ObservableObject
{
    [ObservableProperty] private VideoPlayerItemViewModel? _selectedVideo;

    public ObservableCollection<VideoPlayerItemViewModel> Videos { get; } = new();

    public bool HasVideos => Videos.Count > 0;
    public bool HasSelectedVideo => SelectedVideo != null;

    public Action<string>? OnPlayRequested { get; set; }
    public Action<string>? OnPauseRequested { get; set; }
    public Action<string>? OnStopRequested { get; set; }
    public Action<string, float>? OnSeekRequested { get; set; }
    public Action<string, bool>? OnLoopChanged { get; set; }

    public void Update(System.Collections.Generic.List<VideoPlayerInfo> videos)
    {
        var selectedId = SelectedVideo?.Id;
        var hadVideos = HasVideos;
        var incomingIds = videos.Select(v => v.Id).ToHashSet();

        for (var i = Videos.Count - 1; i >= 0; i--)
        {
            if (!incomingIds.Contains(Videos[i].Id))
                Videos.RemoveAt(i);
        }

        foreach (var video in videos)
        {
            var existing = Videos.FirstOrDefault(v => v.Id == video.Id);
            if (existing == null)
                Videos.Add(new VideoPlayerItemViewModel(video, this));
            else
                existing.Update(video);
        }

        SelectedVideo = Videos.FirstOrDefault(v => v.Id == selectedId) ?? Videos.FirstOrDefault();
        if (hadVideos != HasVideos)
            OnPropertyChanged(nameof(HasVideos));
    }

    [RelayCommand]
    private void Select(VideoPlayerItemViewModel? item)
    {
        if (item == null) return;
        SelectedVideo = item;
    }

    internal void RequestPlay(VideoPlayerItemViewModel item)
    {
        SelectedVideo = item;
        OnPlayRequested?.Invoke(item.Id);
    }

    internal void RequestPause(VideoPlayerItemViewModel item)
    {
        SelectedVideo = item;
        OnPauseRequested?.Invoke(item.Id);
    }

    internal void RequestStop(VideoPlayerItemViewModel item)
    {
        SelectedVideo = item;
        OnStopRequested?.Invoke(item.Id);
    }

    internal void RequestSeek(VideoPlayerItemViewModel item, float seconds)
    {
        SelectedVideo = item;
        OnSeekRequested?.Invoke(item.Id, seconds);
    }

    internal void RequestLoop(VideoPlayerItemViewModel item, bool loop)
    {
        SelectedVideo = item;
        OnLoopChanged?.Invoke(item.Id, loop);
    }

    internal void RequestSelect(VideoPlayerItemViewModel item)
        => SelectedVideo = item;

    partial void OnSelectedVideoChanged(VideoPlayerItemViewModel? value)
        => OnPropertyChanged(nameof(HasSelectedVideo));
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
    public string PositionText => FormatPosition(Position, ClipLength);
    public double SeekMaximum => CanSeek ? ClipLength : 1;
    public string UrlDisplay => string.IsNullOrWhiteSpace(Url) ? "No URL" : Url;

    [ObservableProperty] private float _position;
    [ObservableProperty] private bool _isLooping;

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
        SetIfChanged(ref _isPlaying, info.IsPlaying, nameof(IsPlaying), nameof(StateText));

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
    private void Play() => _owner.RequestPlay(this);

    [RelayCommand]
    private void Select() => _owner.RequestSelect(this);

    [RelayCommand]
    private void Pause() => _owner.RequestPause(this);

    [RelayCommand]
    private void Stop() => _owner.RequestStop(this);

    [RelayCommand]
    private void SeekToStart()
    {
        if (CanSeek)
            _owner.RequestSeek(this, 0);
    }

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
