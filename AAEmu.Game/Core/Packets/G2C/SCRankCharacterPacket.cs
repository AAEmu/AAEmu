using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRankCharacterPacket : GamePacket
    {
        private readonly uint _type;
        private readonly short _ErrorMessage;
        private readonly DateTime _ackTime;
        private readonly int _countRun;
        private readonly uint _accountId;
        private readonly string _account;
        private readonly int _type1;
        private readonly uint _type2;
        private readonly byte _wid;
        private readonly int _v1;
        private readonly bool _isV1Sum;
        private readonly int _v2;
        private readonly bool _isV2Sum;
        private readonly DateTime _recorded;

        public SCRankCharacterPacket() : base(SCOffsets.SCRankCharacterPacket, 1)
        {
            _type = 0;
            _ErrorMessage = 0;
            _ackTime = DateTime.MinValue;
            _countRun = 0;
            _accountId = 0;
            _account = ""; 
            _type1 = 0;
            _type2 = 0;
            _wid = 0;
            _v1 = 0;
            _v2 = 0;
            _isV1Sum = false;
            _isV2Sum = false;
            _recorded = DateTime.MinValue;
        }

        public override PacketStream Write(PacketStream stream)
        {
            // "Fixes" infinite loading, sends less causes packet error
            stream.Write(_type);         // type
            stream.Write(_ErrorMessage); // ErrorMessage
            stream.Write(_ackTime);      // ackTime
            stream.Write(_countRun);     // countRun
            for (int i = 0; i < _countRun; i++)
            {
                stream.Write(_accountId); // accountId
                stream.Write(_account); // account
                stream.Write(_type1); // type
                stream.Write(_type2); // type
                stream.Write(_wid); // wid
                stream.Write(_v1); // v1
                stream.Write(_isV1Sum); // isV1Sum
                stream.Write(_v2); // v2
                stream.Write(_isV2Sum); // isV2Sum
                stream.Write(_recorded); // recorded
            }

            return stream;
        }
    }
}
