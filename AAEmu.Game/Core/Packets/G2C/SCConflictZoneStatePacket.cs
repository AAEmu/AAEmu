using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCConflictZoneStatePacket : GamePacket
    {
        private readonly ushort _zoneId;
        private readonly ZoneConflictType _hpws;
        private readonly DateTime _endTime;

        public SCConflictZoneStatePacket(ushort zoneId, ZoneConflictType hpws, DateTime endTime) : base(SCOffsets.SCConflictZoneStatePacket, 5)
        {
            _zoneId = zoneId;
            _hpws = hpws;
            _endTime = endTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_zoneId);
            stream.Write((byte)_hpws);
            stream.Write(_endTime);
            return stream;
        }
    }
}
