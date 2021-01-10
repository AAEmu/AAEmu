using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamPingPosPacket : GamePacket
    {
        private readonly bool _hasPing;
        private readonly Point _position;
        private readonly uint _insId;

        public SCTeamPingPosPacket(bool hasPing, Point position, uint insId) : base(SCOffsets.SCTeamPingPosPacket, 5)
        {
            _hasPing = hasPing;
            _position = position;
            _insId = insId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_hasPing);
            stream.Write(_position.X);
            stream.Write(_position.Y);
            stream.Write(_position.Z);
            stream.Write(_insId);
            return stream;
        }
    }
}
