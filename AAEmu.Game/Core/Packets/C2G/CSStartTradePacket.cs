using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartTradePacket : GamePacket
    {
        public CSStartTradePacket() : base(0x0ec, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            
            _log.Warn("StartTrade, ObjId: {0}", objId);
        }
    }
}
