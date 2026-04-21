using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.GUI.Services;

namespace AudioClient.GUI.ViewModels;

public partial class ResonitePathPromptViewModel : ObservableObject
{
    [ObservableProperty] private string _installPath = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _helperMessage = string.Empty;
    [ObservableProperty] private bool _isBusy = false;

    public Func<Task<string?>>? OnBrowse { get; set; }
    public Action<string>? OnResolved { get; set; }
    public Action? OnCancel { get; set; }

    public ResonitePathPromptViewModel(string? initialPath, string? suggestedPath)
    {
        InstallPath = initialPath ?? suggestedPath ?? string.Empty;
        HelperMessage = string.IsNullOrWhiteSpace(suggestedPath)
            ? "Resonite が既定の Steam インストール先で見つかりませんでした。インストール先フォルダを指定してください。"
            : $"既定では Steam のインストール先を確認します。見つからなかったため、Resonite の場所を指定してください。候補: {suggestedPath}";
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (OnBrowse is null)
            return;

        string? selected = await OnBrowse();
        if (!string.IsNullOrWhiteSpace(selected))
            InstallPath = selected;
    }

    [RelayCommand]
    private void UseDetectedPath()
    {
        if (!string.IsNullOrWhiteSpace(RuntimeBootstrap.SuggestedEngineDir))
            InstallPath = RuntimeBootstrap.SuggestedEngineDir;
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = string.Empty;
        if (!RuntimeBootstrap.IsValidEngineDirectory(InstallPath))
        {
            ErrorMessage = "FrooxEngine.dll と Locale フォルダが見つかる Resonite のインストール先を指定してください。";
            return;
        }

        IsBusy = true;
        try
        {
            RuntimeBootstrap.ApplyEngineDirectory(InstallPath, persist: true);
            OnResolved?.Invoke(RuntimeBootstrap.CurrentEngineDir!);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
        => OnCancel?.Invoke();
}
