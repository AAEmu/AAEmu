using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSHeroRequestRankDataPacket : GamePacket
    {
        public CSHeroRequestRankDataPacket() : base(CSOffsets.CSHeroRequestRankDataPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt32();
            var division = stream.ReadByte();
            _log.Debug($"CSHeroRequestRankDataPacket: type = {type}, division = {division}");
        }
    }
}
