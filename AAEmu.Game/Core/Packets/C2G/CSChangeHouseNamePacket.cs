using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeHouseNamePacket : GamePacket
    {
        public CSChangeHouseNamePacket() : base(0x059, 1) //TODO 1.0 opcode: 0x057
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16(); // houseId
            var name = stream.ReadString();

            _log.Debug("ChangeHouseName, Tl: {0}, Name: {1}", tl, name);
            HousingManager.Instance.ChangeHouseName(Connection, tl, name);
        }
    }
}
