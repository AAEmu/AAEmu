using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Resident-map feed: the client decides townhall residency by looking up the zone group in a map
/// that only server packets fill.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: the zone group as u16, then one option byte. The client's
/// handler reads that byte as 1 = add the group to its resident map and 2 = remove it, so the
/// default here is <see cref="Add"/>: this feed only ever announces groups the character is a
/// resident of. Opcode 0x38 (family chain 0x38 map -> 0x39 info -> 0x3A balance -> 0x3B info list
/// -> 0x3C member list).
/// </remarks>
public class SCResidentMapPacket(short zoneGroup, byte option = SCResidentMapPacket.Add)
    : GamePacket(SCOffsets.SCResidentMapPacket, 1)
{
    /// <summary>Make the group a residence of the client's resident map.</summary>
    public const byte Add = 1;

    /// <summary>Drop the group from the client's resident map.</summary>
    public const byte Remove = 2;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneGroup);
        stream.Write(option);
        return stream;
    }
}
