using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.FishFinder
{
    public class FishFinderSetSubCommand : SubCommandBase
    {
        public FishFinderSetSubCommand()
        {
            Title = "[FishFinder setup]";
            Description = "FishFinder setup";
            CallPrefix = $"{CommandManager.CommandPrefix}set";
            AddParameter(new StringSubCommandParameter("start", "start", true));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            string start = parameters["start"];
            if (start == "true")
            {
                FishSchoolManager.FishFinderStart((Character)character);
                SendMessage(character, $"FishFinder set start: true");
            }
            else
            {
                FishSchoolManager.StopFishFinderTickAsync((Character)character).GetAwaiter().GetResult();
                SendMessage(character, $"FishFinder set start: false");
            }

            //TODO: There is much more potential information to show on this command.
        }
    }
}