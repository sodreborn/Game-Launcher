// Copyright (c) Burralis.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace Burralis.Game.Launcher;

static class LaunchOptions
{
    public static Option<GameVersion> Version = new("--version", () => GameVersion.Retail);
    public static Option<string> GamePath = new("--path");
    public static Option<string> GameBinary = new("--binary");
    public static Option<bool> KeepCache = new("--keepcache", () => true);
    public static Option<string> VersionUrl = new("--versionurl");
    public static Option<string> CdnsUrl = new("--cdnsurl");
    public static Option<string> ProductName = new("--product", () => "wow");
    public static Option<string> CdnRegion = new("--region", () => "EU");
    public static Option<string> BgsPortal = new("--portal");
    public static Option<bool> Experimental70003 = new("--experimental-70003",
        "Opt-in diagnostic profile for the verified Classic Era 1.15.9.70003 executable. Requires a compatible local server; world entry is still incomplete.");

    // Game command line options.
    public static Option<string> GameConfig = new("-config", () => "Config.wtf");

    public static Parser Instance => new CommandLineBuilder(ConfigureCommandLine(RootCommand))
        .UseHelp()
        .UseParseDirective()
        .CancelOnProcessTermination()
        .UseParseErrorReporting()
        .UseSuggestDirective()
        .Build();

    public static RootCommand RootCommand = new("Burralis Game Launcher")
    {
        Version,
        GamePath,
        GameBinary,
        KeepCache,
        VersionUrl,
        CdnsUrl,
        ProductName,
        CdnRegion,
        BgsPortal,
        Experimental70003,
        GameConfig,
    };

    static Command ConfigureCommandLine(Command rootCommand)
    {
        // Do not show errors for unknown command line parameters.
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        return rootCommand;
    }
}
