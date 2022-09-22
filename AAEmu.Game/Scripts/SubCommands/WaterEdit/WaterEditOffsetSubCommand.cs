using System;
using System.Numerics;
using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.Converters;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditOffsetSubCommand : SubCommandBase 
    {
        public WaterEditOffsetSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Moves all points in the water body by a give offset.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit offset";
            AddParameter(new NumericSubCommandParameter<float>("x", "x-offset", false, -1000f, 1000f) { DefaultValue = 0f});
            AddParameter(new NumericSubCommandParameter<float>("y", "y-offset", false, -1000f, 1000f) { DefaultValue = 0f});
            AddParameter(new NumericSubCommandParameter<float>("z", "z-offset", false, -500f, 500f) { DefaultValue = 0f});
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }
            
            if (WaterEditCmd.SelectedWater == null)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] You need to select a water body first!|r");
                return;
            }
            
            var xoff = GetOptionalParameterValue<float>(parameters, "x", 0f);
            var yoff = GetOptionalParameterValue<float>(parameters, "y", 0f);
            var zoff = GetOptionalParameterValue<float>(parameters, "z", 0f);

            var offset = new Vector3(xoff, yoff, zoff);

            lock (WaterEditCmd.SelectedWorld.Water._lock)
            {
                for (var i = 0; i < WaterEditCmd.SelectedWater.Points.Count; i++)
                    WaterEditCmd.SelectedWater.Points[i] = WaterEditCmd.SelectedWater.Points[i] + offset;
            }

            WaterEditCmd.ShowSelectedArea(character);
            character.SendMessage($"[WaterEdit] Move all points in |cFFFFFFFF#{WaterEditCmd.SelectedWater.Name}|r by |cFF00FF00{offset}|r");
        }
    }
}
