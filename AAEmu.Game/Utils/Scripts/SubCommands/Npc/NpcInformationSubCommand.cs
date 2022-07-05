using System.Drawing;
using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class NpcInformationSubCommand : SubCommandBase
    {
        public NpcInformationSubCommand()
        {
            Prefix = "[Npc Information]";
            Description = "Get all npc information";
            CallExample = "/npc info target||<ObjId>";
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
                    SendMessage(character, "You need to target a Npc first");
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

            var x = npc.Transform.Local.Position.X;
            var y = npc.Transform.Local.Position.Y;
            var z = npc.Transform.Local.Position.Z;
            var yaw = npc.Transform.Local.Rotation.Z.RadToDeg();
            var pitch = npc.Transform.Local.Rotation.Y.RadToDeg();
            var roll = npc.Transform.Local.Rotation.X.RadToDeg();

            //TODO: There is much more potential information to show on this command.
            SendMessage(character, $"Name:@NPC_NAME({npc.TemplateId}) ObjId:{npc.ObjId} TemplateId:{npc.TemplateId}, x:{x}, y:{y}, z:{z}, roll:{roll:0.#}°, pitch:{pitch:0.#}°, yaw:{yaw:0.#}°");
        }
    }
}
