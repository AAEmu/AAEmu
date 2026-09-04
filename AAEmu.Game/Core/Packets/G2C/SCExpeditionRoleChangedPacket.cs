using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces one member's role change (NOT a role-policy definition - see remarks).
/// </summary>
/// <remarks>
/// 2026-09-02: wire format re-derived from the REAL client (x2game.dll, not x2game-dev.dll - see
/// aaemu-guild-buff-housing-fixes-2026-09-02 for why that distinction matters) by tracing this packet's
/// actual Unpack (PacketFunctor&lt;...,SCExpeditionRoleChangedPacket&gt; -&gt; FUN_3933a7a0 -&gt;
/// FUN_395bf260). That consumer reads, in order: an 8-byte value compared against the client's own
/// character id (to pick "Your role changed" vs "Member $1's role changed"), a string used as the "$1"
/// member-name substitution, and a 4-byte value stored as the member's new role id - the role's DISPLAY
/// NAME is looked up client-side from the role-policy list already cached via
/// SCExpeditionRolePolicyListPacket, not sent here.
///
/// The previous version of this packet (2026-08-28) sent a full ExpeditionRolePolicy (id/role/name/10
/// permission bools) plus a trailing "success" bool - a genuinely different, wrong shape, based on a
/// misleading client-side construction-site match rather than this packet's real Unpack. That is very
/// likely why "leader can't assign a rank" showed correct server-side state (confirmed via logs/MySQL in
/// an earlier session) but never displayed correctly client-side - flagged at the time as an unresolved
/// dead end, not fixed until now.
/// </remarks>
public class SCExpeditionRoleChangedPacket(uint characterId, string name, byte role)
    : GamePacket(SCOffsets.SCExpeditionRoleChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)characterId);
        stream.Write(name);
        stream.Write((uint)role);
        return stream;
    }
}
