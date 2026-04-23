using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AudioClient.Core.Models;
using FrooxEngine;

namespace AudioClient.Core.Services;

public class ChatService
{
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
        // First tick: let SpaceName → CurrentName (UpdateName) run
        // Second tick: let DynamicValueVariable.UpdateLinking register values into the space
        _engine.GlobalCoroutineManager.Post(_ =>
        {
            _engine.GlobalCoroutineManager.Post(_ =>
            {
                if (child.IsRemoved || _postListSlot == null) return;
                var post = ParsePost(child);
                if (post != null) PostAdded?.Invoke(this, post);
            }, null!);
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

            space.TryReadValue<DateTime>("Time", out var time);
            space.TryReadValue<string>("MachineID", out var machineId);
            space.TryReadValue<string>("Username", out var username);
            space.TryReadValue<Uri>("IconUrl", out var iconUri);

            var contents = new List<ChatContent>();
            foreach (var contentSlot in slot.Children)
            {
                var cs = contentSlot.GetComponent<DynamicVariableSpace>(
                    s => s.SpaceName.Value == "Content");
                if (cs == null) continue;

                cs.TryReadValue<string>("Type", out var type);
                if (type == "Text")
                {
                    cs.TryReadValue<string>("Content", out var text);
                    contents.Add(new ChatContent("Text", text, null));
                }
                else if (type == "Image")
                {
                    contents.Add(new ChatContent("Image", null, null));
                }
            }

            return new ChatPostInfo(
                slot.ReferenceID.ToString(),
                time,
                machineId ?? "",
                username ?? "",
                iconUri?.ToString(),
                contents);
        }
        catch { return null; }
    }
}
