using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Npcs;

public class NpcRemoveSubCommand : SubCommandBase
{
    public NpcRemoveSubCommand()
    {
        Title = "[Npc Remove]";
        Description = "Remove a targeted npc or using an npc <ObjId>";
        CallPrefix = $"{CommandManager.CommandPrefix}remove";
        AddParameter(new StringSubCommandParameter("target", "target", true, "target", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        Npc npc;
        if (parameters.TryGetValue("ObjId", out var npcObjId))
        {
            npc = ((Character)character).ParentWorld.GetNpc(npcObjId);
            if (npc is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Npc with objId {npcObjId} does not exist");
                Logger.Warn($"Npc with objId {npcObjId} does not exist");
                return;
            }
        }
        else
        {
            var currentTarget = ((Character)character).CurrentTarget;
            if (currentTarget is null || currentTarget is not Npc)
            {
                SendColorMessage(messageOutput, Color.Red, "You need to target a Npc first");
                Logger.Warn("You need to target a Npc first");
                return;
            }

            npc = (Npc)currentTarget;
        }

        // Remove Npc
        //npc.Spawner.Despawn(npc);
        if (npc.Spawner != null)
            npc.Spawner.Id = 0xffffffff; // Do not persist a manually removed spawn. GM-spawned NPCs may have no spawner.
        npc.Hide();
        SendMessage(messageOutput, $"Npc @NPC_NAME({npc.TemplateId}), ObjId: {npc.ObjId}, TemplateId:{npc.TemplateId} removed successfully");
        Logger.Warn($"Npc @NPC_NAME({npc.TemplateId}), ObjId: {npc.ObjId}, TemplateId:{npc.TemplateId} removed successfully");
    }
}
