using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AudioClient.Core;
using AudioClient.Core.Services;

namespace AudioClient;

public class Program
{
    private static volatile bool _shutdownRequested = false;

    public static async Task Main(string[] args)
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string gameDir = ResolveGameDirectory(appDir);

        foreach (string runtimesPath in EnumerateNativeRuntimePaths(appDir, gameDir))
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathEntries = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (!pathEntries.Contains(runtimesPath, StringComparer.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", runtimesPath + Path.PathSeparator + currentPath);
                currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            }
        }

        AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
        {
            var assemblyName = new AssemblyName(resolveArgs.Name);
            foreach (string probeDir in EnumerateProbeDirectories(appDir, gameDir))
            {
                string path = Path.Combine(probeDir, assemblyName.Name + ".dll");
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }

            return null;
        };

        foreach (string probeDir in EnumerateProbeDirectories(appDir, gameDir))
        {
            foreach (string file in Directory.GetFiles(probeDir, "*.dll"))
            {
                try { Assembly.LoadFrom(file); }
                catch (BadImageFormatException) { }
                catch (Exception) { }
            }
        }

        await RunEngine(args, appDir, gameDir);
    }

    private static string ResolveGameDirectory(string appDir)
    {
        var normalized = appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(normalized);
        if (dirName.Equals("AudioClient", StringComparison.OrdinalIgnoreCase))
        {
            return Directory.GetParent(normalized)?.FullName ?? normalized;
        }

        return normalized;
    }

    private static IEnumerable<string> EnumerateProbeDirectories(string appDir, string gameDir)
    {
        yield return appDir;
        if (!string.Equals(appDir, gameDir, StringComparison.OrdinalIgnoreCase))
        {
            yield return gameDir;
        }
    }

    private static IEnumerable<string> EnumerateNativeRuntimePaths(string appDir, string gameDir)
    {
        foreach (string probeDir in EnumerateProbeDirectories(appDir, gameDir))
        {
            string runtimesPath = Path.Combine(probeDir, "runtimes", "win-x64", "native");
            if (Directory.Exists(runtimesPath))
            {
                yield return runtimesPath;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RunEngine(string[] args, string appDir, string gameDir)
    {
        var progress = new EngineInitProgress(
            msg => Console.WriteLine(msg),
            msg => Console.WriteLine(msg),
            () =>
            {
                Console.WriteLine("=================================");
                Console.WriteLine(" FROOXENGINE BOOTSTRAP COMPLETE! ");
                Console.WriteLine("=================================");
            });

        var host = await EngineHost.StartAsync(appDir, gameDir, args, progress, line => Console.WriteLine(line));

        PrintHelp();

        while (!_shutdownRequested)
        {
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();

            if (command is "exit" or "quit")
            {
                _shutdownRequested = true;
                break;
            }

            host.PostToEngine(() => ProcessCommand(host, command, parts));
        }

        host.Shutdown();
        Console.WriteLine("Shutdown complete.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProcessCommand(EngineHost host, string command, string[] args)
    {
        try
        {
            switch (command)
            {
                case "join":
                    if (args.Length < 2) { Console.WriteLine("Usage: join <session_url>"); break; }
                    host.Sessions.Join(args[1]);
                    Console.WriteLine($"Join requested: {args[1]}");
                    break;

                case "leave":
                    host.Sessions.Leave();
                    Console.WriteLine("Leave requested.");
                    break;

                case "currentsessions":
                {
                    var sessions = host.Sessions.GetCurrentSessions();
                    if (sessions.Count == 0) { Console.WriteLine("No connected sessions."); break; }
                    Console.WriteLine($"\n--- Connected Sessions ({sessions.Count}) ---");
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        string focusTag = s.IsFocused ? " [FOCUSED]" : "";
                        Console.WriteLine($"  {i + 1}. {s.Name}{focusTag} | State: {s.State} | Users: {s.UserCount} | Session: {s.Id}");
                    }
                    Console.WriteLine("---");
                    break;
                }

                case "activesessions":
                {
                    bool activeOnly = args.Any(a => a.Equals("--active", StringComparison.OrdinalIgnoreCase));
                    var sessions = host.Sessions.GetActiveSessions(activeOnly);
                    if (sessions.Count == 0) { Console.WriteLine("No sessions found."); break; }
                    Console.WriteLine($"\n--- Active Sessions ({sessions.Count}) ---");
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        Console.WriteLine($"  {i + 1}. {s.Name}");
                        Console.WriteLine($"     Host: {s.HostUsername} | Users: {s.JoinedUsers}/{s.MaximumUsers} | Access: {s.AccessLevel}");
                        Console.WriteLine($"     URL: {s.PreferredUrl}");
                    }
                    Console.WriteLine("---");
                    break;
                }

                case "focus":
                    if (args.Length < 2 || !int.TryParse(args[1], out int focusIndex))
                    { Console.WriteLine("Usage: focus <index>"); break; }
                    host.Sessions.Focus(focusIndex);
                    Console.WriteLine($"Focused on session {focusIndex}.");
                    break;

                case "name":
                    if (args.Length < 2) { Console.WriteLine("Usage: name <newName>"); break; }
                    host.Sessions.SetName(string.Join(" ", args.Skip(1)));
                    Console.WriteLine("Session name updated.");
                    break;

                case "accesslevel":
                    if (args.Length < 2) { Console.WriteLine("Usage: accessLevel <Private|LAN|Contacts|ContactsPlus|RegisteredUsers|Anyone>"); break; }
                    if (host.Sessions.SetAccessLevel(args[1]))
                        Console.WriteLine($"Access level set to {args[1]}.");
                    else
                        Console.WriteLine($"Invalid access level: {args[1]}");
                    break;

                case "startworldurl":
                    if (args.Length < 2) { Console.WriteLine("Usage: startWorldURL <recordURL>"); break; }
                    Task.Run(() => host.Sessions.StartWorldURLAsync(string.Join(" ", args.Skip(1))));
                    Console.WriteLine("World start requested.");
                    break;

                case "startworldtemplate":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Available templates:");
                        foreach (var t in host.Sessions.GetWorldTemplates()) Console.WriteLine($"  - {t}");
                        break;
                    }
                    host.Sessions.StartWorldTemplate(string.Join(" ", args.Skip(1)));
                    Console.WriteLine("World template start requested.");
                    break;

                case "users":
                {
                    var users = host.Users.GetCurrentUsers();
                    if (users.Count == 0) { Console.WriteLine("No users (or no focused session)."); break; }
                    Console.WriteLine($"\n--- Users ({users.Count}) ---");
                    for (int i = 0; i < users.Count; i++)
                    {
                        var u = users[i];
                        string localTag = u.IsLocal ? " (You)" : "";
                        string hostTag = u.IsHost ? " [HOST]" : "";
                        Console.WriteLine($"  {i + 1}. {u.UserName}{localTag}{hostTag} | {(u.IsPresentInWorld ? "Present" : "Away")} | Ping: {u.Ping}ms");
                    }
                    Console.WriteLine("---");
                    break;
                }

                case "movetouser":
                    if (args.Length < 2) { Console.WriteLine("Usage: moveToUser <userName>"); break; }
                    host.Users.MoveToUser(string.Join(" ", args.Skip(1)));
                    Console.WriteLine("Move requested.");
                    break;

                case "locomotion":
                    if (args.Length < 2)
                    {
                        foreach (var m in host.Users.GetLocomotionModules()) Console.WriteLine($"  {m}");
                        break;
                    }
                    host.Users.SetLocomotionModule(string.Join(" ", args.Skip(1)));
                    Console.WriteLine("Locomotion switch requested.");
                    break;

                case "voicemode":
                    if (args.Length < 2)
                    {
                        Console.WriteLine($"Current: {host.Users.GetVoiceMode() ?? "N/A"}");
                        Console.WriteLine("Available: Normal, Shout, Broadcast, Whisper, Mute");
                        break;
                    }
                    if (host.Users.SetVoiceMode(args[1]))
                        Console.WriteLine($"Voice mode set to {args[1]}.");
                    else
                        Console.WriteLine($"Unknown voice mode: {args[1]}");
                    break;

                case "login":
                    if (args.Length < 3) { Console.WriteLine("Usage: login <username> <password> [totp]"); break; }
                    Task.Run(async () =>
                    {
                        string? totp = args.Length >= 4 ? args[3] : null;
                        var r = await host.Auth.LoginAsync(args[1], args[2], totp);
                        if (r.RequiresTotp && string.IsNullOrWhiteSpace(totp))
                        {
                            Console.Write("TOTP code: ");
                            var enteredTotp = Console.ReadLine();
                            if (string.IsNullOrWhiteSpace(enteredTotp))
                            {
                                Console.WriteLine("Login failed: TOTP code entry was cancelled.");
                                return;
                            }

                            r = await host.Auth.LoginAsync(args[1], args[2], enteredTotp);
                        }
                        Console.WriteLine(r.IsOK ? $"Login OK: {r.Message}" : $"Login failed: {r.Message}");
                    });
                    break;

                case "logout":
                    Task.Run(async () =>
                    {
                        var r = await host.Auth.LogoutAsync();
                        Console.WriteLine(r.IsOK ? r.Message : $"Logout failed: {r.Message}");
                    });
                    break;

                case "contactlist":
                {
                    var contacts = host.Contacts.GetOnlineContacts();
                    if (contacts.Count == 0) { Console.WriteLine("No contacts online."); break; }
                    Console.WriteLine($"\n--- Online Contacts ({contacts.Count}) ---");
                    foreach (var c in contacts)
                    {
                        string sessionTag = c.CurrentSessionName != null ? $" | Session: {c.CurrentSessionName}" : "";
                        Console.WriteLine($"  {c.Username} [{c.OnlineStatus}]{sessionTag}");
                    }
                    Console.WriteLine("---");
                    break;
                }

                case "contactinfo":
                    if (args.Length < 2) { Console.WriteLine("Usage: contactInfo <username>"); break; }
                {
                    var (info, sessions) = host.Contacts.GetContactDetail(string.Join(" ", args.Skip(1)));
                    if (info == null) { Console.WriteLine("Contact not found."); break; }
                    Console.WriteLine($"\n--- Contact: {info.Username} ---");
                    Console.WriteLine($"  Status: {info.OnlineStatus}");
                    if (info.CurrentSessionName != null)
                        Console.WriteLine($"  Session: {info.CurrentSessionName} (Host: {info.CurrentSessionHost} | {info.CurrentSessionUsers}/{info.CurrentSessionMaxUsers})");
                    foreach (var s in sessions)
                        Console.WriteLine($"  Session: Access={s.AccessLevel}{(s.IsHost ? " [HOST]" : "")}{(s.IsHidden ? " [Hidden]" : "")}");
                    Console.WriteLine("---");
                    break;
                }

                case "contactinvite":
                    if (args.Length < 2) { Console.WriteLine("Usage: contactInvite <username>"); break; }
                    Task.Run(async () =>
                    {
                        bool ok = await host.Contacts.InviteAsync(string.Join(" ", args.Skip(1)));
                        Console.WriteLine(ok ? "Invite sent." : "Invite failed.");
                    });
                    break;

                case "contactjoin":
                    if (args.Length < 2) { Console.WriteLine("Usage: contactJoin <username>"); break; }
                    if (host.Contacts.JoinContactSession(string.Join(" ", args.Skip(1))))
                        Console.WriteLine("Join requested.");
                    else
                        Console.WriteLine("Could not join contact's session.");
                    break;

                case "audioinputdevice":
                {
                    var devices = host.Audio.GetInputDevices();
                    if (args.Length < 2)
                    {
                        Console.WriteLine("\n--- Audio Input Devices ---");
                        foreach (var d in devices)
                            Console.WriteLine($"  {d.Index}. {d.Name}{(d.IsActive ? " [ACTIVE]" : "")}{(!d.IsConnected ? " [DISCONNECTED]" : "")}");
                        Console.WriteLine("Usage: audioInputDevice <index>");
                        break;
                    }
                    if (int.TryParse(args[1], out int idx)) host.Audio.SetInputDevice(idx);
                    Console.WriteLine($"Input device {args[1]} selected.");
                    break;
                }

                case "audiooutputdevice":
                {
                    var devices = host.Audio.GetOutputDevices();
                    if (args.Length < 2)
                    {
                        Console.WriteLine("\n--- Audio Output Devices ---");
                        foreach (var d in devices)
                            Console.WriteLine($"  {d.Index}. {d.Name}{(d.IsActive ? " [ACTIVE]" : "")}{(!d.IsConnected ? " [DISCONNECTED]" : "")}");
                        Console.WriteLine("Usage: audioOutputDevice <index>");
                        break;
                    }
                    if (int.TryParse(args[1], out int idx)) host.Audio.SetOutputDevice(idx);
                    Console.WriteLine($"Output device {args[1]} selected.");
                    break;
                }

                case "mute":
                    host.Audio.ToggleMute();
                    Console.WriteLine(host.Audio.IsMuted ? "Microphone MUTED." : "Microphone UNMUTED.");
                    break;

                case "volumes":
                {
                    var v = host.Audio.GetVolumes();
                    if (v == null) { Console.WriteLine("Volume settings not available."); break; }
                    Console.WriteLine($"  masterVolume      : {v.Master:P0}");
                    Console.WriteLine($"  soundEffectVolume : {v.SoundEffect:P0}");
                    Console.WriteLine($"  multimediaVolume  : {v.Multimedia:P0}");
                    Console.WriteLine($"  voiceVolume       : {v.Voice:P0}");
                    Console.WriteLine($"  uiVolume          : {v.UI:P0}");
                    break;
                }

                case "mastervolume":
                    HandleVolumeCmd(args, v => host.Audio.SetMasterVolume(v), () => host.Audio.GetVolumes()?.Master);
                    break;
                case "soundeffectvolume":
                    HandleVolumeCmd(args, v => host.Audio.SetSoundEffectVolume(v), () => host.Audio.GetVolumes()?.SoundEffect);
                    break;
                case "multimediavolume":
                    HandleVolumeCmd(args, v => host.Audio.SetMultimediaVolume(v), () => host.Audio.GetVolumes()?.Multimedia);
                    break;
                case "voicevolume":
                    HandleVolumeCmd(args, v => host.Audio.SetVoiceVolume(v), () => host.Audio.GetVolumes()?.Voice);
                    break;
                case "uivolume":
                    HandleVolumeCmd(args, v => host.Audio.SetUIVolume(v), () => host.Audio.GetVolumes()?.UI);
                    break;

                case "import":
                    if (args.Length < 2) { Console.WriteLine("Usage: import <path>"); break; }
                    // InventoryService.Import needs the engine — pass via host.PostToEngine for thread safety
                    host.PostToEngine(() => host.Inventory.Import(string.Join(" ", args.Skip(1))));
                    Console.WriteLine("Import requested.");
                    break;

                case "inventorylist":
                    Task.Run(async () =>
                    {
                        string path = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "";
                        var listing = await host.Inventory.ListAsync(path);
                        if (listing == null) { Console.WriteLine("Not logged in or path not found."); return; }
                        Console.WriteLine($"\n[Inventory: /{path}]");
                        foreach (var d in listing.Directories) Console.WriteLine($"  [DIR] {d}");
                        foreach (var r in listing.Records) Console.WriteLine($"  [OBJ] {r}");
                        if (!listing.Directories.Any() && !listing.Records.Any()) Console.WriteLine("  (empty)");
                    });
                    break;

                case "inventoryspawn":
                    if (args.Length < 2) { Console.WriteLine("Usage: inventorySpawn <path>"); break; }
                    Task.Run(async () =>
                    {
                        bool ok = await host.Inventory.SpawnAsync(string.Join(" ", args.Skip(1)));
                        Console.WriteLine(ok ? "Spawn requested." : "Spawn failed (not logged in or item not found).");
                    });
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static void HandleVolumeCmd(string[] args, Action<float> setter, Func<float?> getter)
    {
        if (args.Length < 2)
        {
            var v = getter();
            if (v.HasValue) Console.WriteLine($"{v.Value:P0}");
            return;
        }
        if (float.TryParse(args[1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float value) && value >= 0f && value <= 1f)
        {
            setter(value);
            Console.WriteLine($"Set to {value:P0}");
        }
        else
        {
            Console.WriteLine("Invalid value. Please specify a number between 0 and 1.");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine("AudioClient is ready. Commands:");
        Console.WriteLine("  join <url>                       - Join a session");
        Console.WriteLine("  leave                            - Leave current session");
        Console.WriteLine("  currentSessions                  - List connected sessions");
        Console.WriteLine("  activeSessions [--active]        - List available sessions");
        Console.WriteLine("  focus <index>                    - Focus a connected session");
        Console.WriteLine("  name <name>                      - Rename current session");
        Console.WriteLine("  accessLevel <level>              - Set session access level");
        Console.WriteLine("  startWorldURL <url>              - Start session from record URL");
        Console.WriteLine("  startWorldTemplate [name]        - Start session from template");
        Console.WriteLine("  users                            - List users in current session");
        Console.WriteLine("  moveToUser <name>                - Move to 1m in front of user");
        Console.WriteLine("  locomotion [name]                - List or switch locomotion");
        Console.WriteLine("  voiceMode [mode]                 - Show or set voice mode");
        Console.WriteLine("  login <user> <password> [totp]   - Login to Resonite");
        Console.WriteLine("  logout                           - Logout from Resonite");
        Console.WriteLine("  contactList                      - List online contacts");
        Console.WriteLine("  contactInfo <username>           - Show contact details");
        Console.WriteLine("  contactInvite <username>         - Invite contact to session");
        Console.WriteLine("  contactJoin <username>           - Join contact's session");
        Console.WriteLine("  audioInputDevice [index]         - List or switch mic");
        Console.WriteLine("  audioOutputDevice [index]        - List or switch speaker");
        Console.WriteLine("  mute                             - Toggle mute");
        Console.WriteLine("  voiceVolume [0-1]                - Show or set voice volume");
        Console.WriteLine("  masterVolume [0-1]               - Show or set master volume");
        Console.WriteLine("  volumes                          - Show all volume levels");
        Console.WriteLine("  inventoryList [path]             - List inventory directory");
        Console.WriteLine("  inventorySpawn <path>            - Spawn inventory item");
        Console.WriteLine("  import <path>                    - Import asset file");
        Console.WriteLine("  exit / quit                      - Shutdown");
        Console.WriteLine("==================================================================");
    }
}
