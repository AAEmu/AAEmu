using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Match invitation (battlefield or Indun PERFECT) — the dialog offering entry once a match is
/// ready. The client raises it from <c>InstantGame::SetAskJoin</c>, which only receives scalars,
/// so the nested buffer between the matching key and the invitation info stays empty.
///
/// Wire: u32 invitationTime, u64 zi, u32 type, u64 matchingKey, nested blob, u32 accept,
/// u32 maxEntry. The blob sits before accept/maxEntry, and omitting its header makes the client
/// read the accept count as a buffer length.
///
/// <c>type</c> names a battle field. The client resolves it and uses the result without checking
/// it, so a dungeon match must pass <see cref="Models.Game.InstantGame.InstantGameWireContract.NoBattleFieldType"/>
/// rather than its catalog id — a dungeon is identified by the zone group the client already holds.
///
/// The dialog only appears while the client considers itself queued, which it learns from
/// SCAppliedToInstantGame.
/// </summary>
public class SCInviteToInstantGamePacket(
    uint invitationTime,
    ZoneInstanceId zoneInstanceId,
    uint type,
    ulong matchingKey,
    uint accept,
    uint maxEntry)
    : GamePacket(SCOffsets.SCInviteToInstantGamePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(invitationTime);
        stream.Write(zoneInstanceId);
        stream.Write(type);
        stream.Write(matchingKey);
        NestedBlobWire.WriteEmpty(stream);
        stream.Write(accept);
        stream.Write(maxEntry);
        return stream;
    }
}
