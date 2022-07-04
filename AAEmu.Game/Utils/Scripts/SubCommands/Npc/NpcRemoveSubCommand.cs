using System;
using System.Drawing;
using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class NpcRemoveSubCommand : SubCommandBase
    {
        public NpcRemoveSubCommand()
        {
            Prefix = "[Npc Remove]";
            Description = "Remove a targeted npc or using a <ObjId>";
            CallExample = "/npc remove target || /npc remove <ObjId>";
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            Npc npc;
            var firstArgument = args.FirstOrDefault();
            if (firstArgument is null)
            {
                SendMessage(character, CallExample);
                return;
            }
            if (firstArgument == "target")
            {
                var currentTarget = ((Character)character).CurrentTarget;
                if (currentTarget is null || !(currentTarget is Npc))
                {
                    SendColorMessage(character, Color.Red, "You need to target a Npc");
                    return;
                }
                npc = (Npc)currentTarget;
            }
            else if (!uint.TryParse(firstArgument, out var npcObjId))
            {
                SendMessage(character, "Invalid <ObjId> for Npc, please use a number");
                return;
            }
            else
            {
                npc = WorldManager.Instance.GetNpc(npcObjId);
                if (npc is null)
                {
                    SendColorMessage(character, Color.Red, "Npc with objId {0} does not exist |r", npcObjId);
                    return;
                }
            }

            // Remove Npc
            try
            {
                //npc.Spawner.Despawn(npc);
                npc.Spawner.Id = 0xffffffff; // removed from the game manually (укажем, что не надо сохранять в файл npc_spawns_new.json командой /save all)
                npc.Hide();
                SendMessage(character, $"Npc @NPC_NAME({npc.TemplateId}), ObjId: {npc.ObjId}, TemplateId:{npc.TemplateId} removed successfuly");
            }
            catch (Exception e)
            {
                SendColorMessage(character, Color.Red, e.Message);
                _log.Error(e.Message);
                _log.Error(e.StackTrace);
            }
        }
    }
}
