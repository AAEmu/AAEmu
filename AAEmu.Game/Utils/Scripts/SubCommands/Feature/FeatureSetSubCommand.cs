using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.Feature;

public class FeatureSetSubCommand : SubCommandBase
{
    public FeatureSetSubCommand()
    {
        Title = "[Feature]";
        Description = "Change the characteristic features of the account using a bitwise installation";
        CallPrefix = $"{CommandManager.CommandPrefix}feature set";
        AddParameter(new NumericSubCommandParameter<int>("feature", "feature id", true, 0, 87));
        AddParameter(new StringSubCommandParameter("enable", "enable", true));
    }

    public override void Execute(ICharacter character, string triggerArgument, string[] args, IMessageOutput messageOutput) =>
        Execute(character, triggerArgument, new Dictionary<string, ParameterValue>(), messageOutput);

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        var feature = -1;
        feature = parameters["feature"];

        if (feature == -1)
        {
            SendColorMessage(messageOutput, Color.Red, $"Error Feature set!");
            return;
        }
        string enableString = parameters["enable"];
        var enable = enableString == "true";

        if (FeaturesManager.Fsets.Set((Models.Game.Features.Feature)feature, enable))
        {
            //TODO: There is much more potential information to show on this command.
            SendMessage(messageOutput, $"Feature set {feature}, {enable}. Need reload character");
        }
        else
        {
            SendColorMessage(messageOutput, Color.Red, $"Error Feature set!");
        }
    }
}
