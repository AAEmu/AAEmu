using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class SlaveRemoveSubCommand : SubCommandBase
    {
        public SlaveRemoveSubCommand()
        {
            Title = "[Slave Remove]";
            Description = "Remove a targeted slave or using an slave <ObjId>";
            CallPrefix = $"{CommandManager.CommandPrefix}slave remove";
            AddParameter(new StringSubCommandParameter("target", "target", true, "target", "id"));
            AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            Models.Game.Units.Slave slave;
            if (parameters.TryGetValue("ObjId", out ParameterValue objId)) 
            { 
                slave = (Models.Game.Units.Slave)WorldManager.Instance.GetGameObject(objId);
                if (slave is null)
                {
                    SendColorMessage(character, Color.Red, "Slave with objId {0} does not exist |r", objId);
                    return;
                }
            }
            else
            {
                var currentTarget = ((Character)character).CurrentTarget;
                if (currentTarget is null || !(currentTarget is Npc))
                {
                    SendColorMessage(character, Color.Red, "You need to target a Slave first");
                    return;
                }
                slave = (Models.Game.Units.Slave) currentTarget;
            }

            // Remove Slave
            slave.Spawner.Id = 0xffffffff; // removed from the game manually (укажем, что не надо сохранять в файл npc_spawns_new.json командой /save all)
            slave.Hide();
            SendMessage(character, $"Slave @NPC_NAME({slave.TemplateId}), ObjId: {slave.ObjId}, TemplateId:{slave.TemplateId} removed successfuly");
        }
    }
}
