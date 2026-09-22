using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Definitive result of a settled zone-permission request: the character's zone-permission state has
/// changed (granted, or the wait closed) and the client re-reads its condition. The client's
/// <c>UPDATE_ZONE_PERMISSION</c> handlers hang off this refresh — it is the only zone-permission
/// result the 10.0.2.13 catalog carries with no field of its own.
/// </summary>
/// <remarks>Wire: empty body — the client's serializer lists no fields for 0x086.</remarks>
public class SCZonePermissionChangedPacket() : GamePacket(SCOffsets.SCZonePermissionChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream) => stream;
}
