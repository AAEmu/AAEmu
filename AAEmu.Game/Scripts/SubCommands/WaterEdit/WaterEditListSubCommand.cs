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
    public class WaterEditListSubCommand : SubCommandBase 
    {
        public WaterEditListSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Lists all bodies of water in your current world.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit list";
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }
            character.SendMessage($"[WaterEdit] World {world.Name} has {world.Water.Areas.Count} water bodies defined:");
            foreach (var area in world.Water.Areas)
            {
                character.SendMessage($"|cFFFFFFFF{area.Name}|r ({area.Id}) => {area.Points.Count} points");
            }
        }
    }
}
