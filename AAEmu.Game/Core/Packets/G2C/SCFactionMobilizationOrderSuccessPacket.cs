using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the hero who issued a mobilization order that it went out.
/// </summary>
/// <remarks>
/// Empty by design, not by omission: the handler at .text 0x347330 never touches the packet body. It
/// looks up a UI string and raises one event, which is the "orders issued" confirmation - so everything
/// the message says is client-side text and the packet is purely the trigger.
/// </remarks>
public class SCFactionMobilizationOrderSuccessPacket()
    : GamePacket(SCOffsets.SCFactionMobilizationOrderSuccessPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
