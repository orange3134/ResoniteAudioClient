using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioClient.GUI.ViewModels;

public partial class UserInfoViewModel : ObservableObject
{
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string? _userId;
    [ObservableProperty] private bool _isContact = false;
    [ObservableProperty] private bool _isVisible = false;
    [ObservableProperty] private bool _isAdding = false;

    public Func<string, string, Task>? OnAddContact { get; set; }

    [RelayCommand]
    private void Close() => IsVisible = false;

    [RelayCommand]
    private async Task AddContactAsync()
    {
        if (UserId == null) return;
        IsAdding = true;
        try
        {
            await (OnAddContact?.Invoke(UserId, UserName) ?? Task.CompletedTask);
            IsContact = true;
        }
        finally
        {
            IsAdding = false;
        }
    }

    public void Show(string userName, string? userId, bool isContact)
    {
        UserName = userName;
        UserId = userId;
        IsContact = isContact;
        IsVisible = true;
    }
}
