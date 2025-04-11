using System.Collections.Generic;
using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Npcs;

public class NpcInformationSubCommand : SubCommandBase
{
    public NpcInformationSubCommand()
    {
        Title = "[Npc Information]";
        Description = "Get all npc information from a NPC (Targeted or by Id)";
        CallPrefix = $"{CommandManager.CommandPrefix}npc info";
        AddParameter(new StringSubCommandParameter("target", "target", true, "target", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        Npc npc;
        if (parameters.TryGetValue("ObjId", out var npcObjId))
        {
            npc = WorldManager.Instance.GetNpc(npcObjId);
            if (npc is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Npc with objId {npcObjId} does not exist");
                return;
            }
        }
        else
        {
            var currentTarget = ((Character)character).CurrentTarget;
            if (currentTarget is null || !(currentTarget is Npc))
            {
                SendColorMessage(messageOutput, Color.Red, "You need to target a Npc first");
                return;
            }

            npc = (Npc)currentTarget;
        }

        var x = npc.Transform.Local.Position.X;
        var y = npc.Transform.Local.Position.Y;
        var z = npc.Transform.Local.Position.Z;
        var yaw = npc.Transform.Local.Rotation.Z.RadToDeg();
        var pitch = npc.Transform.Local.Rotation.Y.RadToDeg();
        var roll = npc.Transform.Local.Rotation.X.RadToDeg();

        //TODO: There is much more potential information to show on this command.
        SendMessage(messageOutput, $"Name:@NPC_NAME({npc.TemplateId}) ObjId:{npc.ObjId} TemplateId:{npc.TemplateId}, x:{x}, y:{y}, z:{z}, roll:{roll:0.#}°, pitch:{pitch:0.#}°, yaw:{yaw:0.#}°");
    }
}
