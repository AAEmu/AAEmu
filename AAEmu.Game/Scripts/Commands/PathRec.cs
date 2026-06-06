using System.IO;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command to record a waypoint-based .path file in Data/Path/. Two modes:
///
///   AUTO (default): /pathrec start &lt;filename&gt; — server samples your position every
///   <see cref="TickIntervalMs"/> ms and adds a point whenever you have moved more than
///   <see cref="MinWaypointDistanceMeters"/> from the previous one. You just walk the route
///   and /pathrec save when done.
///
///   MANUAL: /pathrec wp drops a waypoint at the current position. Use this for precise turns
///   even while auto-recording is on.
///
/// Output uses the pipe format the Simulation loader expects (|X|Y|Z|). Built originally for
/// the Halcyona Golem routes (nuia_golem_move / harihara_golem_move).
/// </summary>
public class PathRec : ICommand
{
    public string[] CommandNames { get; set; } = ["pathrec", "pathrecord"];

    /// <summary>Active recording sessions keyed by character ObjId. In-memory only — server restart wipes them.</summary>
    private static readonly Dictionary<uint, Session> _sessions = new();
    private static readonly object _lock = new();
    private static bool _tickSubscribed;

    /// <summary>How often the auto-recorder samples the player's position.</summary>
    private const int TickIntervalMs = 500;

    /// <summary>
    /// Minimum distance from the previous waypoint before a new auto-waypoint is added. Keeps
    /// auto-recording from producing a thousand points if the player stops moving.
    /// </summary>
    private const float MinWaypointDistanceMeters = 4.0f;

