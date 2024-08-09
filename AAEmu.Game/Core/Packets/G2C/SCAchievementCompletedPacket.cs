using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAchievementCompletedPacket : GamePacket
    {
        private readonly uint _id;

        public SCAchievementCompletedPacket(uint id) : base(SCOffsets.SCAchievementCompletedPacket, 1)
        {
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);             // type
            stream.Write(DateTime.UtcNow); // complete

            return stream;
        }
    }
}
