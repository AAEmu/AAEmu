using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseSoldPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly uint _unkId;
        private readonly uint _unk2Id;
        private readonly uint _newOwnerAcc;
        private readonly string _ownerName;
        private readonly string _houseName;

        public SCHouseSoldPacket(ushort tl, uint unkId, uint unk2Id, uint newOwnerAcc, string ownerName, string houseName) : base(SCOffsets.SCHouseSoldPacket, 5)
        {
            _tl = tl;
            _unkId = unkId;
            _unk2Id = unk2Id;
            _newOwnerAcc = newOwnerAcc;
            _ownerName = ownerName;
            _houseName = houseName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            stream.Write(_unkId);
            stream.Write(_unk2Id);
            stream.Write(_newOwnerAcc);
            stream.Write(_ownerName);
            stream.Write(_houseName);
            return stream;
        }
    }
}
