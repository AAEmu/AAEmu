using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionGetDeclarationMoneyPacket : GamePacket
    {
        public CSFactionGetDeclarationMoneyPacket() : base(CSOffsets.CSFactionGetDeclarationMoneyPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSFactionGetDeclarationMoneyPacket");
        }
    }
}
