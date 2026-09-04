using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// First step of the guild "declare war" round trip. The client sends this before
/// CSDeclareExpeditionWarPacket; the server must answer with SCExpeditionWarDeclarationMoney
/// (target unitId + computed cost) to open the confirm dialog. Body is just the target unit -
/// the server computes and returns the cost.
/// </summary>
public class CSRequestDeclarationMoneyPacket() : GamePacket(CSOffsets.CSRequestDeclarationMoneyPacket, 1)
{
    public uint Bc { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();

        ExpeditionManager.Instance.RequestDeclarationMoney(Connection, Bc);
    }
}
