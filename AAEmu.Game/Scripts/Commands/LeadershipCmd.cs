using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Hero;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Reads and edits leadership, the stat the Hero election runs on.
/// </summary>
/// <remarks>
/// Exists because leadership has no gameplay source yet. SpecialEffectType.GiveLeadershipPoint (175) is
/// implemented but only two shipped rows use it, and the peer-rating payout only moves people a few
/// points per evaluation. This is still the quickest way to put a test character over the leadership
/// hero_conditions demands for rating and candidacy.
/// </remarks>
public class LeadershipCmd : ICommand
{
    private static readonly string[] Verbs = ["add", "set", "setcumul", "setlast"];

    public string[] CommandNames { get; set; } = ["leadership", "leader"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[target] <add|set|setcumul|setlast> <amount>";
    }

    public string GetCommandHelpText()
    {
        var condition = HeroConditions.Current;
        return
            "Show or change leadership, the stat the Hero election is gated on.\n" +
            "An optional target name comes FIRST; without one it uses your target, else yourself.\n" +
            "Offline characters can be targeted by name - the change is written straight to the database.\n" +
            "  /leadership                      - show all three totals\n" +
            "  /leadership add <amount>         - add to period + lifetime (negative reduces period only)\n" +
            "  /leadership set <value>          - set CURRENT PERIOD leadership (what ranks you)\n" +
            "  /leadership setcumul <value>     - set LIFETIME leadership (never reset by a rollover)\n" +
            "  /leadership setlast <value>      - set LAST PERIOD leadership (the historical row)\n" +
            "  /leadership <PlayerName> set 500 - target by name, online or not\n" +
            "The three totals are independent; no verb writes more than the one it names.\n" +
            $"This season's hero_conditions row wants {condition.VotableLeadershipPoint} leadership and " +
            $"level {condition.VotableLevel} to rate or vote.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        // A leading argument that is not a verb is a target name. Deciding it that way, rather than by
        // "does someone with that name happen to be online", is what lets an offline name resolve at all:
        // the stock GetTargetOrSelf silently falls back to self when the lookup misses, so
        // "/leadership SomeoneOffline set 500" quietly set the GM's own leadership instead.
        string name = null;
        var firstArg = 0;
        if (args.Length > 0 && !Verbs.Contains(args[0].ToLowerInvariant()))
        {
            name = args[0];
            firstArg = 1;
        }

        var online = name != null
            ? WorldManager.Instance.GetCharacter(name)
            : character.CurrentTarget as Character ?? character;

        OfflineLeadership offline = null;
        if (online == null && !TryLoadOffline(name, out offline))
        {
            CommandManager.SendNormalText(this, messageOutput, $"No character named '{name}'.");
            return;
        }

        var who = online?.Name ?? offline.Name;

        if (args.Length <= firstArg)
        {
            Report(this, messageOutput, who, online, offline);
            return;
        }

        var verb = args[firstArg].ToLowerInvariant();
        if (args.Length <= firstArg + 1 || !int.TryParse(args[firstArg + 1], out var amount))
        {
            CommandManager.SendNormalText(this, messageOutput, $"'{verb}' needs a whole number, e.g. /leadership {verb} 500");
            return;
        }

        string result;
        if (online != null)
        {
            result = Apply(online, verb, amount);
            HeroManager.PublishLeadership(online);
        }
        else
        {
            result = Apply(offline, verb, amount);
            SaveOffline(offline);
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"{who} {result}{(online == null ? "  (offline, written to the database)" : "")}");

        if (online != null && character.Id != online.Id)
            online.SendMessage($"[GM] {character.Name} changed your leadership.");
    }

