﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInteractNPCPacket : GamePacket
    {
        public CSInteractNPCPacket() : base(CSOffsets.CSInteractNPCPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var isTargetChanged = stream.ReadBoolean();

            _log.Debug("InteractNPC, BcId: {0}", objId);

            Connection.SendPacket(new SCAiAggroPacket(objId, 0)); // TODO проверить count=1
        }
    }
}
