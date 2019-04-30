using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLootableStatePacket : GamePacket
    {
        private readonly uint _iId;
        private readonly bool _isLootable;
        
        public SCLootableStatePacket(uint unitId, bool isLootable) : base(SCOffsets.SCLootableStatePacket, 1)
        {
            _iId = unitId;
            _isLootable = isLootable;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(((ulong)_iId<<32)+65536);
            stream.Write(_isLootable);
            return stream;
        }


    }
}
