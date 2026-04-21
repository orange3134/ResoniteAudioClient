using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Services;

namespace AudioClient.GUI.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _totpCode = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isLoggingIn = false;
    [ObservableProperty] private bool _isVisible = false;
    [ObservableProperty] private bool _isLoggedIn = false;
    [ObservableProperty] private string _loggedInUsername = "";
    [ObservableProperty] private bool _requiresTotp = false;

    public Func<string, string, string?, Task<LoginResult>>? OnLogin { get; set; }
    public Func<Task<LoginResult>>? OnLogout { get; set; }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Username and password are required.";
            return;
        }
        if (RequiresTotp && string.IsNullOrWhiteSpace(TotpCode))
        {
            ErrorMessage = "TOTP code is required.";
            return;
        }
        IsLoggingIn = true;
        ErrorMessage = "";
        try
        {
            var result = await (OnLogin?.Invoke(Username, Password, RequiresTotp ? TotpCode : null) ?? Task.FromResult(new LoginResult(false, "Not connected")));
            if (result.IsOK)
            {
                Password = "";
                TotpCode = "";
                RequiresTotp = false;
                IsVisible = false;
            }
            else
            {
                RequiresTotp = result.RequiresTotp;
                if (!result.RequiresTotp)
                    TotpCode = "";
                ErrorMessage = result.Message;
            }
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        IsLoggingIn = true;
        try
        {
            await (OnLogout?.Invoke() ?? Task.CompletedTask);
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private void Close() => IsVisible = false;

    public void ShowLogin(bool isLoggedIn = false, string username = "")
    {
        IsLoggedIn = isLoggedIn;
        LoggedInUsername = username;
        ErrorMessage = "";
        RequiresTotp = false;
        TotpCode = "";
        if (!isLoggedIn)
            Password = "";
        IsVisible = true;
    }
}
