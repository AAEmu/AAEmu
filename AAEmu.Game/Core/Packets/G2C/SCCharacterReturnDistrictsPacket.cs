using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterReturnDistrictsPacket : GamePacket
    {
        private readonly Portal[] _portals;
        private readonly uint _returnDistrictId;
        
        public SCCharacterReturnDistrictsPacket(Portal[] portals, uint returnDistrictId) : base(SCOffsets.SCCharacterReturnDistrictsPacket, 1)
        {
            _portals = portals;
            _returnDistrictId = returnDistrictId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_portals.Length);
            foreach (var portal in _portals)
                stream.Write(portal);
            stream.Write(_returnDistrictId);
            return stream;
        }
    }
}
