using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AudioClient;

public class Program
{
    private static bool _shutdownRequested = false;
    private static object? _engine; // typed as object to avoid early FrooxEngine resolution
    
    public static async Task Main(string[] args)
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // Add native runtimes folder to PATH so raw DllImports (like SteamAudio's phonon) succeed
        string runtimesPath = Path.Combine(appDir, "runtimes", "win-x64", "native");
        if (Directory.Exists(runtimesPath))
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH", runtimesPath + Path.PathSeparator + currentPath);
        }

        // Register assembly resolver BEFORE any FrooxEngine types are touched.
        // This allows loading FrooxEngine.dll etc. regardless of version mismatch.
        AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
        {
            var assemblyName = new AssemblyName(resolveArgs.Name);
            string path = Path.Combine(appDir, assemblyName.Name + ".dll");
            if (File.Exists(path))
            {
                return Assembly.LoadFrom(path);
            }
            return null;
        };

        // Force-load all managed assemblies in the directory into the AppDomain 
        // This simulates Unity/Mono's behavior of having all assemblies available in the domain.
        // It's required so FrooxEngine's type scanner registers ALL valid Data Model Assemblies (like Awwdio, PhotonDust, etc).
        foreach (string file in Directory.GetFiles(appDir, "*.dll"))
        {
            try
            {
                Assembly.LoadFrom(file);
            }
            catch (BadImageFormatException)
            {
                // Ignore native DLLs like opus.dll, LibFreeImage.so, etc.
            }
            catch (Exception)
            {
                // Non-critical, skip silently
            }
        }

        // Now it's safe to call into FrooxEngine code
        await RunEngine(args, appDir);
    }

    // NoInlining ensures the JIT won't try to resolve FrooxEngine types
    // until this method is actually called (after our AssemblyResolve handler is registered)
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RunEngine(string[] args, string appDir)
    {
        
        InitializeLogging();

        Elements.Core.UniLog.Log("Starting Headless AudioClient...");

        var options = FrooxEngine.LaunchOptions.GetLaunchOptions(args);
        options.OutputDevice = Renderite.Shared.HeadOutputDevice.Screen;
        
        // Use Resonite default directories so it behaves perfectly but inside a console.
        options.DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Yellow Dog Man Studios", "Resonite");
        options.CacheDirectory = Path.Combine(Path.GetTempPath(), "Yellow Dog Man Studios", "Resonite");
        
        // Locale defaults to the Locale folder in the game directory
        options.LocaleDirectory = Path.Combine(appDir, "Locale");

        var engine = new FrooxEngine.Engine();
        _engine = engine;
        var systemInfo = new DummySystemInfo();
        var initProgress = new ConsoleInitProgress();

        // 1. Initialize Engine (Wait for completion)
        await engine.Initialize(appDir, useRenderer: false, options, systemInfo, initProgress);
        
        // 2. Setup Userspace
        FrooxEngine.Userspace.SetupUserspace(engine);
        
        // 3. Start Update Loop Thread
        Thread updateThread = new Thread(EngineUpdateLoop)
        {
            Name = "Engine Update Loop",
            IsBackground = false // Keep the process alive while the engine runs
        };
        updateThread.Start();

        // 4. Command Line Interface
        Console.WriteLine("==================================================================");
        Console.WriteLine("AudioClient is ready.");
        Console.WriteLine("Commands:");
        Console.WriteLine("  join <session_id/url>            - Join a session");
        Console.WriteLine("  contactInvite <username>         - Invite a contact to the current session");
        Console.WriteLine("  contactInfo <username>           - Show contact's online status and sessions");
        Console.WriteLine("  contactJoin <username>           - Join the session a contact is currently in");
        Console.WriteLine("  startWorldURL <recordURL>        - Start a new session from a record URL");
        Console.WriteLine("  startWorldTemplate <name>        - Start a new session from a built-in template");
        Console.WriteLine("  activeSessions                   - List available sessions to join");
        Console.WriteLine("  currentSessions                  - List your connected sessions");
        Console.WriteLine("  focus <index>                    - Focus a connected session by index");
        Console.WriteLine("  users                            - List users in current session");
        Console.WriteLine("  moveToUser <userName>            - Move to 1m in front of a user");
        Console.WriteLine("  locomotion [name]                - Switch to specified locomotion (e.g. Noclip)");
        Console.WriteLine("  name <name>                      - Rename the current session");
        Console.WriteLine("  accessLevel <level>              - Set access level of current session");
        Console.WriteLine("  leave                            - Leave current session");
        Console.WriteLine("  login <user> <password>          - Login to Resonite");
        Console.WriteLine("  logout                           - Logout from Resonite");
        Console.WriteLine("  mute                             - Toggle microphone mute");
        Console.WriteLine("  exit / quit                      - Shutdown the client");
        Console.WriteLine("==================================================================");

        while (!_shutdownRequested)
        {
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();

            engine.GlobalCoroutineManager.Post((state) =>
            {
                ProcessCommand(engine, command, parts);
            }, null);
        }
        
        // Shutdown logic
        FrooxEngine.Userspace.ExitApp(saveHomes: false);
        updateThread.Join();
        engine.Dispose();
        Console.WriteLine("Shutdown complete.");
    }

    private static void EngineUpdateLoop()
    {
        while (!_shutdownRequested)
        {
            (_engine as FrooxEngine.Engine)?.RunUpdateLoop();
            Thread.Sleep(10); // Sleep briefly to prevent 100% CPU usage
        }
    }

    private static void ProcessCommand(FrooxEngine.Engine engine, string command, string[] args)
    {
        try
        {
            switch (command)
            {
                case "contactinvite":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: contactInvite <username>");
                        break;
                    }
                    HandleContactInviteCommand(engine, string.Join(" ", args.Skip(1)));
                    break;

                case "contactinfo":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: contactInfo <username>");
                        break;
                    }
                    HandleContactInfoCommand(engine, string.Join(" ", args.Skip(1)));
                    break;

                case "contactjoin":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: contactJoin <username>");
                        break;
                    }
                    HandleContactJoinCommand(engine, string.Join(" ", args.Skip(1)));
                    break;

                case "startworldurl":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: startWorldURL <recordURL>");
                        break;
                    }
                    HandleStartWorldURLCommand(string.Join(" ", args.Skip(1)));
                    break;

                case "startworldtemplate":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: startWorldTemplate <templateName>");
                        Console.WriteLine("Available templates:");
                        foreach (var p in FrooxEngine.WorldPresets.Presets)
                            Console.WriteLine($"  - {p.Name}");
                        break;
                    }
                    HandleStartWorldTemplateCommand(string.Join(" ", args.Skip(1)));
                    break;

                case "name":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: name <newName>");
                        break;
                    }
                    HandleNameCommand(engine, string.Join(" ", args.Skip(1)));
                    break;

                case "accesslevel":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: accessLevel <Private|LAN|Contacts|ContactsPlus|RegisteredUsers|Anyone>");
                        break;
                    }
                    HandleAccessLevelCommand(engine, args[1]);
                    break;

                case "join":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: join <session_url>");
                        break;
                    }
                    string url = args[1];
                    Console.WriteLine($"Joining session: {url}...");

                    // Parse the URL to URI format if it's not
                    if (!url.Contains("://"))
                    {
                        url = $"resrec:///{url}";
                        Console.WriteLine($"Normalized URL: {url}");
                    }

                    if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                    {
                        // res-steam:// はSteam P2Pを使うため、ヘッドレス環境では動作しない。
                        // クラウドからセッション情報を取得し、lnl-nat:// URLを優先して使う。
                        if (uri.Scheme == "res-steam")
                        {
                            Console.WriteLine("res-steam:// URL detected. Resolving lnl-nat:// URL from cloud...");
                            string? sessionId = uri.Segments.LastOrDefault()?.TrimEnd('/');
                            if (sessionId != null)
                            {
                                var sessions = new List<SkyFrost.Base.SessionInfo>();
                                engine.Cloud.Sessions.GetSessions(sessions);
                                var matchedSession = sessions.FirstOrDefault(s =>
                                    s.SessionURLs != null &&
                                    s.SessionURLs.Any(u => u.Contains(sessionId)));

                                if (matchedSession?.SessionURLs != null)
                                {
                                    var allUris = matchedSession.SessionURLs
                                        .Select(u => Uri.TryCreate(u, UriKind.Absolute, out Uri? parsed) ? parsed : null)
                                        .Where(u => u != null)
                                        .Select(u => u!)
                                        .OrderBy(u => u.Scheme.StartsWith("lnl") ? 0 : 1) // lnl系を優先
                                        .ToList();

                                    if (allUris.Count > 0)
                                    {
                                        Console.WriteLine($"Found {allUris.Count} URL(s). Preferred: {allUris[0]}");
                                        FrooxEngine.Userspace.JoinSession(allUris);
                                        Console.WriteLine("Session Join requested.");
                                        break;
                                    }
                                }
                                Console.WriteLine("Could not resolve session from cloud. The session may be private or not listed. Trying res-steam:// directly (may fail)...");
                            }
                        }
                        FrooxEngine.Userspace.JoinSession(uri);
                        Console.WriteLine("Session Join requested.");
                    }
                    else
                    {
                        Console.WriteLine("Invalid URL format.");
                    }
                    break;
                
                case "users":
                    HandleUsersCommand(engine);
                    break;

                case "movetouser":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: moveToUser <userName>");
                        break;
                    }
                    // Support multi-word usernames by joining all args after the command
                    string targetUserName = string.Join(" ", args.Skip(1));
                    HandleMoveToUserCommand(engine, targetUserName);
                    break;

                case "activesessions":
                    bool activeOnly = args.Any(a => a.Equals("--active", StringComparison.OrdinalIgnoreCase));
                    HandleActiveSessionsCommand(engine, activeOnly);
                    break;

                case "currentsessions":
                    HandleCurrentSessionsCommand(engine);
                    break;

                case "focus":
                    if (args.Length < 2 || !int.TryParse(args[1], out int focusIndex))
                    {
                        Console.WriteLine("Usage: focus <index>");
                        break;
                    }
                    HandleFocusCommand(engine, focusIndex);
                    break;

                case "leave":
                    HandleLeaveCommand(engine);
                    break;

                case "login":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: login <username> <password>");
                        break;
                    }
                    HandleLoginCommand(engine, args[1], args[2]);
                    break;

                case "logout":
                    HandleLogoutCommand(engine);
                    break;

                case "mute":
                    HandleMuteCommand(engine);
                    break;

                case "locomotion":
                    HandleLocomotionCommand(engine, args);
                    break;

                case "exit":
                case "quit":
                    _shutdownRequested = true;
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing command: {ex.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleUsersCommand(FrooxEngine.Engine engine)
    {
        var world = engine.WorldManager.FocusedWorld;
        if (world == null)
        {
            Console.WriteLine("No focused world. Join a session first.");
            return;
        }

        Console.WriteLine($"\n--- Users in '{world.Name}' (Session: {world.SessionId}) ---");
        int index = 0;
        foreach (var user in world.AllUsers)
        {
            index++;
            string hostTag = user.IsHost ? " [HOST]" : "";
            string presenceTag = user.IsPresentInWorld ? "Present" : "Away";
            string localTag = user.IsLocalUser ? " (You)" : "";
            Console.WriteLine($"  {index}. {user.UserName}{localTag}{hostTag} | ID: {user.UserID ?? "N/A"} | {presenceTag} | Ping: {user.Ping}ms");
        }
        if (index == 0)
        {
            Console.WriteLine("  (No users found)");
        }
        Console.WriteLine($"--- Total: {index} user(s) ---\n");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleMoveToUserCommand(FrooxEngine.Engine engine, string targetUserName)
    {
        var world = engine.WorldManager.FocusedWorld;
        if (world == null)
        {
            Console.WriteLine("No focused world. Join a session first.");
            return;
        }

        var localUser = world.LocalUser;
        if (localUser?.Root == null)
        {
            Console.WriteLine("Local user or user root not available.");
            return;
        }

        // Find the target user by name (case-insensitive)
        FrooxEngine.User? targetUser = null;
        foreach (var user in world.AllUsers)
        {
            if (string.Equals(user.UserName, targetUserName, StringComparison.OrdinalIgnoreCase))
            {
                targetUser = user;
                break;
            }
        }

        if (targetUser == null)
        {
            Console.WriteLine($"User '{targetUserName}' not found in the current session.");
            Console.WriteLine("Available users:");
            foreach (var user in world.AllUsers)
            {
                if (!user.IsLocalUser)
                {
                    Console.WriteLine($"  - {user.UserName}");
                }
            }
            return;
        }

        if (targetUser.IsLocalUser)
        {
            Console.WriteLine("Cannot move to yourself.");
            return;
        }

        var targetRoot = targetUser.Root;
        if (targetRoot == null)
        {
            Console.WriteLine($"User '{targetUserName}' does not have a UserRoot (not fully spawned?).");
            return;
        }

        // JumpToPoint modifies the data model, so it must run on the world's synchronized thread
        var capturedTargetRoot = targetRoot;
        var capturedLocalRoot = localUser.Root;
        var capturedName = targetUser.UserName;
        world.RunSynchronously(() =>
        {
            var headPosition = capturedTargetRoot.HeadPosition;
            capturedLocalRoot.JumpToPoint(headPosition, 1.0f);
        });
        Console.WriteLine($"Moved to 1m in front of '{capturedName}'.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleLeaveCommand(FrooxEngine.Engine engine)
    {
        var world = engine.WorldManager.FocusedWorld;
        if (world == null)
        {
            Console.WriteLine("No focused world to leave.");
            return;
        }

        string worldName = world.Name;
        Console.WriteLine($"Leaving session '{worldName}'...");
        try
        {
            FrooxEngine.Userspace.ExitWorld(world);
            Console.WriteLine($"Left session '{worldName}'.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error leaving session: {ex.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleLoginCommand(FrooxEngine.Engine engine, string username, string password)
    {
        if (engine.Cloud.CurrentUser != null)
        {
            Console.WriteLine($"Already logged in as '{engine.Cloud.CurrentUsername}'. Use 'logout' first.");
            return;
        }

        Console.WriteLine($"Logging in as '{username}'...");
        Task.Run(async () =>
        {
            try
            {
                var auth = new SkyFrost.Base.PasswordLogin(password);
                var result = await engine.Cloud.Session.Login(
                    username,
                    auth,
                    engine.Cloud.SecretMachineId,
                    rememberMe: true,
                    totp: null
                );

                if (result.IsOK)
                {
                    Console.WriteLine($"Login successful! Logged in as: {engine.Cloud.CurrentUsername} (ID: {engine.Cloud.CurrentUserID})");
                }
                else
                {
                    Console.WriteLine($"Login failed: {result.State} - {result.Content}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login error: {ex.Message}");
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleLogoutCommand(FrooxEngine.Engine engine)
    {
        if (engine.Cloud.CurrentUser == null)
        {
            Console.WriteLine("Not currently logged in.");
            return;
        }

        string currentUsername = engine.Cloud.CurrentUsername ?? "Unknown";
        Console.WriteLine($"Logging out '{currentUsername}'...");
        Task.Run(async () =>
        {
            try
            {
                await engine.Cloud.Session.Logout(isManual: true);
                Console.WriteLine("Logged out successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logout error: {ex.Message}");
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleMuteCommand(FrooxEngine.Engine engine)
    {
        bool newState = !engine.AudioSystem.IsMuted;
        engine.AudioSystem.IsMuted = newState;
        Console.WriteLine(newState ? "Microphone MUTED." : "Microphone UNMUTED.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleLocomotionCommand(FrooxEngine.Engine engine, string[] args)
    {
        var world = engine.WorldManager.FocusedWorld;
        if (world == null)
        {
            Console.WriteLine("No focused world.");
            return;
        }

        var localUser = world.LocalUser;
        if (localUser == null || localUser.Root == null)
        {
            Console.WriteLine("No local user available.");
            return;
        }

        var locoController = localUser.Root.Slot.GetComponentInChildren<FrooxEngine.LocomotionController>();
        if (locoController == null)
        {
            Console.WriteLine("No locomotion controller found on local user.");
            return;
        }

        if (args.Length < 2)
        {
            Console.WriteLine("Available locomotion modules:");
            foreach (var module in locoController.LocomotionModules)
            {
                if (module != null)
                {
                    string isActive = locoController.ActiveModule == module ? " (ACTIVE)" : "";
                    string nameText = "";
                    
                    try {
                        nameText = module.LocomotionName.ToString();
                    } catch {
                        nameText = module.GetType().Name;
                    }
                    
                    Console.WriteLine($"  - {nameText}{isActive}");
                }
            }
            Console.WriteLine("Usage: locomotion <LocomotionName>");
            return;
        }

        string targetName = string.Join(" ", args.Skip(1)).ToLowerInvariant();
        FrooxEngine.ILocomotionModule? targetModule = null;

        foreach (var module in locoController.LocomotionModules)
        {
            if (module == null) continue;
            
            string moduleName = "";
            try {
                moduleName = module.LocomotionName.ToString();
            } catch {
                moduleName = module.GetType().Name;
            }
            
            if (moduleName.ToLowerInvariant().Contains(targetName) || 
                module.GetType().Name.ToLowerInvariant().Contains(targetName))
            {
                targetModule = module;
                break;
            }
        }

        if (targetModule != null)
        {
            world.RunSynchronously(() =>
            {
                locoController.ActiveModule = targetModule;
            });
            try {
                Console.WriteLine($"Locomotion switched to: {targetModule.LocomotionName.ToString()}");
            } catch {
                Console.WriteLine($"Locomotion switched to: {targetModule.GetType().Name}");
            }
        }
        else
        {
            Console.WriteLine($"Locomotion module matching '{targetName}' not found.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleActiveSessionsCommand(FrooxEngine.Engine engine, bool activeOnly)
    {
        var sessions = new List<SkyFrost.Base.SessionInfo>();
        engine.Cloud.Sessions.GetSessions(sessions);

        if (activeOnly)
        {
            sessions = sessions.Where(s => s.JoinedUsers >= 1).ToList();
        }

        if (sessions.Count == 0)
        {
            Console.WriteLine(activeOnly ? "No sessions with active users found." : "No active sessions found.");
            return;
        }

        string label = activeOnly ? $"Active Sessions with users ({sessions.Count})" : $"Active Sessions ({sessions.Count})";
        Console.WriteLine($"\n--- {label} ---");
        int index = 0;
        foreach (var session in sessions)
        {
            index++;
            // lnl-nat:// を優先して表示（res-steam:// はヘッドレス環境で使用不可）
            string sessionUrl = session.SessionURLs?.FirstOrDefault(u => u.StartsWith("lnl-nat://"))
                ?? session.SessionURLs?.FirstOrDefault(u => u.StartsWith("lnl://"))
                ?? session.SessionURLs?.FirstOrDefault()
                ?? "N/A";
            Console.WriteLine($"  {index}. {session.Name ?? "(No Name)"}");
            Console.WriteLine($"     Host: {session.HostUsername ?? "N/A"} | Users: {session.JoinedUsers}/{session.MaximumUsers} | Access: {session.AccessLevel}");
            Console.WriteLine($"     URL: {sessionUrl}");
        }
        Console.WriteLine($"--- End of sessions ---\n");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleCurrentSessionsCommand(FrooxEngine.Engine engine)
    {
        var worldList = new List<FrooxEngine.World>();
        engine.WorldManager.GetWorlds(worldList);

        // Filter out userspace worlds
        var sessionWorlds = worldList.Where(w => w != FrooxEngine.Userspace.UserspaceWorld).ToList();

        if (sessionWorlds.Count == 0)
        {
            Console.WriteLine("No connected sessions.");
            return;
        }

        var focusedWorld = engine.WorldManager.FocusedWorld;
        Console.WriteLine($"\n--- Connected Sessions ({sessionWorlds.Count}) ---");
        for (int i = 0; i < sessionWorlds.Count; i++)
        {
            var world = sessionWorlds[i];
            string focusTag = (world == focusedWorld) ? " [FOCUSED]" : "";
            string stateTag = world.State.ToString();
            int userCount = world.AllUsers.Count();
            Console.WriteLine($"  {i + 1}. {world.Name ?? "(No Name)"}{focusTag} | State: {stateTag} | Users: {userCount} | Session: {world.SessionId}");
        }
        Console.WriteLine($"--- End of sessions ---\n");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleFocusCommand(FrooxEngine.Engine engine, int index)
    {
        var worldList = new List<FrooxEngine.World>();
        engine.WorldManager.GetWorlds(worldList);

        var sessionWorlds = worldList.Where(w => w != FrooxEngine.Userspace.UserspaceWorld).ToList();

        if (sessionWorlds.Count == 0)
        {
            Console.WriteLine("No connected sessions to focus.");
            return;
        }

        if (index < 1 || index > sessionWorlds.Count)
        {
            Console.WriteLine($"Invalid index. Please specify a number between 1 and {sessionWorlds.Count}.");
            return;
        }

        var targetWorld = sessionWorlds[index - 1];
        if (targetWorld.State != FrooxEngine.World.WorldState.Running)
        {
            Console.WriteLine($"Cannot focus session '{targetWorld.Name}' (State: {targetWorld.State}).");
            return;
        }

        engine.WorldManager.FocusWorld(targetWorld);
        Console.WriteLine($"Focused on session '{targetWorld.Name}'.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleContactInviteCommand(FrooxEngine.Engine engine, string username)
    {
        var world = engine.WorldManager.FocusedWorld;
        if (world == null || world == FrooxEngine.Userspace.UserspaceWorld)
        {
            Console.WriteLine("No active session focused. Start or join a session first.");
            return;
        }

        SkyFrost.Base.ContactData? targetContact = null;
        engine.Cloud.Contacts.ForeachContactData(cd =>
        {
            if (targetContact == null &&
                cd.Contact.ContactUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                targetContact = cd;
            }
        });

        if (targetContact == null)
        {
            Console.WriteLine($"Contact '{username}' not found. Make sure you have them as an accepted contact.");
            return;
        }

        string userId = targetContact.Contact.ContactUserId;
        string contactUsername = targetContact.Contact.ContactUsername;

        // AllowUserToJoin はデータモデル変更のため RunSynchronously でラップ
        world.RunSynchronously(() => world.AllowUserToJoin(userId));

        var sessionInfo = world.GenerateSessionInfo();

        Task.Run(async () =>
        {
            try
            {
                var userMessages = engine.Cloud.Messages.GetUserMessages(userId);
                bool sent = await userMessages.SendInviteMessage(sessionInfo);
                Console.WriteLine(sent
                    ? $"Invited '{contactUsername}' to '{world.Name}'."
                    : $"Failed to send invite to '{contactUsername}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending invite: {ex.Message}");
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleContactInfoCommand(FrooxEngine.Engine engine, string username)
    {
        SkyFrost.Base.ContactData? targetContact = null;
        engine.Cloud.Contacts.ForeachContactData(cd =>
        {
            if (targetContact == null &&
                cd.Contact.ContactUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                targetContact = cd;
            }
        });

        if (targetContact == null)
        {
            Console.WriteLine($"Contact '{username}' not found. Make sure you have them as an accepted contact.");
            return;
        }

        var contact = targetContact.Contact;
        var status = targetContact.CurrentStatus;
        var onlineStatus = status?.OnlineStatus ?? SkyFrost.Base.OnlineStatus.Offline;

        Console.WriteLine($"\n--- Contact: {contact.ContactUsername} (ID: {contact.ContactUserId}) ---");
        Console.WriteLine($"  Online Status: {onlineStatus}");

        if (status?.Sessions != null && status.Sessions.Count > 0)
        {
            Console.WriteLine($"  Sessions ({status.Sessions.Count}):");
            foreach (var session in status.Sessions)
            {
                string hostTag = session.IsHost ? " [HOST]" : "";
                string hiddenTag = session.SessionHidden ? " [Hidden]" : "";
                Console.WriteLine($"    - Access: {session.AccessLevel}{hostTag}{hiddenTag}");
            }
        }
        else
        {
            Console.WriteLine("  Sessions: None");
        }

        var currentSessionInfo = targetContact.CurrentSessionInfo;
        if (currentSessionInfo != null)
        {
            Console.WriteLine($"  Current Session: {currentSessionInfo.Name ?? "(No Name)"}");
            Console.WriteLine($"    Host: {currentSessionInfo.HostUsername ?? "N/A"} | Users: {currentSessionInfo.JoinedUsers}/{currentSessionInfo.MaximumUsers} | Access: {currentSessionInfo.AccessLevel}");
            Console.WriteLine($"    Use 'contactJoin {contact.ContactUsername}' to join.");
        }
        Console.WriteLine($"---\n");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleContactJoinCommand(FrooxEngine.Engine engine, string username)
    {
        SkyFrost.Base.ContactData? targetContact = null;
        engine.Cloud.Contacts.ForeachContactData(cd =>
        {
            if (targetContact == null &&
                cd.Contact.ContactUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                targetContact = cd;
            }
        });

        if (targetContact == null)
        {
            Console.WriteLine($"Contact '{username}' not found. Make sure you have them as an accepted contact.");
            return;
        }

        var sessionInfo = targetContact.CurrentSessionInfo;
        if (sessionInfo == null)
        {
            Console.WriteLine($"'{username}' is not currently in a visible session, or their session is private.");
            return;
        }

        var urls = sessionInfo.GetSessionURLs();
        if (urls == null || urls.Count == 0)
        {
            Console.WriteLine($"No valid session URLs found for '{username}'s session.");
            return;
        }

        Console.WriteLine($"Joining '{username}'s session: {sessionInfo.Name ?? "(No Name)"}...");
        FrooxEngine.Userspace.JoinSession(urls);
        Console.WriteLine("Session Join requested.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleStartWorldURLCommand(string recordUrl)
    {
        if (!Uri.TryCreate(recordUrl, UriKind.Absolute, out Uri? uri))
        {
            Console.WriteLine($"Invalid URL: {recordUrl}");
            return;
        }

        Console.WriteLine($"Starting world from URL: {uri}...");
        Task.Run(async () =>
        {
            try
            {
                var settings = new FrooxEngine.WorldStartSettings(uri);
                var world = await FrooxEngine.Userspace.OpenWorld(settings);
                Console.WriteLine(world != null ? $"World started: {world.Name}" : "World start failed (null returned).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting world: {ex.Message}");
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleStartWorldTemplateCommand(string templateName)
    {
        var preset = FrooxEngine.WorldPresets.Presets
            .FirstOrDefault(p => p.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));

        if (preset == null)
        {
            Console.WriteLine($"Template '{templateName}' not found.");
            Console.WriteLine("Available templates:");
            foreach (var p in FrooxEngine.WorldPresets.Presets)
                Console.WriteLine($"  - {p.Name}");
            return;
        }

        Console.WriteLine($"Starting world from template '{preset.Name}'...");
        var world = FrooxEngine.Userspace.StartSession(preset.Method);
        Console.WriteLine(world != null ? $"World started: {world.Name}" : "World start failed (null returned).");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleNameCommand(FrooxEngine.Engine engine, string newName)
    {
        var world = engine.WorldManager.FocusedWorld;
        if (world == null || world == FrooxEngine.Userspace.UserspaceWorld)
        {
            Console.WriteLine("No active session focused.");
            return;
        }

        string oldName = world.Name;
        world.Name = newName;
        Console.WriteLine($"Session name changed: '{oldName}' -> '{newName}'");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleAccessLevelCommand(FrooxEngine.Engine engine, string levelName)
    {
        var world = engine.WorldManager.FocusedWorld;
        if (world == null || world == FrooxEngine.Userspace.UserspaceWorld)
        {
            Console.WriteLine("No active session focused.");
            return;
        }

        if (!Enum.TryParse<SkyFrost.Base.SessionAccessLevel>(levelName, ignoreCase: true, out var level))
        {
            Console.WriteLine($"Invalid access level '{levelName}'.");
            Console.WriteLine("Valid values: Private, LAN, Contacts, ContactsPlus, RegisteredUsers, Anyone");
            return;
        }

        var oldLevel = world.AccessLevel;
        world.AccessLevel = level;
        Console.WriteLine($"Access level changed: {oldLevel} -> {level}");
    }

    private static void InitializeLogging()
    {
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        Directory.CreateDirectory(logDir);
        string logname = Elements.Core.UniLog.GenerateLogName(FrooxEngine.Engine.VersionNumber + "AudioClient");
        StreamWriter logStream = File.CreateText(Path.Combine(logDir, logname));
        
        object logLock = new object();

        void WriteLog(string prefix, string msg)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{prefix}] {msg}";
            
            // Console output depending on prefix
            if (prefix == "ERR")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(line);
                Console.ResetColor();
            }
            else if (prefix == "WARN")
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(line);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(line);
            }

            // File output
            lock (logLock)
            {
                logStream.WriteLine(line);
                logStream.Flush();
            }
        }

        Elements.Core.UniLog.OnLog += (msg) => WriteLog("INFO", msg);
        Elements.Core.UniLog.OnWarning += (msg) => WriteLog("WARN", msg);
        Elements.Core.UniLog.OnError += (msg) => WriteLog("ERR", msg);
        Elements.Core.UniLog.OnFlush += () =>
        {
            lock (logLock) logStream.Flush();
        };
    }
}

