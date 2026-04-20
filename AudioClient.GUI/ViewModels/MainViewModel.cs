using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AudioClient.Core;

namespace AudioClient.GUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private EngineHost? _host;

    [ObservableProperty] private bool _isEngineReady = false;
    [ObservableProperty] private bool _isLoggedIn = false;
    [ObservableProperty] private string _statusMessage = "Initializing engine...";
    [ObservableProperty] private string _currentUsername = "";

    public SessionListViewModel SessionList { get; }
    public SessionDetailViewModel SessionDetail { get; }
    public MemberListViewModel MemberList { get; }
    public StatusBarViewModel StatusBar { get; }
    public LoginViewModel Login { get; }
    public BrowseSessionsViewModel BrowseSessions { get; }

    public MainViewModel(string appDir, string[] args)
    {
        SessionList = new SessionListViewModel();
        SessionDetail = new SessionDetailViewModel();
        MemberList = new MemberListViewModel();
        StatusBar = new StatusBarViewModel();
        Login = new LoginViewModel();
        BrowseSessions = new BrowseSessionsViewModel();

        Task.Run(() => InitializeEngineAsync(appDir, args));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task InitializeEngineAsync(string appDir, string[] args)
    {
        try
        {
            var host = await EngineHost.StartAsync(
                appDir, args,
                progress: new AudioClient.Core.EngineInitProgress(
                    msg => UpdateStatus(msg),
                    msg => UpdateStatus(msg),
                    () => UpdateStatus("Engine ready.")));

            _host = host;

            host.Auth.LoginStateChanged += (_, loggedIn) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => OnLoginStateChanged(loggedIn));
            host.Sessions.SessionListChanged += (_, sessions) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => SessionList.Update(sessions));
            host.Users.UsersChanged += (_, users) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => MemberList.Update(users));
            host.Audio.MuteChanged += (_, muted) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => StatusBar.IsMuted = muted);
            host.Audio.VolumeChanged += (_, vol) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => StatusBar.UpdateVolumes(vol));

            // Hook up child VMs to host actions
            SessionList.OnFocusRequested = info => host.PostToEngine(() => host.Sessions.FocusWorld(info));
            SessionList.OnLeaveRequested = () => host.PostToEngine(() => host.Sessions.Leave());
            SessionList.OnNewSessionRequested = template => host.PostToEngine(() => host.Sessions.StartWorldTemplate(template));
            SessionList.GetTemplates = () => host.Sessions.GetWorldTemplates();
            SessionDetail.OnLeaveRequested = () => host.PostToEngine(() => host.Sessions.Leave());
            SessionDetail.OnSetName = name => host.PostToEngine(() => host.Sessions.SetName(name));
            SessionDetail.OnSetAccessLevel = level => host.PostToEngine(() => host.Sessions.SetAccessLevel(level));
            StatusBar.OnToggleMute = () => host.PostToEngine(() => host.Audio.ToggleMute());
            StatusBar.OnSetVolume = v => host.PostToEngine(() => host.Audio.SetMasterVolume(v));
            StatusBar.OnShowLogin = () => _ = Dispatcher.UIThread.InvokeAsync(() => Login.ShowLogin());
            BrowseSessions.OnRefreshRequested = () =>
            {
                _ = Dispatcher.UIThread.InvokeAsync(() => BrowseSessions.IsLoading = true);
                var sessions = host.Sessions.GetActiveSessions();
                _ = Dispatcher.UIThread.InvokeAsync(() => BrowseSessions.Update(sessions, BrowseSessions.SearchText));
            };
            BrowseSessions.OnJoinRequested = url => host.PostToEngine(() => host.Sessions.Join(url));
            Login.OnLogin = async (u, p) =>
            {
                var result = await host.Auth.LoginAsync(u, p);
                return result;
            };
            Login.OnLogout = async () => await host.Auth.LogoutAsync();

            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsEngineReady = true;
                IsLoggedIn = host.Auth.IsLoggedIn;
                CurrentUsername = host.Auth.CurrentUsername ?? "";
                StatusMessage = "Connected";
                Login.IsVisible = !host.Auth.IsLoggedIn;
                SessionList.Update(host.Sessions.GetCurrentSessions());
                MemberList.Update(host.Users.GetCurrentUsers());
                StatusBar.IsMuted = host.Audio.IsMuted;
                var vol = host.Audio.GetVolumes();
                if (vol != null) StatusBar.UpdateVolumes(vol);
            });
        }
        catch (Exception ex)
        {
            _ = Dispatcher.UIThread.InvokeAsync(() => StatusMessage = $"Error: {ex.Message}");
        }
    }

    private void UpdateStatus(string msg)
        => _ = Dispatcher.UIThread.InvokeAsync(() => StatusMessage = msg);

    private void OnLoginStateChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        CurrentUsername = _host?.Auth.CurrentUsername ?? "";
        Login.IsVisible = !loggedIn;
    }
}
