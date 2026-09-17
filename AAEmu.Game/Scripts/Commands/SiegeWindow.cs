using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class SiegeWindow : ICommand
{
    public string[] CommandNames { get; set; } = ["siegewindow"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[zoneGroupId] | teams [zoneGroupId]";
    }

    public string GetCommandHelpText()
    {
        return "With no argument, toggles a testing override that forces the Dominion declare window open for a " +
               "zone group (defaults to your current zone group), regardless of the real siege_plans/siege_zones " +
               "schedule. Not persisted - resets on World restart. " +
               "With 'teams', prints the raid teams the siege window would list for a zone group and pushes that " +
               "list to you, so the teams can be inspected without waiting for the siege's phase to come round.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length > 0 && args[0].Equals("teams", StringComparison.OrdinalIgnoreCase))
        {
            ShowRaidTeams(character, args.AsSpan(1), messageOutput);
            return;
        }

        uint zoneGroupId;
        if (args.Length > 0)
        {
            if (!uint.TryParse(args[0], out zoneGroupId))
            {
                CommandManager.SendErrorText(this, messageOutput, "Usage: /siegewindow [zoneGroupId] | teams [zoneGroupId]");
                return;
            }
        }
        else
        {
            if (!TryGetCurrentZoneGroup(character, messageOutput, out zoneGroupId))
                return;
        }

        var nowForced = SiegeManager.Instance.ToggleDeclareWindowOverride(zoneGroupId);
        CommandManager.SendNormalText(this, messageOutput,
            nowForced
                ? $"Declare window FORCED OPEN for zone group {zoneGroupId}."
                : $"Declare window override cleared for zone group {zoneGroupId} - back to the real schedule.");
    }

    /// <summary>
    /// Reads the raid teams for a zone group off the registration roster and hands them to the caller the same
    /// way the window's own request does, so what the next siege will list can be read without a live siege.
    /// </summary>
    private void ShowRaidTeams(Character character, ReadOnlySpan<string> args, IMessageOutput messageOutput)
    {
        ushort zoneGroupId;
        if (args.Length > 0)
        {
            if (!ushort.TryParse(args[0], out zoneGroupId))
            {
                CommandManager.SendErrorText(this, messageOutput, "Usage: /siegewindow teams [zoneGroupId]");
                return;
            }
        }
        else
        {
            if (!TryGetCurrentZoneGroup(character, messageOutput, out var currentZoneGroupId))
                return;
            zoneGroupId = (ushort)currentZoneGroupId;
        }

        var teams = SiegeManager.Instance.GetRaidTeams(zoneGroupId);
        if (teams.Count == 0)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"Zone group {zoneGroupId}: no raid teams - nobody is registered.");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput, $"Zone group {zoneGroupId}: {teams.Count} raid team(s).");
        foreach (var team in teams)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"  team {team.Team}: faction {team.FactionId}, {(team.Defense ? "defence" : "offence")}, " +
                $"{team.MemberCount} registered, waiting to start: {team.IsWaitWar}");
        }

        character.SendPacket(new SCAllSiegeRaidTeamInfoPacket(teams));
    }

    private bool TryGetCurrentZoneGroup(Character character, IMessageOutput messageOutput, out uint zoneGroupId)
    {
        zoneGroupId = 0;
        var zone = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId);
        if (zone != null)
        {
            zoneGroupId = zone.GroupId;
            return true;
        }

        CommandManager.SendErrorText(this, messageOutput,
            "Could not resolve your current zone group - specify one explicitly.");
        return false;
    }
}
