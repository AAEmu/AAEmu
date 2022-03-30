﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChallengeDuelPacket : GamePacket
    {
        public CSChallengeDuelPacket() : base(CSOffsets.CSChallengeDuelPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var challengedId = stream.ReadUInt32();
            Connection.ActiveChar.BroadcastPacket(new SCDuelChallengedPacket(challengedId), true);

            _log.Warn("ChallengeDuel, challengedId: {0}", challengedId);
        }
    }
}
