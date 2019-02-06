using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeHouseNamePacket : GamePacket
    {
        public CSChangeHouseNamePacket() : base(0x057, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16(); // houseId
            var name = stream.ReadString();

            _log.Debug("ChangeHouseName, Tl: {0}, Name: {1}", tl, name);
        }
    }
}
