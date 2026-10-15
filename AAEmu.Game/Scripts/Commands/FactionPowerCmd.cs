using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>GM tool for the weekly faction activity: shows it, changes points and forces standings.</summary>
public class FactionPowerCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["factionpower"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[add|set <faction> <field> <n> | standing <faction> <superior|inferior|normal|auto> | rank]";
    }

    public string GetCommandHelpText()
    {
        return "Without arguments: this week's activity per faction, its stage, last week's total and the standing.\n" +
               "add/set <faction> <siege, siegemaintain, competition, quest, raid, raidmaintain, raidjoint or exile> <n>: change this week's points.\n" +
               "standing <faction> <superior|inferior|normal|auto>: force a standing (auto = computed again).\n" +
               "rank: rank from this week's points now instead of last week's.\n" +
               "Factions: nuia (148), haranya (149), outlaw/pirate (114) or the id.";
    }

    public static bool TryParseFaction(string text, out uint factionId)
    {
        factionId = text?.ToLowerInvariant() switch
        {
            "nuia" or "west" => 148,
            "haranya" or "harihara" or "east" => 149,
            "outlaw" or "outlaws" or "pirate" or "pirates" => 114,
            _ => uint.TryParse(text, out var id) ? id : 0
        };
        return factionId != 0;
    }

    public static string FactionName(uint factionId)
    {
        var name = FactionManager.Instance.GetFaction((FactionsEnum)factionId)?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = factionId switch
            {
                148 => "Nuia",
                149 => "Haranya",
                114 => "Outlaws",
                161 => "Pirates",
                199 => "Haranya (exiles)",
                200 => "Nuia (exiles)",
                _ => "faction"
            };
        }

        return $"{name} ({factionId})";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var power = FactionPowerManager.Instance;
        if (!power.IsStarted)
        {
            CommandManager.SendErrorText(this, messageOutput, "Faction power is not running.");
            return;
        }

        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        switch (verb)
        {
            case "":
                break;
            case "add" or "set" when args.Length >= 4:
                if (!TryParseFaction(args[1], out var factionId) || !int.TryParse(args[3], out var amount) ||
                    !power.ChangePoints(factionId, args[2], amount, verb == "set"))
                {
                    CommandManager.SendErrorText(this, messageOutput, "Unknown faction or field.");
                    return;
                }

                break;
            case "standing" when args.Length >= 3:
                if (!TryParseFaction(args[1], out var standingFaction) ||
                    !FactionPowerManager.PowerFactions().Contains(standingFaction) ||
                    !Enum.TryParse<FactionPowerStanding>(args[2] == "auto" ? "Automatic" : args[2], true, out var standing))
                {
                    CommandManager.SendErrorText(this, messageOutput, "Usage: standing <faction> <superior|inferior|normal|auto>");
                    return;
                }

                power.SetOverride(standingFaction, standing);
                break;
            case "rank":
                power.RankFromCurrentWeek();
                break;
            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                return;
        }

        var superiors = power.Superiors;
        var inferiors = power.Inferiors;
        var overrides = power.Overrides;
        var previous = power.PreviousScores().ToDictionary(s => s.FactionId, FactionPowerRules.Total);
        CommandManager.SendNormalText(this, messageOutput, $"Faction activity, week {power.Week} (resets Sunday 00:00 UTC):");
        foreach (var s in power.CurrentScores().OrderByDescending(FactionPowerRules.Total))
        {
            var total = FactionPowerRules.Total(s);
            var stage = FactionPowerGameData.Instance.Grades.LastOrDefault(g => g.MinScore <= total)?.Grade ?? 1;
            var standing = superiors.Contains(s.FactionId) ? "SUPERIOR" : inferiors.Contains(s.FactionId) ? "INFERIOR" : "normal";
            if (overrides.ContainsKey(s.FactionId))
                standing += " (forced)";
            CommandManager.SendNormalText(this, messageOutput,
                $"{FactionName(s.FactionId)}: {total} (stage {stage}), last week {previous.GetValueOrDefault(s.FactionId)} - {standing}; " +
                $"siege {s.SiegePoint}+{s.SiegeMaintainPoint}, competition {s.CompetitionPoint}, quest {s.QuestPoint:F0}, " +
                $"raid {s.RaidPoint}+{s.RaidMaintainPoint}, joint {s.RaidJointPoint}, exiles {s.ImmigratePoint}");
        }
    }
}
