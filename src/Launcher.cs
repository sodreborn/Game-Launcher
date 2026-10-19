// Copyright (c) Burralis.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine.Parsing;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using static Burralis.Game.Launcher.Misc.Helpers;

namespace Burralis.Game.Launcher;

static class Launcher
{
    public static nint GameProcessHandle { get; private set; }
    public static readonly CancellationTokenSource CancellationTokenSource = new();

    static readonly List<GameVersion> _tryClients =
    [
        GameVersion.Retail,
        GameVersion.Classic, GameVersion.ClassicEra, GameVersion.ClassicAnniversary,
        GameVersion.ClassicTitan
    ];

    static bool _useVersionV2;
    static byte[] _binaryData;

    public static async ValueTask<string> PrepareGameLaunch(ParseResult commandLineResult, GameVersion? branchOverwrite)
    {
        Console.ResetColor();

        var gameVersion = commandLineResult.GetValueForOption(LaunchOptions.Version);

        if (branchOverwrite.HasValue)
            gameVersion = branchOverwrite.Value;

        var (subFolder, binaryName, majorGameVersion, minGameBuild) = gameVersion switch
        {
            GameVersion.Retail => ("_retail_", "Wow.exe", [10, 11, 12], 50401),
            GameVersion.Classic => ("_classic_", "WowClassic.exe", [2, 3, 4, 5], 50063),
            GameVersion.ClassicEra => ("_classic_era_", "WowClassic.exe", [1], 51001),
            GameVersion.ClassicAnniversary => ("_anniversary_", "WowClassic.exe", [2], 65340),
            GameVersion.ClassicTitan => ("_classic_titan_", "WowClassic.exe", new[] { 3 }, 64393),
            _ => throw new NotImplementedException("Invalid game version specified."),
        };

        _tryClients.RemoveAll(c => c == gameVersion);

        Console.ForegroundColor = ConsoleColor.Yellow;

        Console.WriteLine($"Mode: Public Custom Server ({gameVersion})");
        Console.ResetColor();

        var currentFolder = AppDomain.CurrentDomain.BaseDirectory;
        var gameFolder = $"{currentFolder}/{subFolder}";

        if (commandLineResult.HasOption(LaunchOptions.GameBinary))
            binaryName = commandLineResult.GetValueForOption(LaunchOptions.GameBinary);

        var gameBinaryPath = $"{gameFolder}/{binaryName}";

        if (commandLineResult.HasOption(LaunchOptions.GamePath))
        {
            gameFolder = commandLineResult.GetValueForOption(LaunchOptions.GamePath);
            gameBinaryPath = $"{gameFolder}/{binaryName}";
        }
        else if (!File.Exists(gameBinaryPath))
        {
            // Also support game installations without branch sub folders.
            gameFolder = currentFolder;
            gameBinaryPath = $"{gameFolder}/{binaryName}";
        }

        gameFolder = gameFolder.Replace("\\/", "/").Replace("\\", "/");
        gameBinaryPath = gameBinaryPath.Replace("\\/", "/").Replace("\\", "/");

        if (!File.Exists(gameBinaryPath) || !majorGameVersion.Contains(GetVersionValueFromClient(gameBinaryPath).Major))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"No {gameVersion} client found at '{gameBinaryPath}'");

