using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeHousePermissionPacket : GamePacket
    {
        public CSChangeHousePermissionPacket() : base(CSOffsets.CSChangeHousePermissionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var permission = stream.ReadByte();

            _log.Debug("ChangeHousePermission, Tl: {0}, Permission: {1}", tl, permission);
            HousingManager.Instance.ChangeHousePermission(Connection, tl, (HousingPermission)permission);
        }
    }
}
