using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBlockedUsersPacket : GamePacket
    {
        private readonly int _total;
        private readonly (uint characterId, string characterName)[] _blocked;

        public SCBlockedUsersPacket(int total, (uint characterId, string characterName)[] blocked) : base(0x04f, 1)
        {
            _total = total;
            _blocked = blocked;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_total);
            stream.Write(_blocked.Length); // TODO max length 500
            foreach (var (charId, charName) in _blocked)
            {
                stream.Write(charId);
                stream.Write(charName);
            }

            return stream;
        }
    }
}
