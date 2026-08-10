using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Reads reputation and runs the evaluation that turns it into leadership.
/// </summary>
/// <remarks>
/// The evaluation is on a 12-hour cron, which is far too slow to test against. This is the same call the
/// DailyResetReputation GM command makes, reachable from the server console.
/// </remarks>
public class ReputationCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["reputation", "rep"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[target] | eval | standings";
    }

    public string GetCommandHelpText()
    {
        return
            "Peer-rating standing, and the evaluation that pays it out as leadership.\n" +
            "  /reputation              - your standing, or your target's\n" +
            "  /reputation <PlayerName> - that character's standing, online or not\n" +
            "  /reputation standings    - everyone currently carrying reputation\n" +
            "  /reputation eval         - run the Hero Qualification Evaluation NOW\n" +
            "Rating raises the target's reputation only. Leadership is paid at the evaluation, by\n" +
            "ranking each nation's rated characters and applying the reputation_rewards bands.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;

        switch (verb)
        {
            case "eval":
                CommandManager.SendNormalText(this, messageOutput, ReputationManager.Instance.Evaluate());
                return;

            case "standings":
                ReportStandings(this, messageOutput);
                return;
        }

        var name = args.Length > 0 ? args[0] : null;
        var online = name != null
            ? WorldManager.Instance.GetCharacter(name)
            : character.CurrentTarget as Character ?? character;

        if (online != null)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"{online.Name}: reputation {online.Reputation}, period leadership {online.LeadershipPoint}");
            return;
        }

        if (TryLoadOffline(name, out var who, out var reputation))
        {
            CommandManager.SendNormalText(this, messageOutput, $"{who}: reputation {reputation}  (offline)");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput, $"No character named '{name}'.");
    }

    /// <remarks>
    /// Reads the database rather than loaded characters: the field an evaluation ranks is mostly offline,
    /// so a listing built from who is logged in would not resemble what the payout will see.
    /// </remarks>
    private static void ReportStandings(ICommand cmd, IMessageOutput output)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT `name`, `reputation` FROM `characters` WHERE `reputation` > 0 AND `deleted` = 0 " +
            "ORDER BY `reputation` DESC LIMIT 30";
        command.Prepare();

        var any = false;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            any = true;
            CommandManager.SendNormalText(cmd, output, $"  {reader.GetString(0)}  {reader.GetInt32(1)}");
        }

        if (!any)
            CommandManager.SendNormalText(cmd, output, "Nobody is carrying reputation this period.");
    }

    private static bool TryLoadOffline(string name, out string who, out int reputation)
    {
        who = null;
        reputation = 0;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT `name`, `reputation` FROM `characters` WHERE `name` = @name AND `deleted` = 0 LIMIT 1";
        command.Parameters.AddWithValue("@name", name);
        command.Prepare();

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        who = reader.GetString(0);
        reputation = reader.GetInt32(1);
        return true;
    }
}
