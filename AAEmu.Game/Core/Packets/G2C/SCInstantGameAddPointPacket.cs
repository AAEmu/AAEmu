using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInstantGameAddPointPacket : GamePacket
    {
        private ZoneInstanceId _zoneInstanceId;
        private InstantCorps _corps;
        private int _points;
        private int _score1;
        private int _score2;
        private string _charName;

        public SCInstantGameAddPointPacket(ZoneInstanceId zoneInstanceId, InstantCorps corps, int points, int score1, int score2, string charName)
            : base(SCOffsets.SCInstantGameAddPointPacket, 1)
        {
            _zoneInstanceId = zoneInstanceId;
            _corps = corps;
            _points = points;
            _score1 = score1;
            _score2 = score2;
            _charName = charName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_zoneInstanceId);
            stream.Write((byte)_corps);
            stream.Write(_points);
            stream.Write(_score1);
            stream.Write(_score2);
            stream.Write(_charName);

            return stream;
        }
    }
}
