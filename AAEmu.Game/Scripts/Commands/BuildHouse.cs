using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Scripts.Commands
{
    public class BuildHouse : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "build", "build_house" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Advances the targetted house one step further";
        }

        public void Execute(Character character, string[] args)
        {
            var targetHouse = character.CurrentTarget as House;
            if (targetHouse == null)
            {
                character.SendMessage("You must target a house");
                return;
            }

            targetHouse.AddBuildAction();
            character.BroadcastPacket(
                new SCHouseBuildProgressPacket(
                    targetHouse.TlId,
                    targetHouse.ModelId,
                    targetHouse.AllAction,
                    targetHouse.CurrentStep == -1 ? targetHouse.AllAction : targetHouse.CurrentAction
                ),
                true
            );

            if (targetHouse.CurrentStep == -1)
            {
                var doodads = targetHouse.AttachedDoodads.ToArray();
                foreach (var doodad in doodads)
                    doodad.Spawn();
            }
        }
    }
}
