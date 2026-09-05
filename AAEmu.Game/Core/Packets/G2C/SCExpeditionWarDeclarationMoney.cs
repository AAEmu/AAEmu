using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Server's answer to CSRequestDeclarationMoneyPacket. Opens the "declare war for &lt;money&gt;?"
/// confirm dialog client-side; the guild name is resolved client-side from unitId, not sent here.
/// </summary>
public class SCExpeditionWarDeclarationMoney(uint bc, uint money) : GamePacket(SCOffsets.SCExpeditionWarDeclarationMoney, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(money);
        return stream;
    }
}
