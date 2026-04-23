using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AudioClient.Core.Models;
using FrooxEngine;

namespace AudioClient.Core.Services;

public class ChatService
{
    private const int PostReadMaxAttempts = 120;

    private readonly Engine _engine;
    private Slot? _postListSlot;
    private Slot? _prefPostElement;
    private Slot? _prefContentText;
    private World? _lastWorld;
    private readonly string _machineId;

    public bool IsAudioClientWorld { get; private set; }

    // (isAudioClientWorld, initialPosts when becoming true)
    public event EventHandler<(bool IsAcw, List<ChatPostInfo>? InitialPosts)>? AudioClientWorldChanged;
    public event EventHandler<ChatPostInfo>? PostAdded;
    public event EventHandler<string>? PostRemoved;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal ChatService(Engine engine)
    {
        _engine = engine;
        _machineId = Environment.MachineName;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SendTextMessage(string text, string username)
    {
        if (_postListSlot == null || _prefPostElement == null || _prefContentText == null) return;

        var postListSlot = _postListSlot;
        var prefPostElement = _prefPostElement;
        var prefContentText = _prefContentText;
        var machineId = _machineId;

        // Slot.Duplicate と WriteDynamicVariable はワールドのモディフィケーションロックが必要
        postListSlot.World.RunSynchronously(() =>
        {
            if (postListSlot.IsRemoved || prefPostElement.IsRemoved || prefContentText.IsRemoved) return;

            var postSlot = prefPostElement.Duplicate(postListSlot);
            postSlot.PersistentSelf = false;
            postSlot.WriteDynamicVariable("PostElement/Time", DateTime.UtcNow);
            postSlot.WriteDynamicVariable("PostElement/MachineID", machineId);
            postSlot.WriteDynamicVariable("PostElement/Username", username);

            var contentSlot = prefContentText.Duplicate(postSlot);
            contentSlot.PersistentSelf = false;
            contentSlot.WriteDynamicVariable("Content/Type", "Text");
            contentSlot.WriteDynamicVariable("Content/Content", text);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Refresh()
    {
        try
        {
            var world = _engine.WorldManager.FocusedWorld;
            if (world == Userspace.UserspaceWorld) world = null;

            if (world == _lastWorld)
            {
                // Verify slot still valid
                if (IsAudioClientWorld && (_postListSlot == null || _postListSlot.IsRemoved))
                    Detach();
                return;
            }

            Detach();
            _lastWorld = world;
            if (world != null) TryAttach(world);
        }
        catch { }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TryAttach(World world)
    {
        world.RunSynchronously(() =>
        {
            try
            {
                var dynVar = world.RootSlot.GetComponentInChildren<DynamicReferenceVariable<Slot>>(
                    v => v.VariableName.Value == "World/AudioClientData");
                if (dynVar == null) { SetIsAudioClientWorld(false, null); return; }

                var dataSlot = dynVar.Reference.Target;
                if (dataSlot == null || dataSlot.IsRemoved) { SetIsAudioClientWorld(false, null); return; }

                var space = dataSlot.GetComponentInChildren<DynamicVariableSpace>(
                    s => s.CurrentName == "AudioClientData");
                if (space == null) { SetIsAudioClientWorld(false, null); return; }

                if (!space.TryReadValue<Slot>("PostList", out var postListSlot) || postListSlot == null)
                    { SetIsAudioClientWorld(false, null); return; }

                _postListSlot = postListSlot;
                space.TryReadValue<Slot>("Pref.PostElement", out _prefPostElement);
                space.TryReadValue<Slot>("Pref.ContentText", out _prefContentText);

                postListSlot.ChildAdded += OnPostAdded;
                postListSlot.ChildRemoved += OnPostRemoved;

                var initialPosts = ReadAllPosts(postListSlot);
                SetIsAudioClientWorld(true, initialPosts);
            }
            catch { }
        });
    }

    private void Detach()
    {
        if (_postListSlot != null)
        {
            _postListSlot.ChildAdded -= OnPostAdded;
            _postListSlot.ChildRemoved -= OnPostRemoved;
            _postListSlot = null;
        }
        _prefPostElement = null;
        _prefContentText = null;
        SetIsAudioClientWorld(false, null);
    }

    private void SetIsAudioClientWorld(bool value, List<ChatPostInfo>? initialPosts)
    {
        if (IsAudioClientWorld == value && !value) return;
        IsAudioClientWorld = value;
        AudioClientWorldChanged?.Invoke(this, (value, value ? initialPosts : null));
    }

    private void OnPostAdded(Slot parent, Slot child)
    {
        SchedulePostAdded(child, 0);
    }

    private void SchedulePostAdded(Slot child, int attempt)
    {
        _engine.GlobalCoroutineManager.Post(_ =>
        {
            if (child.IsRemoved || _postListSlot == null || child.Parent != _postListSlot) return;

            var post = ParsePost(child);
            if (post != null && IsPostReady(post))
            {
                PostAdded?.Invoke(this, post);
                return;
            }

            if (attempt < PostReadMaxAttempts)
                SchedulePostAdded(child, attempt + 1);
        }, null!);
    }

    private void OnPostRemoved(Slot parent, Slot child)
    {
        PostRemoved?.Invoke(this, child.ReferenceID.ToString());
    }

    private List<ChatPostInfo> ReadAllPosts(Slot postListSlot)
    {
        var result = new List<ChatPostInfo>();
        foreach (var child in postListSlot.Children)
        {
            var post = ParsePost(child);
            if (post != null) result.Add(post);
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ChatPostInfo? ParsePost(Slot slot)
    {
        try
        {
            var space = slot.GetComponent<DynamicVariableSpace>(s => s.SpaceName.Value == "PostElement");
            if (space == null) return null;

            TryReadDynamicValue(space, "Time", out DateTime time);
            TryReadDynamicValue(space, "MachineID", out string? machineId);
            TryReadDynamicValue(space, "Username", out string? username);
            var userId = ResolveUserId(slot.World, username);
            var iconUrl = TryReadIconUrl(space);

            var contents = new List<ChatContent>();
            foreach (var contentSlot in slot.Children)
            {
                var cs = contentSlot.GetComponent<DynamicVariableSpace>(
                    s => s.SpaceName.Value == "Content");
                if (cs == null) continue;

                TryReadDynamicValue(cs, "Type", out string? type);
                if (type == "Text")
                {
                    TryReadDynamicValue(cs, "Content", out string? text);
                    contents.Add(new ChatContent("Text", text, null));
                }
                else if (type == "Image")
                {
                    var imageUrl = TryReadImageUrl(cs);
                    contents.Add(new ChatContent("Image", null, imageUrl));
                }
            }

            return new ChatPostInfo(
                slot.ReferenceID.ToString(),
                time,
                machineId ?? "",
                username ?? "",
                userId,
                iconUrl,
                contents);
        }
        catch { return null; }
    }

    private static bool IsPostReady(ChatPostInfo post)
    {
        if (string.IsNullOrWhiteSpace(post.Username)) return false;
        if (post.Contents.Count == 0) return false;

        foreach (var content in post.Contents)
        {
            if (content.Type == "Text" && string.IsNullOrEmpty(content.Text))
                return false;
        }

        return true;
    }

    private static bool TryReadDynamicValue<T>(DynamicVariableSpace space, string name, out T? value)
    {
        if (space.TryReadValue<T>(name, out var spaceValue))
        {
            value = spaceValue;
            return true;
        }

        var variable = space.Slot.GetComponent<DynamicValueVariable<T>>(
            v => IsVariableNameMatch(v.VariableName.Value, space, name))
            ?? space.Slot.GetComponentInChildren<DynamicValueVariable<T>>(
                v => IsVariableNameMatch(v.VariableName.Value, space, name));

        if (variable != null)
        {
            value = variable.Value.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static bool IsVariableNameMatch(string rawName, DynamicVariableSpace space, string name)
    {
        DynamicVariableHelper.ParsePath(rawName, out var spaceName, out var variableName);
        if (variableName != DynamicVariableHelper.ProcessName(name)) return false;
        return string.IsNullOrWhiteSpace(spaceName) || spaceName == space.CurrentName;
    }

    private static string? ResolveUserId(World world, string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;

        var user = world.AllUsers.FirstOrDefault(u =>
            string.Equals(u.UserName, username, StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.SanitizedUsername, username, StringComparison.OrdinalIgnoreCase));

        return user?.UserID;
    }

    private static string? TryReadIconUrl(DynamicVariableSpace space)
    {
        foreach (var name in new[] { "IconUri", "IconURI", "IconURL", "IconUrl", "Icon", "UserIcon", "ProfileIcon" })
        {
            if (TryReadDynamicValue(space, name, out Uri? uri))
            {
                var url = ToHttpAssetUrl(uri);
                if (url != null) return url;
            }

            if (TryReadDynamicValue(space, name, out string? rawUrl))
            {
                var url = ToHttpAssetUrl(rawUrl);
                if (url != null) return url;
            }

            var providerUrl = TryReadTextureUrl(space, name);
            if (providerUrl != null) return providerUrl;
        }

        return null;
    }

    private static string? TryReadImageUrl(DynamicVariableSpace space)
    {
        foreach (var name in new[] { "Content", "Image", "Texture", "Texture2D" })
        {
            var providerUrl = TryReadTextureUrl(space, name);
            if (providerUrl != null) return providerUrl;
        }

        if (TryReadDynamicValue(space, "Content", out Uri? uri))
            return ToHttpAssetUrl(uri);

        if (TryReadDynamicValue(space, "Content", out string? rawUrl))
            return ToHttpAssetUrl(rawUrl);

        return null;
    }

    private static string? TryReadTextureUrl(DynamicVariableSpace space, string name)
    {
        if (TryReadDynamicReference<IAssetProvider<Texture2D>>(space, name, out var texture2DProvider))
        {
            var url = ToHttpAssetUrl(GetProviderAssetUrl(texture2DProvider));
            if (url != null) return url;
        }

        return null;
    }

    private static Uri? GetProviderAssetUrl(IAssetProvider<Texture2D>? provider)
        => provider?.Asset?.AssetURL;

    private static bool TryReadDynamicReference<T>(DynamicVariableSpace space, string name, out T? value)
        where T : class, IWorldElement
    {
        if (space.TryReadValue<T>(name, out var spaceValue))
        {
            value = spaceValue;
            return true;
        }

        var variable = space.Slot.GetComponent<DynamicReferenceVariable<T>>(
            v => IsVariableNameMatch(v.VariableName.Value, space, name))
            ?? space.Slot.GetComponentInChildren<DynamicReferenceVariable<T>>(
                v => IsVariableNameMatch(v.VariableName.Value, space, name));

        if (variable != null)
        {
            value = variable.Reference.Target;
            return true;
        }

        value = default;
        return false;
    }

    private static string? ToHttpAssetUrl(Uri? uri) => ToHttpAssetUrl(uri?.ToString());

    private static string? ToHttpAssetUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("resdb:///", StringComparison.OrdinalIgnoreCase))
        {
            var path = url.Substring("resdb:///".Length);
            var dot = path.LastIndexOf('.');
            if (dot >= 0) path = path.Substring(0, dot);
            return "https://assets.resonite.com/" + path;
        }

        return url;
    }
}
