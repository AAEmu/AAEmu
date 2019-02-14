using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCharDetailPacket : GamePacket
    {
        public CSCharDetailPacket() : base(0x106, 1) // TODO 1.0 opcode: 0x106
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            
            _log.Debug("CharDetail, Name: {0}", name);
        }
    }
}
