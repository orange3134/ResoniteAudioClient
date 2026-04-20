using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AudioClient.Core.Models;
using FrooxEngine;
using SkyFrost.Base;

namespace AudioClient.Core.Services;

public class SessionService
{
    private readonly Engine _engine;
    private List<WorldInfo> _lastSessions = new();

    public event EventHandler<List<WorldInfo>>? SessionListChanged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal SessionService(Engine engine)
    {
        _engine = engine;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<WorldInfo> GetCurrentSessions()
    {
        var list = new List<World>();
        _engine.WorldManager.GetWorlds(list);
        var focused = _engine.WorldManager.FocusedWorld;
        return list
            .Where(w => w != Userspace.UserspaceWorld)
            .Select(w => new WorldInfo(
                w.SessionId ?? "",
                w.Name ?? "(No Name)",
                w.State.ToString(),
                w.AllUsers.Count(),
                w.AccessLevel.ToString(),
                w == focused,
                w.LocalUser?.IsHost ?? false,
                false))
            .ToList();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WorldInfo? GetFocusedSession()
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null || world == Userspace.UserspaceWorld) return null;
        return new WorldInfo(
            world.SessionId ?? "",
            world.Name ?? "(No Name)",
            world.State.ToString(),
            world.AllUsers.Count(),
            world.AccessLevel.ToString(),
            true,
            world.LocalUser?.IsHost ?? false,
            false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<ActiveSessionInfo> GetActiveSessions(bool activeOnly = false)
    {
        var sessions = new List<SessionInfo>();
        _engine.Cloud.Sessions.GetSessions(sessions);
        if (activeOnly) sessions = sessions.Where(s => s.JoinedUsers >= 1).ToList();
        return sessions.Select(s => new ActiveSessionInfo(
            s.Name ?? "(No Name)",
            s.HostUsername ?? "N/A",
            s.JoinedUsers,
            s.MaximumUsers,
            s.AccessLevel.ToString(),
            s.SessionURLs?.FirstOrDefault(u => u.StartsWith("lnl-nat://"))
                ?? s.SessionURLs?.FirstOrDefault(u => u.StartsWith("lnl://"))
                ?? s.SessionURLs?.FirstOrDefault()
                ?? "N/A"
        )).ToList();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Join(string url)
    {
        if (!url.Contains("://")) url = $"resrec:///{url}";
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return;

        if (uri.Scheme == "res-steam")
        {
            string? sessionId = uri.Segments.LastOrDefault()?.TrimEnd('/');
            if (sessionId != null)
            {
                var sessions = new List<SessionInfo>();
                _engine.Cloud.Sessions.GetSessions(sessions);
                var matched = sessions.FirstOrDefault(s =>
                    s.SessionURLs?.Any(u => u.Contains(sessionId)) == true);
                if (matched?.SessionURLs != null)
                {
                    var uris = matched.SessionURLs
                        .Select(u => Uri.TryCreate(u, UriKind.Absolute, out Uri? p) ? p : null)
                        .Where(u => u != null).Select(u => u!)
                        .OrderBy(u => u.Scheme.StartsWith("lnl") ? 0 : 1).ToList();
                    if (uris.Count > 0) { Userspace.JoinSession(uris); return; }
                }
            }
        }
        Userspace.JoinSession(uri);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task StartWorldURLAsync(string recordUrl)
    {
        if (!Uri.TryCreate(recordUrl, UriKind.Absolute, out Uri? uri)) return;
        var settings = new WorldStartSettings(uri);
        await Userspace.OpenWorld(settings);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<string> GetWorldTemplates()
        => WorldPresets.Presets.Select(p => p.Name).ToList();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void StartWorldTemplate(string templateName)
    {
        var preset = WorldPresets.Presets
            .FirstOrDefault(p => p.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        if (preset != null)
            Userspace.StartSession(preset.Method);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Focus(int oneBasedIndex)
    {
        var list = new List<World>();
        _engine.WorldManager.GetWorlds(list);
        var sessions = list.Where(w => w != Userspace.UserspaceWorld).ToList();
        if (oneBasedIndex < 1 || oneBasedIndex > sessions.Count) return;
        var target = sessions[oneBasedIndex - 1];
        if (target.State == World.WorldState.Running)
            _engine.WorldManager.FocusWorld(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void FocusWorld(WorldInfo info)
    {
        var list = new List<World>();
        _engine.WorldManager.GetWorlds(list);
        var target = list.FirstOrDefault(w => w.SessionId == info.Id);
        if (target?.State == World.WorldState.Running)
            _engine.WorldManager.FocusWorld(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Leave()
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world != null && world != Userspace.UserspaceWorld)
            Userspace.ExitWorld(world);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetName(string name)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world != null && world != Userspace.UserspaceWorld)
            world.Name = name;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool SetAccessLevel(string levelName)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null || world == Userspace.UserspaceWorld) return false;
        if (!Enum.TryParse<SessionAccessLevel>(levelName, ignoreCase: true, out var level)) return false;
        world.AccessLevel = level;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Refresh()
    {
        try
        {
            var current = GetCurrentSessions();
            if (!SessionListsEqual(current, _lastSessions))
            {
                _lastSessions = current;
                SessionListChanged?.Invoke(this, current);
            }
        }
        catch { }
    }

    private static bool SessionListsEqual(List<WorldInfo> a, List<WorldInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
