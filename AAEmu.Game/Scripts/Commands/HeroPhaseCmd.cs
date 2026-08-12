using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Hero;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Moves the hero season between its phases.
/// </summary>
/// <remarks>
/// Not a convenience. The shipped hero_schedules windows are weeks to months long, so without a way to
/// force a phase there is no way to reach the abstain window, the ballot, or the count at all - the next
/// scheduled transition for season 5 is in September.
///
/// The override is deliberately sticky rather than momentary: an election is a sequence, and testing one
/// means sitting inside a phase long enough to do something in it.
/// </remarks>
public class HeroPhaseCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["herophase", "heroperiod"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[ranking|abstain [Name]|voting|period|count|roll|none|auto]";
    }

    public string GetCommandHelpText()
    {
        return
            "Show or force the hero season's phase.\n" +
            "  /herophase              - where the season stands, and its full schedule\n" +
            "  /herophase ranking      - leadership_ranking: leadership accrues, peer rating is open\n" +
            "  /herophase abstain      - hero_abstain: freezes the candidate list, withdrawals open\n" +
            "  /herophase abstain <Name> - force that candidate to withdraw (ignores the seat-count rule)\n" +
            "  /herophase voting       - hero_voting: freezes the candidate list and opens the ballot\n" +
            "  /herophase period       - hero_period: COUNTS the ballots, seats the winners, they serve\n" +
            "  /herophase count        - count now without changing phase (re-runs the seating)\n" +
            "  /herophase roll         - DESTRUCTIVE: move everyone's leadership to last-period, reset to 0\n" +
            "  /herophase none         - between phases, as the gaps in hero_schedules are\n" +
            "  /herophase auto         - stop forcing and follow hero_schedules again\n" +
            "A forced phase sticks until cleared, and is announced to everyone online at once.\n" +
            "Peer rating is gated on leadership_ranking, so it stops working in the other phases -\n" +
            "that is the client's own rule, not a side effect of forcing.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendNormalText(this, messageOutput, HeroElectionManager.Instance.Describe());
            return;
        }

        var verb = args[0].ToLowerInvariant();

        // "abstain" with a name withdraws that candidate; without one it is still the phase. The overload
        // reads naturally at the prompt and the two cannot be confused - a phase change takes no argument.
        if ((verb is "abstain" or "withdraw") && args.Length > 1)
        {
            CommandManager.SendNormalText(this, messageOutput,
                HeroElectionManager.Instance.ForceAbstain(args[1]));
            return;
        }

        if (verb is "roll" or "rollperiod")
        {
            // Forced: the automatic roll fires once per season, so a GM asking again means they want it
            // regardless. Destructive across every character, hence a verb of its own rather than a flag.
            var rolled = HeroElectionManager.Instance.RollPeriod(force: true);
            CommandManager.SendNormalText(this, messageOutput, rolled < 0
                ? "The leadership roll failed; see the server log."
                : $"Rolled the leadership period: {rolled} character(s) moved to last-period and reset to 0.");
            return;
        }

        if (verb is "count" or "tally")
        {
            // Separate from entering hero_period so a count can be repeated against the same ballots
            // while testing, without walking the phase back and forth.
            CommandManager.SendNormalText(this, messageOutput, HeroElectionManager.Instance.CountVotes());
            return;
        }

        if (verb is "auto" or "schedule" or "clear")
        {
            HeroElectionManager.Instance.SetOverride(null);
            CommandManager.SendNormalText(this, messageOutput,
                "Following hero_schedules again.\n" + HeroElectionManager.Instance.Describe());
            return;
        }

        var phase = verb switch
        {
            "ranking" or "leadership" or "leadership_ranking" or "1" => (HeroPhase?)HeroPhase.LeadershipRanking,
            "abstain" or "hero_abstain" or "2" => HeroPhase.HeroAbstain,
            "voting" or "vote" or "hero_voting" or "3" => HeroPhase.HeroVoting,
            "period" or "hero_period" or "serving" or "4" => HeroPhase.HeroPeriod,
            "none" or "off" or "0" => HeroPhase.None,
            _ => null
        };

        if (phase == null)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"'{args[0]}' is not a phase. Use ranking, abstain, voting, period, none or auto.");
            return;
        }

        HeroElectionManager.Instance.SetOverride(phase.Value);
        CommandManager.SendNormalText(this, messageOutput, HeroElectionManager.Instance.Describe());
    }
}
