using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AudioClient.Core.Models;
using FrooxEngine;
using FrooxEngine.Store;

namespace AudioClient.Core.Services;

public class ChatService
{
    private const int PostReadMaxAttempts = 120;
    private const int PostReadRetryDelayMs = 100;

    private readonly Engine _engine;
    private Slot? _postListSlot;
    private Slot? _prefPostElement;
    private Slot? _prefContentText;
    private Slot? _prefContentImage;
    private World? _lastWorld;

    public bool IsAudioClientWorld { get; private set; }

    // (isAudioClientWorld, initialPosts when becoming true)
    public event EventHandler<(bool IsAcw, List<ChatPostInfo>? InitialPosts)>? AudioClientWorldChanged;
    public event EventHandler<ChatPostInfo>? PostAdded;
    public event EventHandler<string>? PostRemoved;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal ChatService(Engine engine)
    {
        _engine = engine;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<Uri?> ImportImageAsync(string filePath)
    {
        try
        {
            return await _engine.LocalDB.ImportLocalAssetAsync(
                filePath, LocalDB.ImportLocation.Original).ConfigureAwait(false);
        }
        catch { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SendPost(string? text, Uri? imageUri, string username)
    {
        if (_postListSlot == null || _prefPostElement == null) return;
        if (text == null && imageUri == null) return;

        var postListSlot = _postListSlot;
        var prefPostElement = _prefPostElement;
        var prefContentText = _prefContentText;
        var prefContentImage = _prefContentImage;

        var iconUrlString = _engine.Cloud.CurrentUser?.Profile?.IconUrl;
        Uri.TryCreate(iconUrlString, UriKind.Absolute, out var iconUri);

        postListSlot.World.RunSynchronously(() =>
        {
            if (postListSlot.IsRemoved || prefPostElement.IsRemoved) return;

            var world = postListSlot.World;
            var postSlot = prefPostElement.Duplicate(postListSlot);
            postSlot.PersistentSelf = false;
            postSlot.OrderOffset = (long)(world.Time.WorldTime * 100);
            postSlot.WriteDynamicVariable("PostElement/Time", DateTime.UtcNow);
            postSlot.WriteDynamicVariable("PostElement/MachineID", world.LocalUser?.MachineID ?? "");
            postSlot.WriteDynamicVariable("PostElement/Username", username);
            if (iconUri != null)
                postSlot.WriteDynamicVariable("PostElement/IconUri", iconUri);

            if (text != null && prefContentText != null && !prefContentText.IsRemoved)
            {
                var textSlot = prefContentText.Duplicate(postSlot);
                textSlot.PersistentSelf = false;
                textSlot.WriteDynamicVariable("Content/Type", "Text");
                textSlot.WriteDynamicVariable("Content/Content", text);
            }

            if (imageUri != null && prefContentImage != null && !prefContentImage.IsRemoved)
            {
                var imageSlot = prefContentImage.Duplicate(postSlot);
                imageSlot.PersistentSelf = false;
                var tex = imageSlot.GetComponentInChildren<StaticTexture2D>();
                if (tex is not null)
                    tex.URL.Value = imageUri;
            }
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
                space.TryReadValue<Slot>("Pref.ContentImageWithAsset", out _prefContentImage);

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
        _prefContentImage = null;
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
        _engine.GlobalCoroutineManager.Post(_ => ProcessPostAttempt(child, attempt), null!);
    }

    private void ProcessPostAttempt(Slot child, int attempt)
    {
        if (child.IsRemoved || _postListSlot == null || child.Parent != _postListSlot) return;

        var post = ParsePost(child);
        if (post != null && IsPostReady(post))
        {
            PostAdded?.Invoke(this, post);
            return;
        }

        if (attempt >= PostReadMaxAttempts) return;

        var engine = _engine;
        _ = Task.Delay(PostReadRetryDelayMs).ContinueWith(_ =>
        {
            try { engine.GlobalCoroutineManager.Post(__ => ProcessPostAttempt(child, attempt + 1), null!); }
            catch { }
        });
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
            if (content.Type == "Image" && string.IsNullOrEmpty(content.ImageUrl))
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
                var url = ToAssetUrl(uri);
                if (url != null) return url;
            }

            if (TryReadDynamicValue(space, name, out string? rawUrl))
            {
                var url = ToAssetUrl(rawUrl);
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
            return ToAssetUrl(uri);

        if (TryReadDynamicValue(space, "Content", out string? rawUrl))
            return ToAssetUrl(rawUrl);

        return null;
    }

    private static string? TryReadTextureUrl(DynamicVariableSpace space, string name)
    {
        if (TryReadDynamicReference<IAssetProvider<Texture2D>>(space, name, out var texture2DProvider))
        {
            var url = ToAssetUrl(GetProviderAssetUrl(texture2DProvider));
            if (url != null) return url;
        }
        return null;
    }

    private static Uri? GetProviderAssetUrl(IAssetProvider<Texture2D>? provider)
    {
        if (provider == null) return null;
        if (provider is IStaticAssetProvider staticProvider)
            return staticProvider.URL;
        return provider.Asset?.AssetURL;
    }

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

    private static string? ToAssetUrl(Uri? uri) => ToAssetUrl(uri?.ToString());

    private static string? ToAssetUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("resdb:///", StringComparison.OrdinalIgnoreCase))
        {
            var path = url.Substring("resdb:///".Length);
            var dot = path.LastIndexOf('.');
            if (dot >= 0) path = path.Substring(0, dot);
            return "https://assets.resonite.com/" + path;
        }
        // local:// はそのまま保持（P2P経由でエンジン経由フェッチ）
        return url;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<byte[]?> FetchLocalImageAsync(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            var filePath = await _engine.AssetManager.GatherAssetFile(uri, 0f).ConfigureAwait(false);
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
            return await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
        }
        catch { return null; }
    }
}
