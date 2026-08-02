using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZUnitState (0x007) — full unit snapshot. Phase 3 uses a prebuilt raw body
/// (ownerType 0 → CharacterManager_CreateCharacter).
/// </summary>
public class WZUnitStatePacket(byte[] body) : ZonePacket(WzOpcodes.UnitState)
{
    protected override void WriteBody(PacketStream stream)
    {
        if (body is { Length: > 0 })
            stream.Write(body, false);
    }
}
