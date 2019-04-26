using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLootableStatePacket : GamePacket
    {
        private readonly ulong _iId;
        private readonly bool _isLootable;
        
        public SCLootableStatePacket(ulong itemId, bool isLootable) : base(SCOffsets.SCLootableStatePacket, 1)
        {
            _iId = itemId;
            _isLootable = isLootable;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_iId);
            stream.Write(_isLootable);
            return stream;
        }


    }
}
