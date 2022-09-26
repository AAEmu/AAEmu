﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCharacterConnectionRestrictPacket : GamePacket
    {
        public CSCharacterConnectionRestrictPacket() : base(CSOffsets.CSCharacterConnectionRestrictPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var characterId = stream.ReadUInt32();

            _log.Info("CSCharacterConnectionRestrictPacket");

            Connection.SendPacket(new SCCheckRaceCongestionResponsePacket());
        }
    }
}
