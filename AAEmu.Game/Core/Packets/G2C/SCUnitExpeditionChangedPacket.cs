using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitExpeditionChangedPacket(
    uint unitId,
    uint characterId,
    string kicker,
    string unitName,
    uint id,
    uint expeditionId,
    bool expel)
    : GamePacket(SCOffsets.SCUnitExpeditionChangedPacket, 1)
{
    // TODO nation? faction?

    public override PacketStream Write(PacketStream stream)
    {
        // 2026-08-27 fix: found while chasing the SCExpeditionInvitationPacket bug (see that class's doc
        // comment for the full RE trail) - characterId shares interface slot 0x98 with that packet's
        // "invitor"/"playerId"-style field, confirmed (via FUN_393775e0's "playerId" at offset+0x10 followed
        // by the next field at +0x18, an exact 8-byte gap) to be a plain 8-byte value, not the 4 bytes this
        // was previously written as. This packet was never actually wire-verified despite underpinning the
        // existing guild-nameplate fix in Character.cs's AddVisibleObject - likely the real reason that fix
        // never worked live.
        stream.WriteBc(unitId);
        stream.Write((ulong)characterId);
        stream.Write(kicker);
        stream.Write(unitName);
        stream.Write(id);
        stream.Write(expeditionId);
        stream.Write(expel);
        return stream;
    }
}
