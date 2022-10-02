﻿using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.Feature
{
    public class FeatureCheckSubCommand : SubCommandBase
    {
        public FeatureCheckSubCommand()
        {
            Title = "[Feature]";
            Description = "Checking the characteristic features of the account using a bitwise installation";
            CallPrefix = $"{CommandManager.CommandPrefix}feature check";
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args) =>
            Execute(character, triggerArgument, new Dictionary<string, ParameterValue>());

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            foreach (var fObj in Enum.GetValues(typeof(Models.Game.Features.Feature)))
            {
                var f = (Models.Game.Features.Feature)fObj;
                if (FeaturesManager.Fsets.Check(f))
                    character.SendMessage("[Feature] |cFF00FF00ON  |cFF80FF80" + f.ToString() + "|r");
                else
                    character.SendMessage("[Feature] |cFFFF0000OFF |cFF802020" + f.ToString() + "|r");
            }
        }
    }
}