    /// <summary>Applies a verb to a loaded character. Returns the resulting totals.</summary>
    private static string Apply(Character target, string verb, int amount)
    {
        switch (verb)
        {
            case "setlast": target.SetLastSeasonLeadership(amount); break;
            case "setcumul": target.SetAccumulatedLeadership(amount); break;
            case "add": target.AddLeadership(amount); break;
            default: target.SetLeadership(amount); break;
        }

        return $"-> period {target.LeadershipPoint}, lifetime {target.AccumulatedLeadershipPoint}, " +
               $"last period {target.LeadershipPeriodPoint}, today {target.DailyLeadershipPoint}";
    }

    /// <summary>
    /// The same verbs against a character that is not loaded.
    /// </summary>
    /// <remarks>
    /// Mirrors Character.AddLeadership deliberately rather than sharing it: that method also moves the
    /// daily counter and its rollover stamp, which need a live character. Seeding a ranking with more
    /// than one name is the point here, and that only needs the three totals.
    /// </remarks>
    private static string Apply(OfflineLeadership target, string verb, int amount)
    {
        switch (verb)
        {
            case "setlast":
                target.LastPeriod = Math.Max(0, amount);
                break;
            case "setcumul":
                target.Lifetime = Math.Max(0, amount);
                break;
            case "add":
                target.Period = (int)Math.Clamp((long)target.Period + amount, 0L, int.MaxValue);
                // Lifetime follows awards only, never losses - same rule as the online path.
                if (amount > 0)
                    target.Lifetime = (int)Math.Clamp((long)target.Lifetime + amount, 0L, int.MaxValue);
                break;
            default:
                target.Period = Math.Max(0, amount);
                break;
        }

        return $"-> period {target.Period}, lifetime {target.Lifetime}, last period {target.LastPeriod}";
    }

    private static void Report(ICommand cmd, IMessageOutput output, string who, Character online, OfflineLeadership offline)
    {
        if (online != null)
        {
            CommandManager.SendNormalText(cmd, output,
                $"{who} leadership: period {online.LeadershipPoint}, lifetime {online.AccumulatedLeadershipPoint}, " +
                $"last period {online.LeadershipPeriodPoint}, earned today {online.DailyLeadershipPoint}, " +
                $"reputation {online.Reputation}");
        }
        else
        {
            CommandManager.SendNormalText(cmd, output,
                $"{who} leadership (offline): period {offline.Period}, lifetime {offline.Lifetime}, " +
                $"last period {offline.LastPeriod}");
        }
    }

    private sealed class OfflineLeadership
    {
        public uint Id { get; init; }
        public string Name { get; init; }
        public int Period { get; set; }
        public int Lifetime { get; set; }
        public int LastPeriod { get; set; }
    }

    private static bool TryLoadOffline(string name, out OfflineLeadership result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT `id`, `name`, `leadership_point`, `accumulated_leadership_point`, `leadership_period_point` " +
            "FROM `characters` WHERE `name` = @name AND `deleted` = 0 LIMIT 1";
        command.Parameters.AddWithValue("@name", name);
        command.Prepare();

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        result = new OfflineLeadership
        {
            Id = reader.GetUInt32(0),
            Name = reader.GetString(1),
            Period = reader.GetInt32(2),
            Lifetime = reader.GetInt32(3),
            LastPeriod = reader.GetInt32(4)
        };
        return true;
    }

    /// <summary>
    /// Writes the three totals back with a targeted UPDATE.
    /// </summary>
    /// <remarks>
    /// Named columns rather than the character save path: that is a REPLACE INTO of the whole row built
    /// from a loaded Character, and there is none here. Touching only these three also means the edit
    /// cannot be clobbered by unrelated stale state.
    /// </remarks>
    private static void SaveOffline(OfflineLeadership target)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE `characters` SET `leadership_point` = @p, `accumulated_leadership_point` = @a, " +
            "`leadership_period_point` = @l WHERE `id` = @id";
        command.Parameters.AddWithValue("@p", target.Period);
        command.Parameters.AddWithValue("@a", target.Lifetime);
        command.Parameters.AddWithValue("@l", target.LastPeriod);
        command.Parameters.AddWithValue("@id", target.Id);
        command.Prepare();
        command.ExecuteNonQuery();
    }

}
