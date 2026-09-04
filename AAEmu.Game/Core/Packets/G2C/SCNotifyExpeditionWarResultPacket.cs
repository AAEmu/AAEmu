using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Guild War end result. Opcode 0x1f. Fires the client's EXPEDITION_WAR_STATE event with a
/// (state, declarer, defendant, winner) tuple -&gt; the win/lose/draw banner + result icons
/// (expedition_war.lua). Without it the client shows every war as a draw.
/// </summary>
/// <remarks>
/// Wire from client Unpack FUN_39a9a6d0: u32 id, u32 id2, u8 result (result is an optional field).
/// Handler FUN_3933abe0 -&gt; FUN_395b72d0(mgr, id, id2, result): result == 1 makes 'id' the winner,
/// otherwise 'id2'. result 0 = draw.
/// </remarks>
public class SCNotifyExpeditionWarResultPacket(uint id, uint id2, byte result) : GamePacket(SCOffsets.SCNotifyExpeditionWarResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(id2);
        stream.Write(result);
        return stream;
    }
}
