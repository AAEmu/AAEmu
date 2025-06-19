using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.Feature;

public class FeatureCheckSubCommand : SubCommandBase
{
    public FeatureCheckSubCommand()
    {
        Title = "[Feature]";
        Description = "Checking the characteristic features of the account using a bitwise installation";
        CallPrefix = $"{CommandManager.CommandPrefix}feature check";
    }

    public override void Execute(ICharacter character, string triggerArgument, string[] args, IMessageOutput messageOutput) =>
        Execute(character, triggerArgument, new Dictionary<string, ParameterValue>(), messageOutput);

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        foreach (var fObj in Enum.GetValues<Models.Game.Features.Feature>())
        {
            if (FeaturesManager.Fsets.Check(fObj))
                messageOutput.SendMessage("[Feature] |cFF00FF00ON  |cFF80FF80" + fObj.ToString() + "|r");
            else
                messageOutput.SendMessage("[Feature] |cFFFF0000OFF |cFF802020" + fObj.ToString() + "|r");
        }
    }
}
