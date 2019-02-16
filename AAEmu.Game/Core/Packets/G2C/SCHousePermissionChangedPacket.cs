using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHousePermissionChangedPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly byte _permission;
        
        public SCHousePermissionChangedPacket(ushort tl, byte permission) : base(0x0bd, 1) //TODO 1.0 opcode: 0x0b6
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
