﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetLpManageCharacterPacket : GamePacket
    {
        public CSSetLpManageCharacterPacket() : base(CSOffsets.CSSetLpManageCharacterPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSSetLpManageCharacterPacket");
            
            var characterId = stream.ReadUInt32();
            Connection.SendPacket(new SCCharacterLpManagedPacket(characterId));
        }
    }
}
