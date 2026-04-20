using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class SessionItemViewModel : ObservableObject
{
    public WorldInfo Info { get; }
    public string Name => Info.Name;
    public bool IsFocused => Info.IsFocused;
    public string StateLabel => Info.State;
    public int UserCount => Info.UserCount;

    public SessionItemViewModel(WorldInfo info) => Info = info;
}

public partial class SessionListViewModel : ObservableObject
{
    [ObservableProperty] private SessionItemViewModel? _selectedSession;
    [ObservableProperty] private bool _isNewSessionPopupOpen = false;
    [ObservableProperty] private List<string> _availableTemplates = new();

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = new();

    public Action<WorldInfo>? OnFocusRequested { get; set; }
    public Action? OnLeaveRequested { get; set; }
    public Action<string>? OnNewSessionRequested { get; set; }
    public Func<List<string>>? GetTemplates { get; set; }

    public void Update(List<WorldInfo> sessions)
    {
        Sessions.Clear();
        foreach (var s in sessions)
            Sessions.Add(new SessionItemViewModel(s));
    }

    [RelayCommand]
    private void SelectSession(SessionItemViewModel? item)
    {
        if (item == null) return;
        SelectedSession = item;
        OnFocusRequested?.Invoke(item.Info);
    }

    [RelayCommand]
    private void LeaveSession()
    {
        OnLeaveRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenNewSession()
    {
        AvailableTemplates = GetTemplates?.Invoke() ?? new List<string>();
        IsNewSessionPopupOpen = true;
    }

    [RelayCommand]
    private void StartTemplate(string templateName)
    {
        IsNewSessionPopupOpen = false;
        OnNewSessionRequested?.Invoke(templateName);
    }
}
