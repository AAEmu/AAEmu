using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Crimes;

public class CrimeJuryInviteSubCommand : SubCommandBase
{
    public CrimeJuryInviteSubCommand()
    {
        Title = "[Crime Jury Invite]";
        Description = "Does a jury invite request";
        CallPrefix = $"{CommandManager.CommandPrefix}crime jury_invite";
        AddParameter(new StringSubCommandParameter("target", "player name||target||self", true));
        AddParameter(new StringSubCommandParameter("defendant", "defendant=<name>", false, "defendant"));
        AddParameter(new NumericSubCommandParameter<uint>("trial", "trial=<trialId>", false, "trial"));
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

        var trialId = GetOptionalParameterValue<uint>(parameters, "trial", 0);
        var defendantName = GetOptionalParameterValue(parameters, "defendant", "WantedCriminal");
        SendColorMessage(messageOutput, Color.White, $"Summoning {targetCharacter.Name} to trial {trialId}");
        targetCharacter.SendPacket(new SCInviteJuryPacket(defendantName, trialId));
    }
}
