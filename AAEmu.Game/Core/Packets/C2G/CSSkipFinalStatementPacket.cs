﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSkipFinalStatementPacket : GamePacket
    {
        public CSSkipFinalStatementPacket() : base(CSOffsets.CSSkipFinalStatementPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var trial = stream.ReadUInt32();

            _log.Warn("SkipFinalStatement, Trial: {0}", trial);
        }
    }
}
