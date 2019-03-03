﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCShipyardStatePacket : GamePacket
    {
        private readonly ShipyardData _shipyardData;
        private readonly int _step;

        public SCShipyardStatePacket(ShipyardData shipyardData) : base(SCOffsets.SCShipyardStatePacket, 1)
        {
            _shipyardData = shipyardData;
            _step = shipyardData.Step;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_shipyardData);
            stream.Write(_step);
            return stream;
        }
    }
}
