using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.FishFinder;

public class FishFinderSetSubCommand : SubCommandBase
{
    public FishFinderSetSubCommand()
    {
        Title = "[FishFinder setup]";
        Description = "FishFinder setup";
        CallPrefix = $"{CommandManager.CommandPrefix}set";
        AddParameter(new StringSubCommandParameter("start", "start", true));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        string start = parameters["start"];
        if (start == "true")
        {
            RadarManager.Instance.RegisterForFishSchool((Character)character, 1000f);
            SendMessage(messageOutput, $"FishFinder set start: true");
        }
        else
        {
            RadarManager.Instance.RegisterForFishSchool((Character)character, 0f);
            SendMessage(messageOutput, $"FishFinder set start: false");
        }

        //TODO: There is much more potential information to show on this command.
    }
}