            if (_tryClients.Count > 0)
            {
                var nextClient = _tryClients.First();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Trying a different branch...");
                Console.ResetColor();

                return await PrepareGameLaunch(commandLineResult, nextClient);
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"No supported client found.");
            }

            return string.Empty;
        }

        var gameClientBuild = GetVersionValueFromClient(gameBinaryPath).Build;

        if (gameClientBuild < minGameBuild && gameClientBuild != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Your found client version {gameClientBuild} is not supported.");
            Console.WriteLine($"The minimum required build is {minGameBuild}");

            return string.Empty;
        }

        // Delete the cache folder by default.
        if (!commandLineResult.GetValueForOption(LaunchOptions.KeepCache))
        {
            try
            {
                // Trying to delete the cache folder.
                Directory.Delete($"{gameFolder}/Cache", true);
            }
            catch (Exception)
            {
                // We don't care if it worked. Swallow it!
            }
        }

        var configPath = $"{gameFolder}/WTF/{commandLineResult.GetValueForOption(LaunchOptions.GameConfig)}";

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine();
        Console.WriteLine($"Client Config: \"{configPath}\"");
        Console.ResetColor();

        (string IPAddress, string HostName, int Port) portal = new();


        var config = File.ReadAllText(configPath);
        var bgsPortal = commandLineResult.GetValueForOption(LaunchOptions.BgsPortal);

        portal = ParseOrSetPortal(ref config, bgsPortal);

        if (!string.IsNullOrEmpty(bgsPortal))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Config Portal Overwrite:\"{bgsPortal}\"");
            Console.ResetColor();
            Console.WriteLine();

            File.WriteAllText(configPath, config);
        }

        _binaryData = File.ReadAllBytes(gameBinaryPath);
        _useVersionV2 = UsesVersionV2(_binaryData);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Client Path: '{gameBinaryPath}'");
        Console.WriteLine($"Client Portal: '{portal.HostName}:{portal.Port}'");
        Console.ForegroundColor = ConsoleColor.Gray;


        if (string.IsNullOrEmpty(portal.HostName) || string.IsNullOrWhiteSpace(portal.HostName))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Client Portal should not be empty.");
            Console.WriteLine("Be sure to have a valid portal in your Config.wtf file.");
            Console.ResetColor();

            return string.Empty;
        }

        // Return if no valid ip address has been found.
        if (string.IsNullOrEmpty(portal.IPAddress) || string.IsNullOrWhiteSpace(portal.IPAddress))
            return string.Empty;

        // Check for valid certificate.
        try
        {
            using var tcpClient = new TcpClient();

            // 3.5 seconds timeout.
            const int timeout = 3500;

            tcpClient.ReceiveTimeout = timeout;
            tcpClient.SendTimeout = timeout;

            using var tcpClientTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));

            await tcpClient.ConnectAsync(portal.HostName, portal.Port, tcpClientTimeout.Token);

            using var sslStream = new SslStream(tcpClient.GetStream(), false,
                (_, _, _, sslPolicyErrors) =>
                {
                    // Redirect to the trusted cert warning.
                    if (sslPolicyErrors != SslPolicyErrors.None)
                        throw new AuthenticationException();

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Certificate for server '{portal.HostName}' successfully validated.");
                    Console.WriteLine();
                    Console.ResetColor();

                    return true;
                },
                null
            );

            sslStream.AuthenticateAsClient(portal.HostName);
        }
        catch (IOException exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Server {portal.HostName}:{portal.Port}: {exception.Message}");
            Console.ResetColor();

            return string.Empty;
        }
        catch (AuthenticationException)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Server with host name {portal.HostName}:{portal.Port} does not have a trusted certificate attached.");
            Console.WriteLine("If you are the server owner be sure to generate one and replace the default bnet server certificate.");
            Console.WriteLine("One way to generate one is through Let's Encrypt.");
            Console.ResetColor();

            return string.Empty;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{portal.HostName}:{portal.Port} is offline or not reachable from the current network.");
            Console.ResetColor();

            return string.Empty;
        }

        return gameBinaryPath;
    }

    public static bool LaunchGame(string appPath, string gameCommandLine, ParseResult commandLineResult)
    {
        // Build the version URL from the game binary build.
        var clientVersion = GetVersionValueFromClient(appPath);
        bool experimental70003 = commandLineResult.GetValueForOption(LaunchOptions.Experimental70003);
        bool tutorialBridge70003 = commandLineResult.GetValueForOption(LaunchOptions.TutorialBridge70003);
        ClassicEra70003.ValidateTutorialBridgeOptions(experimental70003, tutorialBridge70003,
            commandLineResult.GetValueForOption(LaunchOptions.BgsPortal));
        if (experimental70003)
        {
            ClassicEra70003.ValidateImage(_binaryData, clientVersion, commandLineResult.GetValueForOption(LaunchOptions.Version));
            Console.WriteLine("Experimental 70003 diagnostic mode: executable SHA-256 verified.");
            Console.WriteLine("Requires a compatible 70003 server and trusted TLS. Login can require a retry; world entry remains incomplete.");
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Client Build {clientVersion}");
        Console.ResetColor();

        // Assign the region and product dependent version url to check it's online status.
        string[] versionUrls = CreateVersionUrls(commandLineResult, clientVersion);

        static string[] CreateVersionUrls(ParseResult commandLineResult, (int Major, int Minor, int Revision, int Build) clientVersion)
        {
            var versionUrl = commandLineResult.GetValueForOption(LaunchOptions.VersionUrl);

            // Always return a forced version url parameter.
            if (!string.IsNullOrEmpty(versionUrl))
                return [versionUrl, versionUrl, versionUrl];

            return [];
        }

        bool hasCustomVersionUrl = commandLineResult.HasOption(LaunchOptions.VersionUrl);
        bool hasCustomCdnUrl = commandLineResult.HasOption(LaunchOptions.CdnsUrl);

        if (hasCustomCdnUrl || hasCustomVersionUrl)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: Custom version servers were specified.");
            Console.WriteLine("The game will connect to these servers.");
            Console.ResetColor();
        }

        var cdnsUrl = commandLineResult.GetValueForOption(LaunchOptions.CdnsUrl);

        if (hasCustomCdnUrl && (!CheckUrl(cdnsUrl, fallbackUrls: [Patterns.Common.CdnsUrl]).GetAwaiter().GetResult()))
            cdnsUrl = Patterns.Common.CdnsUrl;

        Console.ForegroundColor = ConsoleColor.Cyan;

        Console.WriteLine();
        Console.WriteLine("Game CDN connection info:");
        Console.WriteLine($"Version source(s):");

        if (versionUrls.Length == 0)
        {
            foreach (var vUrl in new[] { Patterns.Common.VersionUrl, Patterns.Common.Version2Url, Patterns.Common.Version2ChinaUrl })
                Console.WriteLine($"- {vUrl}");
        }
        else
        {
            if (versionUrls.All(element => element.Equals(versionUrls[0])))
                Console.WriteLine($"- {versionUrls[0]}");
            else
            {
                foreach (var vUrl in versionUrls)
                    Console.WriteLine($"- {vUrl}");
            }
        }

        Console.WriteLine($"CDNs source: {cdnsUrl}");
        Console.WriteLine();
        Console.ResetColor();

        var startupInfo = new StartupInfo();
        var processInfo = new ProcessInformation();

        try
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Starting game client...");

            var createSuccess = NativeWindows.CreateProcess(null, $"\"{appPath}\" {gameCommandLine}", 0, 0, false, 4, 0, new FileInfo(appPath).DirectoryName,
                ref startupInfo, out processInfo);

            // On some systems we have to launch the game with the application name used.
            if (!createSuccess)
                createSuccess = NativeWindows.CreateProcess(appPath, $" {gameCommandLine}", 0, 0, false, 4, 0, new FileInfo(appPath).DirectoryName, ref startupInfo, out processInfo);

            // Start process with suspend flags.
            if (createSuccess)
            {
                GameProcessHandle = processInfo.ProcessHandle;

                using var gameAppData = File.OpenRead(appPath);

                var appDataLength = gameAppData.Length;
                var memory = new WinMemory(processInfo);

                if (memory.BaseAddress != 0)
                {
                    Process gameProcess = Process.GetProcessById((int)processInfo.ProcessId);

                    // Refresh the client data before patching.
                    memory.RefreshMemoryData((int)appDataLength);

                    if (experimental70003)
                        ClassicEra70003.ValidateMemory(memory.Data);

                    // Custom CDN related patches.
                    if ((hasCustomVersionUrl && hasCustomCdnUrl))
                    {
                        var versionUrlPatches = new byte[versionUrls.Length][];

                        for (var i = 0; i < versionUrls.Length; i++)
                            versionUrlPatches[i] = Encoding.UTF8.GetBytes(versionUrls[i] + '\0');

                        // 11.1.7 added new v2 links.
                        if (_useVersionV2)
                        {
                            Task.WaitAll(
                            [
                                memory.TryPatchPatterns(versionUrlPatches, "Version URL", true, false, true,
                                                        Patterns.Common.Version2UrlNew.ToPattern(), Patterns.Common.Version2ChinaUrlNew.ToPattern()),
                            ], CancellationTokenSource.Token);
                        }
                        else
                        {
                            Task.WaitAll(
                            [
                                memory.TryPatchPatterns(versionUrlPatches, "Version URL", true, false, true,
                                                        Patterns.Common.VersionUrl.ToPattern(), Patterns.Common.Version2Url.ToPattern(), Patterns.Common.Version2ChinaUrl.ToPattern()),
                                memory.PatchMemory(Patterns.Common.CdnsUrl.ToPattern(), Encoding.UTF8.GetBytes(cdnsUrl), "CDNs URL", exitOnFail: false),
                            ], CancellationTokenSource.Token);
                        }
                    }

                    // Wait for all direct memory patch tasks to complete.
                    var patchTasks = new List<Task<bool>>();
                    if (!experimental70003)
                        patchTasks.Add(memory.PatchMemory(Patterns.Common.ConnectToModulus, Patches.Common.RsaModulus, "ConnectTo RsaModulus"));
                    else
                        Console.WriteLine("[ConnectTo RsaModulus] Legacy RSA patch omitted; a compatible 70003 server must supply the modern endpoint-vector redirect.");

                    if (!experimental70003)
                        patchTasks.Add(memory.PatchMemory(Patterns.Common.CryptoEdPublicKey,
                            Patches.Common.CryptoEdPublicKey, "GameCrypto Ed25519 PublicKey"));
                    patchTasks.Add(memory.PatchMemory(Patterns.Common.Portal, Patches.Common.Portal, "Login Portal", printInfo: experimental70003 ? true : null));
                    patchTasks.Add(memory.PatchMemory(Patterns.Windows.LauncherLogin, Patches.Windows.LauncherLogin, "Launcher Login Registry", printInfo: experimental70003 ? true : null));
                    Task.WaitAll(patchTasks.ToArray(), CancellationTokenSource.Token);
                    if (patchTasks.Any(task => !task.Result))
                        throw new InvalidOperationException("A required client patch failed.");

                    if (experimental70003)
                    {
                        foreach (var (pattern, patch) in new[]
                        {
                            (Patterns.Common.Portal, Patches.Common.Portal),
                            (Patterns.Windows.LauncherLogin, Patches.Windows.LauncherLogin)
                        })
                        {
                            nint address = (nint)memory.Data.FindPattern(pattern, memory.BaseAddress);
                            if (memory.Read(address, patch.Length)?.SequenceEqual(patch) != true)
                                throw new InvalidOperationException("Diagnostic patch readback verification failed.");
                        }
                        Console.WriteLine("Portal and registry patches verified by reading client memory. Regional public key will be verified after the certificate store initializes.");
                        Console.WriteLine($"Diagnostic client process ID: {processInfo.ProcessId}");
                    }

                    NativeWindows.NtResumeProcess(processInfo.ProcessHandle);

                    if (experimental70003)
                    {
                        ClassicEra70003.MonitorRegionKey(processInfo.ProcessHandle, gameProcess, tutorialBridge70003);
                        Console.WriteLine("Client closed; region-key monitor stopped.");
                        return true;
                    }

                    Console.WriteLine("Done :) ");

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("You can login now.");
                    Console.WriteLine("Closing in 3 seconds...");
                    Thread.Sleep(3000);
                    Console.ResetColor();

                    return true;
                }
            }
        }
        // Only exit and do not print any exception messages to the console.
        catch (OperationCanceledException)
        {
            NativeWindows.TerminateProcess(processInfo.ProcessHandle, 0);
        }
        // Just print out the exception we have and kill the game process.
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            Console.WriteLine(ex.StackTrace);

            NativeWindows.TerminateProcess(processInfo.ProcessHandle, 0);
        }
        finally
        {
            NativeWindows.CloseHandle(processInfo.ProcessHandle);
            NativeWindows.CloseHandle(processInfo.ThreadHandle);
        }

        return false;
    }
}
