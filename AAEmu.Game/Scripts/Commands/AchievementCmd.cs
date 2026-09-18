using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// The achievement surface: what the character's records and achievements say, and the handles to move them.
/// </summary>
public class AchievementCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["achievement", "ach"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "list|check|complete|reset|record|push [args]";
    }

    public string GetCommandHelpText()
    {
        return "Achievements. 'list [n]' shows progress (default 20), 'check <id>' the evaluation of one, " +
               "'complete <id>'/'reset <id>' change it, 'record <recordId> <value>' reports a record, " +
               "'push [all]' resends the client's list.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character == null)
            return;

        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        switch (action)
        {
            case "list":
                List(character, args, messageOutput);
                break;
            case "check":
                Check(character, args, messageOutput);
                break;
            case "complete":
                Complete(character, args, messageOutput);
                break;
            case "reset":
                Reset(character, args, messageOutput);
                break;
            case "record":
                Record(character, args, messageOutput);
                break;
            case "push":
                Push(character, args, messageOutput);
                break;
            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown action '{action}'. {GetCommandLineHelp()}");
                break;
        }
    }

    private void List(Character character, string[] args, IMessageOutput messageOutput)
    {
        var limit = args.Length > 1 && int.TryParse(args[1], out var parsed) ? Math.Max(1, parsed) : 20;
        var rows = AchievementManager.Instance.BuildList(character);
        CommandManager.SendNormalText(this, messageOutput,
            $"[Achievements] {character.Name}: {rows.Count} with progress");

        foreach (var row in rows.Take(limit))
        {
            var achievement = AchievementGameData.Instance.GetAchievement(row.Id);
            var state = achievement != null && row.Complete != default ? "complete" : "in progress";
            CommandManager.SendNormalText(this, messageOutput,
                $"  {row.Id} {row.Amount}/{achievement?.CompleteNum ?? 0} {state} {achievement?.Name}");
        }
    }

    private void Check(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var achievementId))
        {
            CommandManager.SendErrorText(this, messageOutput, "check <achievementId>");
            return;
        }

        var achievement = AchievementGameData.Instance.GetAchievement(achievementId);
        if (achievement == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"No achievement {achievementId}");
            return;
        }

        var objectives = AchievementGameData.Instance.GetObjectives(achievementId);
        CommandManager.SendNormalText(this, messageOutput,
            $"[{achievementId}] {achievement.Name} | complete_num={achievement.CompleteNum} " +
            $"objectives={objectives.Count} | stored amount={character.Achievements.Amount(achievementId)} " +
            $"complete={character.Achievements.IsComplete(achievementId)}");

        foreach (var objective in objectives.Take(10))
        {
            var record = AchievementGameData.Instance.GetRecord(objective.RecordId);
            CommandManager.SendNormalText(this, messageOutput,
                $"  objective {objective.Id} record {objective.RecordId} " +
                $"({record?.KindId.ToString() ?? "unknown"}) = {character.Records.Get(objective.RecordId)}");
        }

        // Re-evaluating is a no-op when nothing moved, so this doubles as a repair.
        var result = AchievementManager.Instance.Refresh(character, achievementId);
        CommandManager.SendNormalText(this, messageOutput,
            $"  refreshed: amountChanged={result.AmountChanged} newlyCompleted={result.NewlyCompleted}");
    }

    private void Complete(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var achievementId))
        {
            CommandManager.SendErrorText(this, messageOutput, "complete <achievementId>");
            return;
        }

        var completed = AchievementManager.Instance.Complete(character, achievementId);
        CommandManager.SendNormalText(this, messageOutput,
            completed ? $"Completed {achievementId}" : $"{achievementId} was already complete (or does not exist)");
    }

    private void Reset(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var achievementId))
        {
            CommandManager.SendErrorText(this, messageOutput, "reset <achievementId>");
            return;
        }

        var reset = AchievementManager.Instance.Reset(character, achievementId);
        CommandManager.SendNormalText(this, messageOutput,
            reset ? $"Reset {achievementId}" : $"No achievement {achievementId}");
    }

    private void Record(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 3 || !uint.TryParse(args[1], out var recordId) || !int.TryParse(args[2], out var value))
        {
            CommandManager.SendErrorText(this, messageOutput, "record <recordId> <value>");
            return;
        }

        var record = AchievementGameData.Instance.GetRecord(recordId);
        if (record == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"No record {recordId}");
            return;
        }

        var moved = AchievementManager.Instance.SetRecord(character, recordId, value);
        CommandManager.SendNormalText(this, messageOutput,
            $"Record {recordId} ({record.KindId}) = {value}; {moved} achievement(s) moved");
    }

    private void Push(Character character, string[] args, IMessageOutput messageOutput)
    {
        var all = args.Length > 1 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase);
        var packets = AchievementManager.Instance.SendList(character, all);
        CommandManager.SendNormalText(this, messageOutput,
            $"Pushed {packets} achievement packet(s){(all ? " (full list)" : "")}");
    }
}
