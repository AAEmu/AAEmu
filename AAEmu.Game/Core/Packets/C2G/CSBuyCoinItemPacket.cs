using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuyCoinItemPacket : GamePacket
    {
        public CSBuyCoinItemPacket() : base(0x0af, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var id = stream.ReadUInt32();

            _log.Debug("BuyCoinItem, objId: {0}, id: {1}", objId, id);
        }
    }
}
