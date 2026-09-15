using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Utils.Scripts;

using TrialCase = AAEmu.Game.Models.Game.Justice.Trial;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Trial test hook.
/// </summary>
/// <remarks>
/// A trial is entered through client windows only: the defendant's imprison-or-trial offer and the
/// juror's invite, then the record review and the verdict window. Nothing on the server can drive a
/// case end to end on its own, which is exactly what these paths need to be tested for. Every
/// subcommand below does what one of those windows does - the same call the matching packet handler
/// makes - so a case can be run from a GM client.
/// </remarks>
public class Trial : ICommand
{
    public string[] CommandNames { get; set; } = ["trial"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        // No "|r" anywhere in here: the chat colour parser eats it as a reset code.
        return "<start,join,read,vote,skip,audience,release,status> [player or row]";
    }

    public string GetCommandHelpText()
    {
        return "Trial test hook - does what the client's trial windows do.\n" +
               "  " + CommandManager.CommandPrefix + "trial start [player] - open a trial for that player (target, else yourself)\n" +
               "  " + CommandManager.CommandPrefix + "trial join [player] - seat them on the bench of the court's gathering trial\n" +
               "  " + CommandManager.CommandPrefix + "trial read [player] - (juror) they are done reading the crime record\n" +
               "  " + CommandManager.CommandPrefix + "trial vote <row> [player] - (juror) 1 not guilty, 2-6 guilty tiers\n" +
               "  " + CommandManager.CommandPrefix + "trial skip [player] - (defendant) cut the final statement short\n" +
               "  " + CommandManager.CommandPrefix + "trial audience [player] - (onlooker) join the nearest trial's gallery\n" +
               "  " + CommandManager.CommandPrefix + "trial release [player] - end a sentence, so a test subject is not jailed for half an hour\n" +
               "  " + CommandManager.CommandPrefix + "trial status - the cases the courts are holding";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var manager = TrialManager.Instance;
        var sub = args[0].ToLowerInvariant();
        var nameArg = args.Length > 1 ? args[1] : null;
        var rowArg = (string)null;

        if (sub == "vote")
        {
            // vote takes the row first, so the player is the third word.
            rowArg = nameArg;
            nameArg = args.Length > 2 ? args[2] : null;
        }

        if (sub == "status")
        {
            PrintStatus(character, messageOutput);
            return;
        }

        var subject = nameArg == null
            ? character.CurrentTarget as Character ?? character
            : WorldManager.Instance.GetTargetOrSelf(character, nameArg, out _);

        if (subject == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "No such player");
            return;
        }

        switch (sub)
        {
            case "start":
            {
                var trial = manager.StartTrial(subject);
                if (trial == null)
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        $"{subject.Name} already has a case open");
                    return;
                }

                CommandManager.SendNormalText(this, messageOutput,
                    $"Trial {trial.Id}: {subject.Name} at court {trial.Court}, state {trial.State}");
                return;
            }

            case "join":
            {
                var trial = FindGatheringTrial(manager, subject);
                if (trial == null)
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        $"No trial is gathering a bench for {subject.Name}");
                    return;
                }

                // Same two steps as the client: ask for a waiting number, then accept the invite the
                // court sends back, which promises a chair and summons them to it.
                manager.OnWaitingNumberRequest(subject);
                manager.OnReplyInvite(subject, true, trial.Id);
                CommandManager.SendNormalText(this, messageOutput,
                    $"Trial {trial.Id}: {subject.Name} summoned to a chair");
                return;
            }

            case "audience":
            {
                // An onlooker is not on the case - the court they watch from is the one whose trial is
                // nearest them, so this resolves the trial itself.
                manager.JoinAudience(subject, TrialManager.PlayerTrialType);
                CommandManager.SendNormalText(this, messageOutput,
                    $"{subject.Name} asked to watch the nearest trial");
                return;
            }

            case "release":
            {
                if (!JusticeManager.IsPrisoner(subject))
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        $"{subject.Name} is not serving a sentence");
                    return;
                }

                JusticeManager.Instance.ReleasePrisoner(subject);
                CommandManager.SendNormalText(this, messageOutput,
                    $"{subject.Name} is out of the cell, crime {subject.CrimePoint}");
                return;
            }

            case "read":
            case "vote":
            case "skip":
            {
                var trial = manager.GetLiveTrialOf(subject.Id);
                if (trial == null)
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        $"{subject.Name} is not in a trial");
                    return;
                }

                switch (sub)
                {
                    case "read":
                        manager.OnEndTestimony(subject, trial.Id, SeatOf(trial, subject));
                        break;
                    case "vote":
                        if (!byte.TryParse(rowArg, out var row) || !TrialVerdictRules.IsValidChoice(row))
                        {
                            CommandManager.SendErrorText(this, messageOutput,
                                "Verdict row must be 1 (not guilty) or 2-6 (guilty tiers)");
                            return;
                        }

                        manager.OnVerdict(subject, trial.Id, SeatOf(trial, subject), row);
                        break;
                    case "skip":
                        manager.OnSkipFinalStatement(subject, trial.Id);
                        break;
                }

                CommandManager.SendNormalText(this, messageOutput,
                    $"Trial {trial.Id}: {sub} for {subject.Name}, state {trial.State}");
                return;
            }

            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                return;
        }
    }

    private static TrialCase FindGatheringTrial(TrialManager manager, Character subject) =>
        manager.LiveTrials
            .Where(t => t.State == TrialState.WaitingJury && manager.GetLiveTrialOf(subject.Id) == null)
            .OrderBy(t => t.Id)
            .FirstOrDefault();

    private static int SeatOf(TrialCase trial, Character subject) =>
        trial.FindJuror(subject.Id)?.Seat ?? 0;

    private void PrintStatus(Character character, IMessageOutput messageOutput)
    {
        var trials = TrialManager.Instance.LiveTrials.OrderBy(t => t.Id).ToArray();
        if (trials.Length == 0)
        {
            CommandManager.SendNormalText(this, messageOutput, "No live trials");
            return;
        }

        foreach (var trial in trials)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"Trial {trial.Id}: {trial.DefendantName} at court {trial.Court}, state {trial.State}, " +
                $"queue {trial.QueueOrder}, crime {trial.CrimePoint}");
            CommandManager.SendNormalText(this, messageOutput,
                $"  seats {trial.Jurors.Count} summoned {trial.Summoned.Count} invited {trial.Invited.Count} " +
                $"read {trial.ReadRecordCount} voted {trial.VotedCount} " +
                $"guilty {trial.GuiltyVotes} not-guilty {trial.NotGuiltyVotes}");

            foreach (var juror in trial.Jurors.OrderBy(j => j.Seat))
            {
                var name = WorldManager.Instance.GetCharacterById(juror.CharacterId)?.Name
                           ?? juror.CharacterId.ToString();
                CommandManager.SendNormalText(this, messageOutput,
                    $"  chair {juror.Seat}{(juror.IsWest ? " west" : " east")}: {name} " +
                    $"read {juror.ReadRecord} vote {VoteText(juror)}");
            }
        }
    }

    private static string VoteText(TrialJuror juror)
    {
        if (!juror.Voted)
            return "-";

        var tier = TrialVerdictRules.GuiltyTier(juror.Choice);
        return tier == 0 ? "not guilty" : $"guilty {tier}";
    }
}
