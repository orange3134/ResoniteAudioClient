using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FrooxEngine;
using SkyFrost.Base;

namespace AudioClient.Core.Services;

public record LoginResult(bool IsOK, string Message, bool RequiresTotp = false);

public class AuthService
{
    private readonly Engine _engine;
    private bool _lastLoggedIn;
    private OnlineStatus _lastOnlineStatus = OnlineStatus.Offline;

    public event EventHandler<bool>? LoginStateChanged;
    public event EventHandler<OnlineStatus>? OnlineStatusChanged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal AuthService(Engine engine)
    {
        _engine = engine;
        _lastLoggedIn = engine.Cloud.CurrentUser != null;
        _lastOnlineStatus = CurrentOnlineStatus;
    }

    public bool IsLoggedIn => _engine.Cloud.CurrentUser != null;
    public string? CurrentUsername => _engine.Cloud.CurrentUsername;
    public string? CurrentUserId => _engine.Cloud.CurrentUserID;
    public OnlineStatus CurrentOnlineStatus => IsLoggedIn ? _engine.Cloud.Status.OnlineStatus : OnlineStatus.Offline;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetOnlineStatus(OnlineStatus status)
    {
        if (!IsLoggedIn)
            return;

        if (status == OnlineStatus.Offline)
            return;

        _engine.Cloud.Status.OnlineStatus = status;
        NotifyOnlineStatusChanged();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<LoginResult> LoginAsync(string username, string password, string? totp = null)
    {
        if (_engine.Cloud.CurrentUser != null)
            return new LoginResult(false, $"Already logged in as '{_engine.Cloud.CurrentUsername}'. Use logout first.");

        try
        {
            var auth = new PasswordLogin(password);
            var result = await _engine.Cloud.Session.Login(
                username, auth, _engine.Cloud.SecretMachineId, rememberMe: true, totp: totp);

            if (result.IsOK)
            {
                NotifyLoginChanged();
                return new LoginResult(true, $"Logged in as: {_engine.Cloud.CurrentUsername}");
            }
            if (string.Equals(result.Content, "TOTP", StringComparison.OrdinalIgnoreCase))
                return new LoginResult(false, "A TOTP code is required for this account.", RequiresTotp: true);
            return new LoginResult(false, $"{result.State} - {result.Content}");
        }
        catch (Exception ex)
        {
            return new LoginResult(false, ex.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<LoginResult> LogoutAsync()
    {
        if (_engine.Cloud.CurrentUser == null)
            return new LoginResult(false, "Not currently logged in.");

        try
        {
            await _engine.Cloud.Session.Logout(isManual: true);
            NotifyLoginChanged();
            return new LoginResult(true, "Logged out successfully.");
        }
        catch (Exception ex)
        {
            return new LoginResult(false, ex.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Refresh()
    {
        bool loggedIn = _engine.Cloud.CurrentUser != null;
        if (loggedIn != _lastLoggedIn)
        {
            _lastLoggedIn = loggedIn;
            LoginStateChanged?.Invoke(this, loggedIn);
        }

        var currentStatus = CurrentOnlineStatus;
        if (currentStatus != _lastOnlineStatus)
        {
            _lastOnlineStatus = currentStatus;
            OnlineStatusChanged?.Invoke(this, currentStatus);
        }
    }

    private void NotifyLoginChanged()
    {
        _lastLoggedIn = _engine.Cloud.CurrentUser != null;
        LoginStateChanged?.Invoke(this, _lastLoggedIn);
        NotifyOnlineStatusChanged();
    }

    private void NotifyOnlineStatusChanged()
    {
        _lastOnlineStatus = CurrentOnlineStatus;
        OnlineStatusChanged?.Invoke(this, _lastOnlineStatus);
    }
}
