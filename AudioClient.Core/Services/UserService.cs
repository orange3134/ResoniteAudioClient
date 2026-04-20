using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AudioClient.Core.Models;
using FrooxEngine;
using UserInfo = AudioClient.Core.Models.UserInfo;

namespace AudioClient.Core.Services;

public class UserService
{
    private readonly Engine _engine;
    private List<UserInfo> _lastUsers = new();

    public event EventHandler<List<UserInfo>>? UsersChanged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal UserService(Engine engine)
    {
        _engine = engine;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<UserInfo> GetCurrentUsers()
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null || world == Userspace.UserspaceWorld) return new List<UserInfo>();
        return world.AllUsers.Select(u => new UserInfo(
            u.UserName, u.UserID, u.IsHost, u.IsLocalUser, u.IsPresentInWorld, u.Ping)).ToList();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void MoveToUser(string targetUserName)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return;
        var localUser = world.LocalUser;
        if (localUser?.Root == null) return;

        User? targetUser = null;
        foreach (var user in world.AllUsers)
        {
            if (string.Equals(user.UserName, targetUserName, StringComparison.OrdinalIgnoreCase))
            { targetUser = user; break; }
        }
        if (targetUser == null || targetUser.IsLocalUser || targetUser.Root == null) return;

        var capturedTarget = targetUser.Root;
        var capturedLocal = localUser.Root;
        world.RunSynchronously(() =>
        {
            var headPos = capturedTarget.HeadPosition;
            capturedLocal.JumpToPoint(headPos, 1.0f);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<string> GetLocomotionModules()
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return new List<string>();
        var localUser = world.LocalUser;
        if (localUser?.Root == null) return new List<string>();
        var loco = localUser.Root.Slot.GetComponentInChildren<LocomotionController>();
        if (loco == null) return new List<string>();
        var result = new List<string>();
        foreach (var m in loco.LocomotionModules)
        {
            if (m == null) continue;
            string name;
            try { name = m.LocomotionName.ToString(); } catch { name = m.GetType().Name; }
            bool isActive = loco.ActiveModule == m;
            result.Add(isActive ? $"[ACTIVE] {name}" : name);
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetLocomotionModule(string targetName)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return;
        var localUser = world.LocalUser;
        if (localUser?.Root == null) return;
        var loco = localUser.Root.Slot.GetComponentInChildren<LocomotionController>();
        if (loco == null) return;

        ILocomotionModule? target = null;
        foreach (var m in loco.LocomotionModules)
        {
            if (m == null) continue;
            string name;
            try { name = m.LocomotionName.ToString(); } catch { name = m.GetType().Name; }
            if (name.ToLowerInvariant().Contains(targetName.ToLowerInvariant()) ||
                m.GetType().Name.ToLowerInvariant().Contains(targetName.ToLowerInvariant()))
            { target = m; break; }
        }
        if (target != null)
            world.RunSynchronously(() => loco.ActiveModule = target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string? GetVoiceMode()
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return null;
        return world.LocalUser?.VoiceMode.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool SetVoiceMode(string modeName)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return false;
        var localUser = world.LocalUser;
        if (localUser == null) return false;

        VoiceMode targetMode = modeName.ToLowerInvariant() switch
        {
            "normal" => VoiceMode.Normal,
            "shout" => VoiceMode.Shout,
            "broadcast" => VoiceMode.Broadcast,
            "whisper" => VoiceMode.Whisper,
            "mute" => VoiceMode.Mute,
            _ => (VoiceMode)(-1)
        };
        if ((int)targetMode == -1) return false;

        world.RunSynchronously(() => localUser.VoiceMode = targetMode);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Refresh()
    {
        try
        {
            var current = GetCurrentUsers();
            if (!UserListsEqual(current, _lastUsers))
            {
                _lastUsers = current;
                UsersChanged?.Invoke(this, current);
            }
        }
        catch { }
    }

    private static bool UserListsEqual(List<UserInfo> a, List<UserInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
