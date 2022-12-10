﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCItemUccDataChangedPacket : GamePacket
    {
        private readonly ulong _uccId;
        private readonly uint _playerId;
        private readonly ulong _itemId;

        public SCItemUccDataChangedPacket(ulong uccId, uint playerId, ulong targetItemId) : base(SCOffsets.SCItemUccDataChangedPacket, 1)
        {
            _uccId = uccId;
            _playerId = playerId;
            _itemId = targetItemId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_uccId);
            stream.Write(_playerId);
            stream.Write(_itemId);

            return stream;
        }
    }
}
