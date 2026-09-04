using AAEmu.World.Models;

namespace AAEmu.World.Core.ZoneHost;

public sealed record ZoneHostLaunchSpec(
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string LogDirectory,
    string LogName);

/// <summary>
/// Builds the same native command line Zone Manager uses for <c>AAEmu.ZoneHost.exe</c>,
/// plus dungeon <c>+instance</c> and a unique <c>+sv_port</c>.
/// </summary>
public static class ZoneHostCommandLine
{
    public const string DllEnvironment = "AAEMU_ZONE_DLL";
    public const string SaveDirectoryEnvironment = "AAEMU_ZONE_SAVE_DIR";
    public const string LogNameEnvironment = "AAEMU_ZONE_LOG_NAME";

    public static ZoneHostLaunchSpec Build(
        ZoneHostConfig config,
        string zoneName,
        uint instanceId,
        int svPort,
        string logDirectory,
        string logName)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(zoneName))
            throw new ArgumentException("Zone map name is required.", nameof(zoneName));
        if (string.IsNullOrWhiteSpace(config.Executable))
            throw new InvalidOperationException("ZoneHost.Executable is not configured.");
        if (string.IsNullOrWhiteSpace(config.WorkingDirectory))
            throw new InvalidOperationException("ZoneHost.WorkingDirectory is not configured.");
        if (string.IsNullOrWhiteSpace(config.NativeDll))
            throw new InvalidOperationException("ZoneHost.NativeDll is not configured.");

        var arguments = new List<string>();
        if (config.Dedicated)
            arguments.Add("-dedicated");

        AddCVar(arguments, "world_ip", config.WorldIp);
        AddCVar(arguments, "world_port", config.WorldPort.ToString());
        AddCVar(arguments, "world_serveraddr", config.WorldIp);
        AddCVar(arguments, "world_serverport", config.WorldPort.ToString());
        AddCVar(arguments, "zone", zoneName);
        AddCVar(arguments, "sv_map", zoneName);
        AddCVar(arguments, "instance", instanceId.ToString());
        AddCVar(arguments, "sv_port", svPort.ToString());
        AddCVar(arguments, "db_location", config.DbLocation);
        if (config.DisableRendering)
        {
            AddCVar(arguments, "e_render", "0");
            AddCVar(arguments, "r_Driver", "Null");
        }

        if (!string.IsNullOrWhiteSpace(config.ExtraArguments))
            arguments.AddRange(SplitArguments(config.ExtraArguments));

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DllEnvironment] = config.NativeDll,
            [SaveDirectoryEnvironment] = logDirectory,
            [LogNameEnvironment] = logName
        };

        return new ZoneHostLaunchSpec(
            Path.GetFullPath(config.Executable),
            Path.GetFullPath(config.WorkingDirectory),
            arguments,
            environment,
            logDirectory,
            logName);
    }

    private static void AddCVar(List<string> arguments, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        arguments.Add($"+{name}");
        arguments.Add(value.Trim());
    }

    private static List<string> SplitArguments(string value)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in value)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }
}
