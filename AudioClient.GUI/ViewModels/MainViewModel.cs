using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core;

namespace AudioClient.GUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private EngineHost? _host;

    [ObservableProperty] private bool _isEngineReady = false;
    [ObservableProperty] private bool _isLoggedIn = false;
    [ObservableProperty] private bool _isShuttingDown = false;
    [ObservableProperty] private string _statusMessage = "Initializing engine...";
    [ObservableProperty] private string _currentUsername = "";
    [ObservableProperty] private bool _isBrowseTabSelected = true;
    [ObservableProperty] private bool _isContactsTabSelected = false;

    public SessionListViewModel SessionList { get; }
    public SessionDetailViewModel SessionDetail { get; }
    public MemberListViewModel MemberList { get; }
    public StatusBarViewModel StatusBar { get; }
    public LoginViewModel Login { get; }
    public BrowseSessionsViewModel BrowseSessions { get; }
    public ContactListViewModel ContactList { get; }
    public SessionPreviewViewModel SessionPreview { get; }
    public UserInfoViewModel UserInfoPopup { get; }

    public MainViewModel(string appDir, string[] args)
    {
        SessionList = new SessionListViewModel();
        SessionDetail = new SessionDetailViewModel();
        MemberList = new MemberListViewModel();
        StatusBar = new StatusBarViewModel();
        Login = new LoginViewModel();
        BrowseSessions = new BrowseSessionsViewModel();
        ContactList = new ContactListViewModel();
        SessionPreview = new SessionPreviewViewModel();
        UserInfoPopup = new UserInfoViewModel();

        Task.Run(() => InitializeEngineAsync(appDir, args));
    }

    [RelayCommand]
    private void SelectLeftTab(string tab)
    {
        IsBrowseTabSelected = tab == "Browse";
        IsContactsTabSelected = tab == "Contacts";
        if (tab == "Browse")
            BrowseSessions.RefreshCommand.Execute(null);
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
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SessionList.Update(sessions);
                    SessionDetail.Update(sessions.FirstOrDefault(s => s.IsFocused));
                });
            host.Users.UsersChanged += (_, users) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => MemberList.Update(users));
            host.Users.VoiceModeChanged += (_, mode) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => StatusBar.CurrentVoiceMode = mode ?? "Normal");
            host.Audio.MuteChanged += (_, muted) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => StatusBar.IsMuted = muted);
            host.Audio.VolumeChanged += (_, vol) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => StatusBar.UpdateVolumes(vol));
            host.Contacts.ContactsChanged += (_, contacts) =>
                _ = Dispatcher.UIThread.InvokeAsync(() => ContactList.Update(contacts));

            // Session list actions
            SessionList.OnFocusRequested = info => host.PostToEngine(() => host.Sessions.FocusWorld(info));
            SessionList.OnLeaveRequested = () => host.PostToEngine(() => host.Sessions.Leave());
            SessionList.OnNewSessionRequested = template => host.PostToEngine(() => host.Sessions.StartWorldTemplate(template));
            SessionList.GetTemplates = () => host.Sessions.GetWorldTemplates();

            // Session detail actions
            SessionDetail.OnLeaveRequested = () => host.PostToEngine(() => host.Sessions.Leave());
            SessionDetail.OnSetName = name => host.PostToEngine(() => host.Sessions.SetName(name));
            SessionDetail.OnSetAccessLevel = level => host.PostToEngine(() => host.Sessions.SetAccessLevel(level));

            // Status bar actions
            StatusBar.OnToggleMute = () => host.PostToEngine(() => host.Audio.ToggleMute());
            StatusBar.OnSetVolume = v => host.PostToEngine(() => host.Audio.SetMasterVolume(v));
            StatusBar.OnSetSoundEffectVolume = v => host.PostToEngine(() => host.Audio.SetSoundEffectVolume(v));
            StatusBar.OnSetMultimediaVolume = v => host.PostToEngine(() => host.Audio.SetMultimediaVolume(v));
            StatusBar.OnSetVoiceVolume = v => host.PostToEngine(() => host.Audio.SetVoiceVolume(v));
            StatusBar.OnSetUIVolume = v => host.PostToEngine(() => host.Audio.SetUIVolume(v));
            StatusBar.OnSetVoiceMode = mode => host.PostToEngine(() => host.Users.SetVoiceMode(mode));
            StatusBar.OnShowLogin = () => _ = Dispatcher.UIThread.InvokeAsync(() =>
                Login.ShowLogin(host.Auth.IsLoggedIn, host.Auth.CurrentUsername ?? ""));
            StatusBar.OnGetInputDevices = () => host.Audio.GetInputDevices();
            StatusBar.OnGetOutputDevices = () => host.Audio.GetOutputDevices();
            StatusBar.OnSetInputDevice = index => host.PostToEngine(() => host.Audio.SetInputDevice(index));
            StatusBar.OnSetOutputDevice = index => host.PostToEngine(() => host.Audio.SetOutputDevice(index));

            // Browse sessions: click → preview
            BrowseSessions.OnRefreshRequested = () =>
            {
                _ = Dispatcher.UIThread.InvokeAsync(() => BrowseSessions.IsLoading = true);
                var sessions = host.Sessions.GetActiveSessions(BrowseSessions.ActiveOnly);
                _ = Dispatcher.UIThread.InvokeAsync(() => BrowseSessions.Update(sessions, BrowseSessions.SearchText));
            };
            BrowseSessions.OnPreviewRequested = info =>
                _ = Dispatcher.UIThread.InvokeAsync(() => SessionPreview.ShowFromBrowse(info));

            // Contact list: click → preview
            ContactList.OnPreviewRequested = item =>
                _ = Dispatcher.UIThread.InvokeAsync(() => SessionPreview.ShowFromContact(item.Info));

            // Session preview: join button
            SessionPreview.OnJoinRequested = url => host.PostToEngine(() => host.Sessions.Join(url));

            // Login
            Login.OnLogin = async (u, p) => await host.Auth.LoginAsync(u, p);
            Login.OnLogout = async () => await host.Auth.LogoutAsync();

            // Icon fetch callback (shared for both panels)
            Func<string, Task<string?>> fetchIcon = userId => host.Contacts.GetUserIconUrlAsync(userId);
            MemberList.FetchIconUrl = fetchIcon;
            ContactList.FetchIconUrl = fetchIcon;

            // Member list
            MemberList.OnMoveToRequested = item =>
                host.PostToEngine(() => host.Users.MoveToUser(item.UserName));
            MemberList.OnShowInfoRequested = item =>
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                    UserInfoPopup.Show(item.UserName, item.UserId, item.IsContact));
            UserInfoPopup.OnAddContact = async (userId, username) =>
                await host.Contacts.AddContactAsync(userId, username);

            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsEngineReady = true;
                IsLoggedIn = host.Auth.IsLoggedIn;
                CurrentUsername = host.Auth.CurrentUsername ?? "";
                StatusMessage = "Connected";
                Login.IsLoggedIn = host.Auth.IsLoggedIn;
                Login.LoggedInUsername = CurrentUsername;
                Login.IsVisible = !host.Auth.IsLoggedIn;
                var currentSessions = host.Sessions.GetCurrentSessions();
                SessionList.Update(currentSessions);
                SessionDetail.Update(currentSessions.FirstOrDefault(s => s.IsFocused));
                MemberList.Update(host.Users.GetCurrentUsers());
                ContactList.Update(host.Contacts.GetOnlineContacts());
                StatusBar.IsMuted = host.Audio.IsMuted;
                var vol = host.Audio.GetVolumes();
                if (vol != null) StatusBar.UpdateVolumes(vol);
                var voiceMode = host.Users.GetVoiceMode();
                if (voiceMode != null) StatusBar.CurrentVoiceMode = voiceMode;
            });

            // Auto-refresh Browse sessions on startup (cloud API call, run off UI thread)
            BrowseSessions.OnRefreshRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _ = Dispatcher.UIThread.InvokeAsync(() => StatusMessage = $"Error: {ex.Message}");
        }
    }

    private void UpdateStatus(string msg)
        => _ = Dispatcher.UIThread.InvokeAsync(() => StatusMessage = msg);

    public async Task ShutdownAsync()
    {
        IsShuttingDown = true;
        if (_host != null)
            await Task.Run(() => _host.Shutdown());
    }

    private void OnLoginStateChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        CurrentUsername = _host?.Auth.CurrentUsername ?? "";
        Login.IsLoggedIn = loggedIn;
        Login.LoggedInUsername = CurrentUsername;
        if (loggedIn)
            Login.IsVisible = false;
    }
}
