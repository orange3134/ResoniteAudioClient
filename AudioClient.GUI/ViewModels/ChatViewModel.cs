using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AudioClient.Core.Models;
using AudioClient.GUI.Helpers;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static AudioClient.GUI.Helpers.ResoniteRichText;

namespace AudioClient.GUI.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    [ObservableProperty] private string _inputText = "";

    public ObservableCollection<ChatPostItemViewModel> Posts { get; } = new();

    public Action<string>? OnSendRequested { get; set; }
    public Func<string, Task<string?>>? FetchIconUrl { get; set; }

    [RelayCommand]
    private void Send()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        OnSendRequested?.Invoke(text);
        InputText = "";
    }

    public void LoadPosts(List<ChatPostInfo> posts)
    {
        Posts.Clear();
        foreach (var post in posts)
            Posts.Add(new ChatPostItemViewModel(post, FetchIconUrl));
    }

    public void AddPost(ChatPostInfo post)
    {
        // Avoid duplicates (e.g. our own send already visible via ChildAdded)
        if (Posts.Any(p => p.SlotId == post.SlotId)) return;
        Posts.Add(new ChatPostItemViewModel(post, FetchIconUrl));
    }

    public void RemovePost(string slotId)
    {
        var item = Posts.FirstOrDefault(p => p.SlotId == slotId);
        if (item != null) Posts.Remove(item);
    }

    public void ClearPosts() => Posts.Clear();
}

public partial class ChatPostItemViewModel : ObservableObject
{
    public string SlotId { get; }
    public string Username { get; }
    public string IconLetter { get; }
    public string TimeText { get; }
    public string? UserId { get; }
    public string? IconUrl { get; }
    public List<ChatContentItemViewModel> Contents { get; }

    public string TextContent => string.Join("\n",
        Contents.Where(c => c.Type == "Text").Select(c => c.Text ?? ""));

    public bool HasTextContent => Contents.Any(c => c.Type == "Text");
    public bool HasImageContent => Contents.Any(c => c.Type == "Image");

    [ObservableProperty] private Bitmap? _iconBitmap;

    private readonly Func<string, Task<string?>>? _fetchIconUrl;

    public ChatPostItemViewModel(ChatPostInfo post, Func<string, Task<string?>>? fetchIconUrl)
    {
        SlotId = post.SlotId;
        Username = post.Username;
        UserId = post.UserId;
        _fetchIconUrl = fetchIconUrl;
        var plainUsername = StripTags(post.Username);
        IconLetter = string.IsNullOrEmpty(plainUsername) ? "?" : plainUsername[0].ToString().ToUpperInvariant();
        TimeText = post.Time.ToLocalTime().ToString("HH:mm");
        IconUrl = post.IconUrl;
        Contents = post.Contents.Select(c => new ChatContentItemViewModel(c)).ToList();
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        var url = IconUrl;
        if (url == null && UserId != null && _fetchIconUrl != null)
            url = await _fetchIconUrl(UserId).ConfigureAwait(false);

        IconBitmap = await IconLoader.LoadAsync(url).ConfigureAwait(false);
    }
}

public partial class ChatContentItemViewModel : ObservableObject
{
    public string Type { get; }
    public string? Text { get; }
    public string? ImageUrl { get; }
    public bool IsImage => Type == "Image";

    [ObservableProperty] private Bitmap? _imageBitmap;

    public ChatContentItemViewModel(ChatContent content)
    {
        Type = content.Type;
        Text = content.Text;
        ImageUrl = content.ImageUrl;
        if (IsImage)
            _ = LoadImageAsync();
    }

    private async Task LoadImageAsync()
    {
        ImageBitmap = await IconLoader.LoadAsync(ImageUrl).ConfigureAwait(false);
    }
}
