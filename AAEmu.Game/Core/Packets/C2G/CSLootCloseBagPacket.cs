using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLootCloseBagPacket : GamePacket
    {
        public CSLootCloseBagPacket() : base(0x090, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var iid = stream.ReadUInt64();
            
            _log.Warn("LootCloseBag, IId: {0}", iid);
        }
    }
}
