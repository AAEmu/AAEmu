using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBoardingTransferPacket : GamePacket
    {
        public CSBoardingTransferPacket() : base(CSOffsets.CSBoardingTransferPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var ap = stream.ReadByte();

            _log.Warn("BoardingTransfer, Tl: {0}, Ap: {1}", tl, ap);
        }
    }
}
