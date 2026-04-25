using System;
using System.Collections.Generic;
using AudioClient.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioClient.GUI.ViewModels;

public partial class NewSessionViewModel : ObservableObject
{
    [ObservableProperty] private bool _isVisible = false;
    [ObservableProperty] private string _sessionName = "New Session";
    [ObservableProperty] private int _maxUsers = 16;
    [ObservableProperty] private string _selectedAccessLevel = "Contacts";
    [ObservableProperty] private string _selectedTemplate = "AudioClientWorld";
    [ObservableProperty] private string _worldRecordUrl = "";
    [ObservableProperty] private bool _isStarting = false;

    public bool IsWorldRecordTemplate => SelectedTemplate == "WorldRecord";

    partial void OnSelectedTemplateChanged(string value)
        => OnPropertyChanged(nameof(IsWorldRecordTemplate));

    public List<string> AccessLevels { get; } =
        ["Private", "Contacts", "ContactsPlus", "RegisteredUsers", "Anyone"];

    public Func<NewSessionSettings, System.Threading.Tasks.Task>? OnStartRequested { get; set; }

    public void Show()
    {
        SessionName = "New Session";
        MaxUsers = 16;
        SelectedAccessLevel = "Contacts";
        SelectedTemplate = "AudioClientWorld";
        WorldRecordUrl = "";
        IsStarting = false;
        IsVisible = true;
    }

    [RelayCommand]
    private void SetAccessLevel(string level) => SelectedAccessLevel = level;

    [RelayCommand]
    private void SetTemplate(string template) => SelectedTemplate = template;

    [RelayCommand]
    private async System.Threading.Tasks.Task Start()
    {
        if (SelectedTemplate == "WorldRecord" && string.IsNullOrWhiteSpace(WorldRecordUrl)) return;
        if (IsStarting) return;

        var name = string.IsNullOrWhiteSpace(SessionName) ? "New Session" : SessionName.Trim();
        var settings = new NewSessionSettings(
            name,
            MaxUsers,
            SelectedAccessLevel,
            SelectedTemplate,
            SelectedTemplate == "WorldRecord" ? WorldRecordUrl.Trim() : null);

        IsStarting = true;
        IsVisible = false;

        if (OnStartRequested != null)
            await OnStartRequested(settings);

        IsStarting = false;
    }

    [RelayCommand]
    private void Cancel() => IsVisible = false;
}
