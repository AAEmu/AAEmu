using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZDropBackpack (0x07F) — announce a World-authored ground pack to Zone.
/// Minimal live body used by PutDownBackpackEffect: uid + zoneId + type + instanceId + removeItem
/// </summary>
public class WZDropBackpackPacket(
    Item item,
    uint zoneId,
    ulong doodadTemplateId,
    uint instanceId,
    bool removeItem,
    bool hackAttempt,
    bool userDrop,
    float x,
    float y,
    float z)
    : ZonePacket(WzOpcodes.DropBackpack)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(item.Id);
        stream.Write(zoneId);
        item.Write(stream);
        stream.Write(doodadTemplateId);
        stream.Write((ulong)Helpers.ConvertLongX(x));
        stream.Write((ulong)Helpers.ConvertLongY(y));
        stream.Write(z);
        stream.Write(instanceId);
        stream.Write(removeItem);
        stream.Write(hackAttempt);
        stream.Write(userDrop);
    }
}
