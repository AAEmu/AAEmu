using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells a character their zone permission ended while they are in the zone. Nothing sends it yet: the
/// expulsion conditions (and every value of <see cref="Option"/>) live in zone-permission condition
/// state this server does not hold — no 10.0.2.13 packet carries that condition to the client, so the
/// trigger cannot be wired without inventing one.
/// </summary>
/// <remarks>Wire: u8 option. The client's dialog branches option 1 = level, 2 = idle, 3 = quest,
/// anything else = faction.</remarks>
public class SCZonePermissionExpelledPacket(byte option) : GamePacket(SCOffsets.SCZonePermissionExpelledPacket, 1)
{
    public byte Option { get; } = option;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Option);
        return stream;
    }
}
