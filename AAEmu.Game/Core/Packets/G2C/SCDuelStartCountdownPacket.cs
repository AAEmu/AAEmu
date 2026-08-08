using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the client to run its duel countdown. The packet genuinely has no body - its read function
/// (RVA 0x5E5690) is a bare `ret`.
/// </summary>
/// <remarks>
/// The duration is the client's, not ours: the handler at RVA 0x105E20 stamps the current time and
/// writes the constant 0xBB8 - 3000 ms - so the countdown always runs for exactly three seconds. That
/// is why this has to be sent when the duel is accepted and SCDuelStarted three seconds later. We used
/// to send both from DuelStart in the same breath, and a countdown that ends the instant it begins is
/// a countdown nobody sees.
/// </remarks>
public class SCDuelStartCountdownPacket() : GamePacket(SCOffsets.SCDuelStartCountdownPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // no body

        return stream;
    }
}
