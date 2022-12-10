using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBeautyShopDataPacket : GamePacket
    {
        public CSBeautyShopDataPacket() : base(CSOffsets.CSBeautyShopDataPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            //_log.Debug("BeautyshopData");
            var hair = stream.ReadUInt32(); // unknown value ? maybe bitmask of what was changed ?
            var model = new UnitCustomModelParams();
            model.Read(stream);
            CharacterManager.Instance.ApplyBeautySalon(Connection.ActiveChar, hair, model);
        }

    }
}
