using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBlockedUsersPacket : GamePacket
    {
        private readonly int _total;
        private readonly Blocked[] _blocked;

        public SCBlockedUsersPacket(int total, Blocked[] blocked) : base(SCOffsets.SCBlockedUsersPacket, 1)
        {
            _total = total;
            _blocked = blocked;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_total);
            stream.Write(_blocked.Length); // TODO max length 500
            foreach (var blocked in _blocked)
                stream.Write(blocked);
            return stream;
        }
    }
}
