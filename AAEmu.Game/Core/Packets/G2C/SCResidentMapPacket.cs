using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Resident-map feed: the client decides townhall residency by looking up the zone group in a map
/// that only server packets fill.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: the zone group as i16 <c>type</c>, then one i8 <c>option</c>.
/// The option is not decorative: the client reads it as a dword, decrements it, and takes the add
/// path for 1 and the remove path for 2 — anything else returns without touching the resident map.
/// The default here is <see cref="Add"/>, because this feed only ever announces groups the character
/// is a resident of. The family chain is 0x38 map -> 0x39 info -> 0x3A balance -> 0x3B info list ->
/// 0x3C member list, each opcode stamped by the client's constructor for that type.
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
