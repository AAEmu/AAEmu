using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInstantGameEndPacket : GamePacket
    {
        private ZoneInstanceId _zoneInstanceId;
        private BattlefieldEndingReason _battlefieldEndingReason;
        private InstantGameTeamResult _corps1Result;
        private InstantGameTeamResult _corps2Result;

        public SCInstantGameEndPacket(ZoneInstanceId zoneInstanceId, BattlefieldEndingReason battlefieldEndingReason, InstantGameTeamResult corps1Result, InstantGameTeamResult corps2Result)
            : base(SCOffsets.SCInstantGameEndPacket, 1)
        {
            _zoneInstanceId = zoneInstanceId;
            _battlefieldEndingReason = battlefieldEndingReason;
            _corps1Result = corps1Result;
            _corps2Result = corps2Result;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_zoneInstanceId);
            stream.Write((byte)_battlefieldEndingReason);
            stream.Write(_corps1Result);
            stream.Write(_corps2Result);

            return stream;
        }
    }
}
