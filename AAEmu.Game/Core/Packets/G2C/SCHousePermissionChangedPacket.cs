using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHousePermissionChangedPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly byte _permission;
        
        public SCHousePermissionChangedPacket(ushort tl, byte permission) : base(SCOffsets.SCHousePermissionChangedPacket, 5)
        {
            _tl = tl;
            _permission = permission;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            stream.Write(_permission);
            return stream;
        }
    }
}
