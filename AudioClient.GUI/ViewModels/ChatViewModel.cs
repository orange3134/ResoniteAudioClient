using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AudioClient.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioClient.GUI.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    [ObservableProperty] private string _inputText = "";

    public ObservableCollection<ChatPostItemViewModel> Posts { get; } = new();

    public Action<string>? OnSendRequested { get; set; }

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
            Posts.Add(new ChatPostItemViewModel(post));
    }

    public void AddPost(ChatPostInfo post)
    {
        // Avoid duplicates (e.g. our own send already visible via ChildAdded)
        if (Posts.Any(p => p.SlotId == post.SlotId)) return;
        Posts.Add(new ChatPostItemViewModel(post));
    }

    public void RemovePost(string slotId)
    {
        var item = Posts.FirstOrDefault(p => p.SlotId == slotId);
        if (item != null) Posts.Remove(item);
    }

    public void ClearPosts() => Posts.Clear();
}

public class ChatPostItemViewModel
{
    public string SlotId { get; }
    public string Username { get; }
    public string IconLetter { get; }
    public string TimeText { get; }
    public string? IconUrl { get; }
    public List<ChatContent> Contents { get; }

    public string TextContent => string.Join("\n",
        Contents.Where(c => c.Type == "Text").Select(c => c.Text ?? ""));

    public bool HasTextContent => Contents.Any(c => c.Type == "Text");
    public bool HasImageContent => Contents.Any(c => c.Type == "Image");

    public ChatPostItemViewModel(ChatPostInfo post)
    {
        SlotId = post.SlotId;
        Username = post.Username;
        IconLetter = string.IsNullOrEmpty(post.Username) ? "?" : post.Username[0].ToString().ToUpper();
        TimeText = post.Time.ToLocalTime().ToString("HH:mm");
        IconUrl = post.IconUrl;
        Contents = post.Contents;
    }
}
