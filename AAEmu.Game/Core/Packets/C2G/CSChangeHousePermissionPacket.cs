using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeHousePermissionPacket : GamePacket
    {
        public CSChangeHousePermissionPacket() : base(CSOffsets.CSChangeHousePermissionPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var houseId = stream.ReadUInt16();  // tl
            var permission = stream.ReadByte();

            _log.Debug("ChangeHousePermission, houseId: {0}, Permission: {1}", houseId, permission);
            HousingManager.Instance.ChangeHousePermission(Connection, houseId, (HousingPermission)permission);
        }
    }
}
