using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;

namespace AudioClient.Core.Services;

public record InventoryListing(List<string> Directories, List<string> Records);

public class InventoryService
{
    private readonly Engine _engine;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal InventoryService(Engine engine)
    {
        _engine = engine;
    }

    public bool IsLoggedIn => _engine.Cloud.InventoryRootDirectory != null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<InventoryListing?> ListAsync(string path)
    {
        if (_engine.Cloud.InventoryRootDirectory == null) return null;

        RecordDirectory dir = _engine.Cloud.InventoryRootDirectory;
        if (!string.IsNullOrEmpty(path))
        {
            foreach (string segment in path.Split('/'))
            {
                await dir.EnsureFullyLoaded();
                var sub = dir.Subdirectories.FirstOrDefault(d =>
                    string.Equals(d.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (sub == null) return null;
                dir = sub;
            }
        }
        await dir.EnsureFullyLoaded();
        return new InventoryListing(
            dir.Subdirectories.Select(d => d.Name).ToList(),
            dir.Records.Select(r => r.Name).ToList());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<bool> SpawnAsync(string itemPath)
    {
        if (_engine.Cloud.InventoryRootDirectory == null) return false;
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null || world == Userspace.UserspaceWorld) return false;

        var segments = itemPath.Split('/');
        var itemName = segments.Last();
        var dirSegments = segments.Take(segments.Length - 1).ToArray();

        RecordDirectory dir = _engine.Cloud.InventoryRootDirectory;
        foreach (string segment in dirSegments)
        {
            await dir.EnsureFullyLoaded();
            var sub = dir.Subdirectories.FirstOrDefault(d =>
                string.Equals(d.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (sub == null) return false;
            dir = sub;
        }
        await dir.EnsureFullyLoaded();
        var record = dir.Records.FirstOrDefault(r =>
            string.Equals(r.Name, itemName, StringComparison.OrdinalIgnoreCase));
        if (record == null) return false;

        world.RunSynchronously(() =>
        {
            if (!WorldPermissionsExtensoins.CanSpawnObjects(world)) return;
            Slot s = world.RootSlot.LocalUserSpace.AddSlot("InventorySpawn");
            s.StartTask(async delegate
            {
                await default(ToWorld);
                await s.LoadObjectAsync(record);
                var list = Pool.BorrowList<Slot>();
                SlotPositioning.PositionInFrontOfUser(s, offset: float3.Down * 0.2f, distance: 0.5f);
                s = s.GetComponent<InventoryItem>()?.Unpack(keepExistingPosition: false, list) ?? s;
                Pool.Return(ref list);
            });
        });
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Import(string filePath)
    {
        filePath = filePath.Trim('"', '\'');
        if (!File.Exists(filePath)) return;
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return;
        var localUser = world.LocalUser;
        if (localUser?.Root == null) return;

        var headPos = localUser.Root.HeadPosition;
        var headRot = localUser.Root.HeadFacingRotation;
        var forward = headRot * new float3(0f, 0f, 1f);
        var spawnPos = headPos + forward * 2f;
        UniversalImporter.Import(filePath, world, spawnPos, headRot, silent: true);
    }
}
