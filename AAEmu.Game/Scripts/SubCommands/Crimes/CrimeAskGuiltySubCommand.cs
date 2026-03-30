using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Crimes;

public class CrimeAskGuiltySubCommand : SubCommandBase
{
    public CrimeAskGuiltySubCommand()
    {
        Title = "[Crime Ask Guilty]";
        Description = "Asks player if they plead guilty or not.";
        CallPrefix = $"{CommandManager.CommandPrefix}crime ask_guilty";
        AddParameter(new StringSubCommandParameter("target", "player name||target||self", true));
        AddParameter(new NumericSubCommandParameter<uint>("crime", "crime=<points>", true, "crime"));
        AddParameter(new NumericSubCommandParameter<int>("time", "time=<minutes>", true, "time"));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string,
        ParameterValue> parameters, IMessageOutput messageOutput)
    {
        Character targetCharacter;
        var selfCharacter = (Character)character;

        var firstParameter = parameters["target"].ToString();
        if (firstParameter == "target")
        {
            if (selfCharacter.CurrentTarget is null || selfCharacter.CurrentTarget is not Character)
            {
                SendColorMessage(messageOutput, Color.Red, "Please select a valid character player");
                return;
            }

            targetCharacter = selfCharacter.CurrentTarget as Character;
        }
        else if (firstParameter == "self")
        {
            targetCharacter = selfCharacter;
        }
        else
        {
            var player = WorldManager.Instance.GetCharacter(firstParameter);
            if (player is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Character player: {firstParameter} was not found.");
                return;
            }

            targetCharacter = player;
        }

        if (targetCharacter is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Character player: {firstParameter} was not found.");
            return;
        }

        int jailTime = parameters["time"];
        uint crimePoints = parameters["crime"];
        SendColorMessage(messageOutput, Color.White, $"Asking {targetCharacter.Name} if they plead guilty for {jailTime} minutes ({crimePoints} crime points).");
        targetCharacter.SendPacket(new SCAskImprisonOrTrialPacket(crimePoints, jailTime));
    }
}
