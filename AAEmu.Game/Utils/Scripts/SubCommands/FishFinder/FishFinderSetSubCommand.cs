using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Scripts.Commands;

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
            FishSchoolManager.FishFinderStart((Character)character);
            SendMessage(messageOutput, $"FishFinder set start: true");
        }
        else
        {
            FishSchoolManager.StopFishFinderTickAsync((Character)character).GetAwaiter().GetResult();
            SendMessage(messageOutput, $"FishFinder set start: false");
        }

        //TODO: There is much more potential information to show on this command.
    }
}
