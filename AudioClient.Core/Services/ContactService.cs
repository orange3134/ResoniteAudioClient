using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AudioClient.Core.Models;
using FrooxEngine;
using SkyFrost.Base;

namespace AudioClient.Core.Services;

public class ContactService
{
    private readonly Engine _engine;
    private List<ContactInfo> _lastContacts = new();

    public event EventHandler<List<ContactInfo>>? ContactsChanged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal ContactService(Engine engine)
    {
        _engine = engine;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<ContactInfo> GetOnlineContacts()
    {
        var result = new List<ContactInfo>();
        _engine.Cloud.Contacts.ForeachContactData(cd =>
        {
            var status = cd.CurrentStatus;
            var onlineStatus = status?.OnlineStatus ?? OnlineStatus.Offline;
            if (onlineStatus == OnlineStatus.Offline || onlineStatus == OnlineStatus.Invisible) return;
            var si = cd.CurrentSessionInfo;
            var siUrls = si?.GetSessionURLs();
            var sessionUrl = siUrls?.FirstOrDefault(u => u.Scheme.StartsWith("lnl"))?.ToString()
                ?? siUrls?.FirstOrDefault()?.ToString();
            result.Add(new ContactInfo(
                cd.Contact.ContactUsername,
                cd.Contact.ContactUserId,
                onlineStatus.ToString(),
                si?.Name,
                si?.HostUsername,
                si?.JoinedUsers ?? 0,
                si?.MaximumUsers ?? 0,
                si?.AccessLevel.ToString(),
                sessionUrl));
        });
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public (ContactInfo? Info, List<ContactSessionMeta> Sessions) GetContactDetail(string username)
    {
        SkyFrost.Base.ContactData? cd = null;
        _engine.Cloud.Contacts.ForeachContactData(c =>
        {
            if (cd == null && c.Contact.ContactUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                cd = c;
        });
        if (cd == null) return (null, new List<ContactSessionMeta>());

        var status = cd.CurrentStatus;
        var onlineStatus = status?.OnlineStatus ?? OnlineStatus.Offline;
        var si = cd.CurrentSessionInfo;
        var siUrls2 = si?.GetSessionURLs();
        var sessionUrl2 = siUrls2?.FirstOrDefault(u => u.Scheme.StartsWith("lnl"))?.ToString()
            ?? siUrls2?.FirstOrDefault()?.ToString();
        var info = new ContactInfo(
            cd.Contact.ContactUsername, cd.Contact.ContactUserId,
            onlineStatus.ToString(), si?.Name, si?.HostUsername,
            si?.JoinedUsers ?? 0, si?.MaximumUsers ?? 0,
            si?.AccessLevel.ToString(), sessionUrl2);

        var sessions = status?.Sessions?.Select(s =>
            new ContactSessionMeta(s.IsHost, s.SessionHidden, s.AccessLevel.ToString())
        ).ToList() ?? new List<ContactSessionMeta>();

        return (info, sessions);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<bool> InviteAsync(string username)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null || world == Userspace.UserspaceWorld) return false;

        SkyFrost.Base.ContactData? target = null;
        _engine.Cloud.Contacts.ForeachContactData(cd =>
        {
            if (target == null && cd.Contact.ContactUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                target = cd;
        });
        if (target == null) return false;

        string userId = target.Contact.ContactUserId;
        world.RunSynchronously(() => world.AllowUserToJoin(userId));
        var sessionInfo = world.GenerateSessionInfo();
        var messages = _engine.Cloud.Messages.GetUserMessages(userId);
        return await messages.SendInviteMessage(sessionInfo);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task AddContactAsync(string userId, string username)
    {
        await _engine.Cloud.Contacts.AddContact(userId, username);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool IsContact(string? userId)
    {
        if (userId == null) return false;
        bool found = false;
        _engine.Cloud.Contacts.ForeachContactData(cd =>
        {
            if (cd.Contact.ContactUserId == userId) found = true;
        });
        return found;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool JoinContactSession(string username)
    {
        SkyFrost.Base.ContactData? target = null;
        _engine.Cloud.Contacts.ForeachContactData(cd =>
        {
            if (target == null && cd.Contact.ContactUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                target = cd;
        });
        if (target == null) return false;

        var sessionInfo = target.CurrentSessionInfo;
        if (sessionInfo == null) return false;

        var urls = sessionInfo.GetSessionURLs();
        if (urls == null || urls.Count == 0) return false;

        Userspace.JoinSession(urls);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Refresh()
    {
        try
        {
            var current = GetOnlineContacts();
            if (!ContactListsEqual(current, _lastContacts))
            {
                _lastContacts = current;
                ContactsChanged?.Invoke(this, current);
            }
        }
        catch { }
    }

    private static bool ContactListsEqual(List<ContactInfo> a, List<ContactInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
