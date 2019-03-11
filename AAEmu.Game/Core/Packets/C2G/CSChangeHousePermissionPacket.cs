using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeHousePermissionPacket : GamePacket
    {
        public CSChangeHousePermissionPacket() : base(0x05a, 1) //TODO 1.0 opcode: 0x058
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var permission = stream.ReadByte();

            _log.Debug("ChangeHousePermission, Tl: {0}, Permission: {1}", tl, permission);
        }
    }
}
