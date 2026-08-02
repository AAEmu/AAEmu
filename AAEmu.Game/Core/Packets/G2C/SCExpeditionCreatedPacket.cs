using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCExpeditionCreatedPacket(int @type, int @type2, string name, ulong ownerId, string ownerName, sbyte unnamed1, long createdTime, bool aggroLink, bool dTarget, sbyte allowChangeName, long renameTime, bool integrationFaction, uint bc, sbyte count, uint bc2, ulong @type3, string name2) : GamePacket(SCOffsets.SCExpeditionCreatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(name);
        stream.Write(ownerId);
        stream.Write(ownerName);
        stream.Write(unnamed1);
        stream.Write(createdTime);
        stream.Write(aggroLink);
        stream.Write(dTarget);
        stream.Write(allowChangeName);
        stream.Write(renameTime);
        stream.Write(integrationFaction);
        stream.WriteBc(bc);
        stream.Write(count);
        stream.WriteBc(bc2);
        stream.Write(@type3);
        stream.Write(name2);
        return stream;
    }
}
