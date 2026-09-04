using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionInvitationPacket(uint invitorId, string invitorName, uint factionId, string factionName)
    : GamePacket(SCOffsets.SCExpeditionInvitationPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 2026-08-27, third pass, now unambiguously confirmed: the native client (x2game-dev.dll,
        // FUN_39c59630) reads this field through interface slot 0x98. Two earlier guesses at this slot were
        // both wrong (WriteBc 3-byte, then WriteBc of ObjId instead of the persistent id) - the first assumed
        // slot 0x98 was the same "Bc" id-encoder used elsewhere in this codebase, without checking. It isn't:
        // that real Bc encoder is a DIFFERENT slot (0x1a0/0x1a8, confirmed via SCUnitExpeditionChangedPacket's
        // FUN_393908b0 sub-call), independently matching this project's own already-established WriteBc
        // short/long-form finding. Slot 0x98 itself is confirmed a plain 8-byte scalar by direct, unambiguous
        // struct-offset evidence: in FUN_393775e0, a field literally named "playerId" sits at slot 0x98,
        // offset+0x10, with the very next field starting at offset+0x18 - an exact 8-byte gap (the same 8-byte
        // gap independently shows up for "preciseHealth"/"preciseMana" elsewhere). "playerId" also matches
        // the persistent Character.Id semantically far better than ObjId. So: plain 8-byte value, persistent
        // id - both this codebase's original width (wrong, was 4 bytes) and attempt #2's value (wrong, was
        // ObjId) needed correcting, not just one or the other.
        stream.Write((ulong)invitorId);
        stream.Write(invitorName);
        stream.Write(factionId);
        stream.Write(factionName);
        return stream;
    }
}
