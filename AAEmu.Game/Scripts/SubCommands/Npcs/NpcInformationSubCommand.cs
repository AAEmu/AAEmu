using System.Drawing;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
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
            npc = ((Character)character).ParentWorld.GetNpc(npcObjId);
            if (npc is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Npc with objId {npcObjId} does not exist");
                return;
            }
        }
        else
        {
            var currentTarget = ((Character)character).CurrentTarget;
            if (currentTarget is null || currentTarget is not Npc)
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
        SendMessage(messageOutput, $"Name:@NPC_NAME({npc.TemplateId}) ObjId:{npc.ObjId} TemplateId:{npc.TemplateId}, modelRef:{npc.ModelId}, x:{x}, y:{y}, z:{z}, roll:{roll:0.#}°, pitch:{pitch:0.#}°, yaw:{yaw:0.#}°");
        var appearanceExt = npc.ModelParams.Write(new PacketStream()).GetBytes()[0];
        SendMessage(messageOutput, $"Appearance ext:{appearanceExt} ({(UnitCustomModelType)appearanceExt})");
        var cosplay = npc.Equipment.GetItemBySlot((int)EquipmentItemSlot.Cosplay);
        if (cosplay is not null)
            SendMessage(messageOutput, $"Cosplay slot 27: template {cosplay.TemplateId}, itemId {cosplay.Id}");
        else
            SendMessage(messageOutput, "Cosplay slot 27: empty");

        var occupied = new List<string>();
        for (var slot = 0; slot < EquipmentSerializer.SlotCount; slot++)
        {
            var item = npc.Equipment.GetItemBySlot(slot);
            if (item is not null)
                occupied.Add($"{slot}:{item.TemplateId}");
        }
        SendMessage(messageOutput, occupied.Count > 0
            ? $"Equipment: {string.Join(", ", occupied)}"
            : "Equipment: empty");

        var unitStateLen = new SCUnitStatePacket(npc).Write(new PacketStream()).GetBytes().Length;
        SendMessage(messageOutput, $"SCUnitState body: {unitStateLen} bytes");
    }
}
