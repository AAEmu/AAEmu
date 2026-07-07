using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Crimes;

public class CrimeSetTrialStateSubCommand : SubCommandBase
{
    public CrimeSetTrialStateSubCommand()
    {
        Title = "[Crime Set Trial State]";
        Description = "Changes a trial state.";
        CallPrefix = $"{CommandManager.CommandPrefix}crime trial_state";
        AddParameter(new StringSubCommandParameter("target", "player name||target||self", true));
        AddParameter(new NumericSubCommandParameter<uint>("trial", "trial=<trialId>", true, "trial"));
        AddParameter(new NumericSubCommandParameter<byte>("state", "state=<stateId>", true, "state"));
        AddParameter(new NumericSubCommandParameter<int>("jury", "jury=<count>", true, "jury"));
        AddParameter(new NumericSubCommandParameter<uint>("time", "time=<minutes>", true, "time"));
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

        uint trialId = parameters["trial"];
        byte state = parameters["state"];
        int jury = parameters["jury"];
        uint timeMinutes = parameters["time"];
        var time = timeMinutes * 60_000u; // time is in ms
        SendColorMessage(messageOutput, Color.White, $"Setting Trial {trialId} to {state} ({(TrialStep)state}) with Jury {jury} and Time {timeMinutes} minutes.");
        targetCharacter.SendPacket(new SCChangeTrialStatePacket(trialId, state, jury, time));
    }
}