    /// <summary>Hard cap so a forgotten session can't run forever.</summary>
    private const int MaxWaypointsPerSession = 500;

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<action> [args]";
    }

    public string GetCommandHelpText()
    {
        return "Record a .path file by walking the route.\n" +
               "  start <filename>            Begin AUTO recording (samples your position every 0.5s, adds points every 4m).\n" +
               "  wp                          Add a manual waypoint at your current position (overrides distance gate).\n" +
               "  undo                        Remove the last waypoint.\n" +
               "  list                        Show all current waypoints.\n" +
               "  save                        Write Data/Path/<filename>.path and end the session.\n" +
               "  cancel                      Discard the session without saving.\n" +
               "  reverse <src> <dst>         Mirror an existing .path file end-to-end (Harani Golem = reversed Nuia route).\n" +
               "  reload [filename]           Invalidate AiPathsManager cache (single file or ALL) so the next path lookup re-reads disk.\n" +
               "Halcyona Golem usage:\n" +
               "  /pathrec start nuia_golem_move    (you are at the Nuia camp / Golem spawn point)\n" +
               "  (run to Harani camp; manual /pathrec wp at sharp corners if you want extra precision)\n" +
               "  /pathrec save\n" +
               "  /pathrec reverse nuia_golem_move harihara_golem_move";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "start":
                {
                    if (args.Length < 2)
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Usage: pathrec start <filename>");
                        return;
                    }
                    var name = SanitizeFilename(args[1]);
                    if (string.IsNullOrEmpty(name))
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Invalid filename — use letters/digits/_/-/.");
                        return;
                    }
                    var startPos = character.Transform.World.Position;
                    lock (_lock)
                    {
                        _sessions[character.ObjId] = new Session
                        {
                            Name = name,
                            CharacterObjId = character.ObjId,
                            Points = [startPos], // seed with start point so the player doesn't have to /wp first
                            LastSampleAt = DateTime.UtcNow,
                        };
                        EnsureTickSubscribed();
                    }
                    CommandManager.SendNormalText(this, messageOutput,
                        $"Recording '{name}'. Auto-sampling every {TickIntervalMs} ms, new waypoint every >= {MinWaypointDistanceMeters} m. " +
                        $"Walk the route, /pathrec save when done. Manual /pathrec wp also works.");
                    CommandManager.SendNormalText(this, messageOutput,
                        $"  WP #1 (start): ({startPos.X:F2}, {startPos.Y:F2}, {startPos.Z:F2})");
                    return;
                }

            case "wp":
            case "waypoint":
                {
                    Session sess;
                    lock (_lock)
                    {
                        if (!_sessions.TryGetValue(character.ObjId, out sess))
                        {
                            CommandManager.SendErrorText(this, messageOutput, "No active session — /pathrec start <filename> first.");
                            return;
                        }
                    }
                    var pos = character.Transform.World.Position;
                    AppendWaypoint(sess, pos, manual: true);
                    CommandManager.SendNormalText(this, messageOutput,
                        $"Manual WP #{sess.Points.Count}: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
                    return;
                }

            case "undo":
                {
                    lock (_lock)
                    {
                        if (!_sessions.TryGetValue(character.ObjId, out var sess) || sess.Points.Count == 0)
                        {
                            CommandManager.SendErrorText(this, messageOutput, "Nothing to undo.");
                            return;
                        }
                        sess.Points.RemoveAt(sess.Points.Count - 1);
                        CommandManager.SendNormalText(this, messageOutput, $"Removed last waypoint. {sess.Points.Count} remaining.");
                    }
                    return;
                }

            case "list":
                {
                    Session sess;
                    lock (_lock)
                    {
                        if (!_sessions.TryGetValue(character.ObjId, out sess))
                        {
                            CommandManager.SendErrorText(this, messageOutput, "No active session.");
                            return;
                        }
                    }
                    CommandManager.SendNormalText(this, messageOutput,
                        $"Session '{sess.Name}': {sess.Points.Count} waypoints");
                    for (var i = 0; i < sess.Points.Count; i++)
                    {
                        var p = sess.Points[i];
                        CommandManager.SendNormalText(this, messageOutput,
                            $"  #{i + 1}: ({p.X:F2}, {p.Y:F2}, {p.Z:F2})");
                    }
                    return;
                }

            case "save":
                {
                    Session sess;
                    lock (_lock)
                    {
                        if (!_sessions.TryGetValue(character.ObjId, out sess))
                        {
                            CommandManager.SendErrorText(this, messageOutput, "No active session.");
                            return;
                        }
                        if (sess.Points.Count < 2)
                        {
                            CommandManager.SendErrorText(this, messageOutput, $"Need at least 2 waypoints, have {sess.Points.Count}.");
                            return;
                        }
                        _sessions.Remove(character.ObjId);
                        MaybeUnsubscribeTick();
                    }
                    try
                    {
                        var path = Path.Combine("Data", "Path", $"{sess.Name}.path");
                        using (var writer = new StreamWriter(path, false))
                        {
                            foreach (var p in sess.Points)
                                writer.WriteLine($"|{p.X:F2}|{p.Y:F2}|{p.Z:F2}|");
                        }
                        // Invalidate AiPathsManager cache for this file — without this, the next
                        // GoToPath that resolves through AiPathsManager keeps using the prior boot's
                        // cached entries and the new waypoints don't take effect until restart.
                        AAEmu.Game.Core.Managers.AiPathsManager.Instance.ClearCacheForFile(sess.Name);
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Saved {sess.Points.Count} waypoints to {path} (cache invalidated, hot-reloads on next path lookup)");
                    }
                    catch (Exception ex)
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"Save failed: {ex.Message}");
                    }
                    return;
                }

            case "cancel":
                {
                    lock (_lock)
                    {
                        if (_sessions.Remove(character.ObjId))
                        {
                            MaybeUnsubscribeTick();
                            CommandManager.SendNormalText(this, messageOutput, "Recording cancelled.");
                        }
                        else
                        {
                            CommandManager.SendErrorText(this, messageOutput, "No active session.");
                        }
                    }
                    return;
                }

            case "reverse":
                {
                    if (args.Length < 3)
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Usage: pathrec reverse <source> <dest>");
                        return;
                    }
                    var srcName = SanitizeFilename(args[1]);
                    var dstName = SanitizeFilename(args[2]);
                    if (string.IsNullOrEmpty(srcName) || string.IsNullOrEmpty(dstName))
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Invalid source or destination filename.");
                        return;
                    }
                    var srcPath = Path.Combine("Data", "Path", $"{srcName}.path");
                    var dstPath = Path.Combine("Data", "Path", $"{dstName}.path");
                    if (!File.Exists(srcPath))
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"Source file not found: {srcPath}");
                        return;
                    }
                    try
                    {
                        // Read the lines verbatim — Simulation's loader parses pipe-separated
                        // |X|Y|Z|. Reversing the line order is enough; no need to re-parse.
                        var lines = File.ReadAllLines(srcPath)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Reverse()
                            .ToArray();
                        if (lines.Length < 2)
                        {
                            CommandManager.SendErrorText(this, messageOutput, $"Source has only {lines.Length} valid lines — nothing to reverse.");
                            return;
                        }
                        File.WriteAllLines(dstPath, lines);
                        AAEmu.Game.Core.Managers.AiPathsManager.Instance.ClearCacheForFile(dstName);
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Mirrored {lines.Length} waypoints {srcName} → {dstName}. File: {dstPath} (cache invalidated)");
                    }
                    catch (Exception ex)
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"Reverse failed: {ex.Message}");
                    }
                    return;
                }

            case "reload":
                {
                    // Force cache invalidation for a single file OR all files. Useful when you
                    // edit a .path manually on disk and want it picked up without a restart.
                    if (args.Length >= 2)
                    {
                        var n = SanitizeFilename(args[1]);
                        if (string.IsNullOrEmpty(n))
                        {
                            CommandManager.SendErrorText(this, messageOutput, "Invalid filename.");
                            return;
                        }
                        AAEmu.Game.Core.Managers.AiPathsManager.Instance.ClearCacheForFile(n);
                        CommandManager.SendNormalText(this, messageOutput, $"Cleared cache for '{n}.path'. Next path lookup reads from disk.");
                    }
                    else
                    {
                        AAEmu.Game.Core.Managers.AiPathsManager.Instance.ClearCache();
                        CommandManager.SendNormalText(this, messageOutput, "Cleared ALL cached paths. Next path lookups read from disk.");
                    }
                    return;
                }

            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown action: {args[0]}");
                return;
        }
    }

    /// <summary>
    /// Periodic poll for all active sessions. Adds a new waypoint when the player has moved
    /// more than <see cref="MinWaypointDistanceMeters"/> from the previous one. Cheap — runs
    /// only while at least one session is active.
    /// </summary>
    private static void Tick(TimeSpan delta)
    {
        List<Session> snapshot;
        lock (_lock)
        {
            if (_sessions.Count == 0)
                return;
            snapshot = _sessions.Values.ToList();
        }

        foreach (var sess in snapshot)
        {
            // Look up the character — must still be online + in a world.
            var character = AAEmu.Game.Core.Managers.World.WorldManager.Instance.GetCharacterByObjId(sess.CharacterObjId);
            if (character == null)
                continue;
            var pos = character.Transform.World.Position;
            var last = sess.Points[^1];
            var dx = pos.X - last.X;
            var dy = pos.Y - last.Y;
            var dz = pos.Z - last.Z;
            var distSq = dx * dx + dy * dy + dz * dz;
            if (distSq < MinWaypointDistanceMeters * MinWaypointDistanceMeters)
                continue;
            AppendWaypoint(sess, pos, manual: false);
        }
    }

    private static void AppendWaypoint(Session sess, Vector3 pos, bool manual)
    {
        lock (_lock)
        {
            if (sess.Points.Count >= MaxWaypointsPerSession)
                return;
            sess.Points.Add(pos);
        }
    }

    private static void EnsureTickSubscribed()
    {
        if (_tickSubscribed)
            return;
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(TickIntervalMs), true);
        _tickSubscribed = true;
    }

    private static void MaybeUnsubscribeTick()
    {
        if (!_tickSubscribed || _sessions.Count > 0)
            return;
        TickManager.Instance.OnTick.UnSubscribe(Tick);
        _tickSubscribed = false;
    }

    private static string SanitizeFilename(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        foreach (var c in Path.GetInvalidFileNameChars())
            input = input.Replace(c.ToString(), "");
        input = input.Replace("/", "").Replace("\\", "").Replace("..", "");
        input = Path.GetFileNameWithoutExtension(input);
        return string.IsNullOrWhiteSpace(input) ? null : input;
    }

    private sealed class Session
    {
        public string Name;
        public uint CharacterObjId;
        public List<Vector3> Points;
        public DateTime LastSampleAt;
    }
}
