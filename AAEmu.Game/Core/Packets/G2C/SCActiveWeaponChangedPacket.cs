using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCActiveWeaponChangedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly byte _activeWeapon;

        public SCActiveWeaponChangedPacket(uint objId, byte activeWeapon) : base(SCOffsets.SCActiveWeaponChangedPacket, 5)
        {
            _objId = objId;
            _activeWeapon = activeWeapon;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_activeWeapon);
            return stream;
        }
    }
}
