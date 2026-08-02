using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZDropBackpack (0x07F) — announce a World-authored ground pack to Zone.
/// Fields from dedicate struct: uid (item), zoneId, type (doodad tpl), instanceId, removeItem,
/// Minimal live body used by PutDownBackpackEffect: uid + zoneId + type + instanceId + removeItem
/// + world pos. Extra trailing fields zeroed if dedicate expects more.
/// </summary>
public class WZDropBackpackPacket(
    ulong itemUid,
    uint zoneId,
    ulong doodadTemplateId,
    uint instanceId,
    bool removeItem,
    float x,
    float y,
    float z)
    : ZonePacket(WzOpcodes.DropBackpack)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(itemUid);
        stream.Write(zoneId);
        stream.Write(doodadTemplateId);
        stream.Write(instanceId);
        stream.Write(removeItem);
        stream.Write((ulong)Helpers.ConvertLongX(x));
        stream.Write((ulong)Helpers.ConvertLongY(y));
        stream.Write(z);
    }
}
