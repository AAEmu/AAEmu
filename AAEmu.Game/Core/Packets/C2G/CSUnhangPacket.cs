#nullable enable

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUnhangPacket() : GamePacket(CSOffsets.CSUnhangPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var unitObjId = stream.ReadBc();
        // var targetObjId = stream.ReadBc(); // Not used in 1.2
        var targetObjId = 0u;
        var reason = stream.ReadUInt32();
        // 0 climbed off from bottom
        // 2 climbed off on top
        // 7 jumped off

        Logger.Trace($"Unhang, unitObjId: {unitObjId}, targetObjId: {targetObjId}, Reason: {reason}");
        // For 1.2 the targetObjId is not sent, so we will need to grab our saved value from Transform
        // Later this can also be used to verify if it's the correct object
        Slave? stickySlave = null;
        var character = Connection.ActiveChar.ParentWorld.GetBaseUnit(unitObjId);
        if (character != null)
        {
            stickySlave = character.Transform.StickyParent?.GameObject as Slave;
            targetObjId = character.Transform.StickyParent?.GameObject?.ObjId ?? 0;
            character.Transform.StickyParent = null;
        }

        Connection.ActiveChar.BroadcastPacket(new SCUnhungPacket(unitObjId, targetObjId, reason), false);

        if (stickySlave != null)
            ShipHarpoonRopeController.BreakRopeForClients(stickySlave, cutouted: false);
    }
}
