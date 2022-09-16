using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditSetBottomSubCommand : SubCommandBase 
    {
        public WaterEditSetBottomSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Set the bottom location of all points in the water body";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit setbottom";
            AddParameter(new NumericSubCommandParameter<float>("bottom", "bottom height", true, 0f, 4096f));
            AddParameter(new NumericSubCommandParameter<int>("point", "point=0", false, -1, 9999) { DefaultValue = -1 });
        }

        public override void Execute(ICharacter character, string triggerArgument,
            IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }

            if (WaterEdit.SelectedWater == null)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] You need to select a water body first!|r");
                return;
            }

            if (WaterEdit.SelectedWorld != world)
            {
                character.SendMessage(
                    $"|cFFFF0000[WaterEdit] Currently selected water is not in the same world as you! ({WaterEdit.SelectedWorld.Name})|r");
                return;
            }

            float newBottom = parameters["bottom"];
            var onePoint = GetOptionalParameterValue<int>(parameters, "point", -1);

            for (var i = 0; i < WaterEdit.SelectedWater.Points.Count; i++)
            {
                if ((onePoint >= 0) && (onePoint != i))
                    continue;

                WaterEdit.SelectedWater.Points[i] = new Vector3(WaterEdit.SelectedWater.Points[i].X,
                    WaterEdit.SelectedWater.Points[i].Y, newBottom);
            }

            WaterEdit.ShowSelectedArea(character);
            if (onePoint >= 0)
                character.SendMessage(
                    $"[WaterEdit] Z position for point |cFF00FF00{onePoint}|r in |cFFFFFFFF{WaterEdit.SelectedWater.Name}|r has been set to |cFF00FF00{newBottom}!|r");
            else
                character.SendMessage(
                    $"[WaterEdit] Z position for all points in |cFFFFFFFF{WaterEdit.SelectedWater.Name}|r have been set to |cFF00FF00{newBottom}!|r");
        }
    }
}
