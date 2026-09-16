using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks how many entries the in-game event board has.
/// </summary>
/// <remarks>
/// The board lists the events this server runs on a schedule of its own, and it runs none — tower defence,
/// sieges and schedule items are separate systems with their own windows — so the answer is an empty board
/// rather than silence, which is what leaves the window waiting. <c>count 0</c> with <c>loadedTime 0</c> is
/// what an empty board is; the packet takes both as parameters, so adding an event changes only the caller.
/// The request has no body.
/// </remarks>
public class CSRequestEventInfoCountPacket() : GamePacket(CSOffsets.CSRequestEventInfoCountPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        Connection.ActiveChar?.SendPacket(new SCEventInfoCountPacket(0, 0));
    }
}
