using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSchoolOfFishDoodadsPacket : GamePacket
    {
        private readonly bool _last;
        private readonly Doodad[] _transfers;

        public SCSchoolOfFishDoodadsPacket(bool last, Doodad[] transfers) : base(SCOffsets.SCSchoolOfFishDoodadsPacket, 1)
        {
            _last = last;
            _transfers = transfers;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_last);
            stream.Write((byte)_transfers.Length); // не более 10
            foreach (var transfer in _transfers)
            {
                transfer.WriteFishFinderUnit(stream);
            }

            return stream;
        }
    }
}
