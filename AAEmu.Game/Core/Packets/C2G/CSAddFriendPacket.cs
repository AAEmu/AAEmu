using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAddFriendPacket : GamePacket
    {
        public CSAddFriendPacket() : base(0x101, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();

            var character = WorldManager.Instance.GetCharacter(name);
            if (character == null)
                return;

            Connection.SendPacket(new SCAddFriendPacket(character, true, 0));

            _log.Debug("AddFriend, name: {0}", name);
        }
    }
}