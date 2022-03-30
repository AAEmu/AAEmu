using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSGameGuardPacket : GamePacket
    {
        public CSGameGuardPacket() : base(CSOffsets.CSGameGuardPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var a = stream.ReadByte();
            var b = stream.ReadUInt32();
            
            _log.Warn("CSGameGuardPacket");
            
            Connection.SendPacket(new SCGameGuardPacket(a, b));
        }
    }
}
