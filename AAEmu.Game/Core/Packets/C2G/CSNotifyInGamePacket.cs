using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifyInGamePacket : GamePacket
    {
        public CSNotifyInGamePacket() : base(0x029, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            Connection.ActiveChar.Spawn();
            Connection.ActiveChar.StartRegen();

            Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Region, 0, 148));
            Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Shout, 6, 0));
            Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Judge, 0, 148));
            Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Ally, 0, 148));
            FriendMananger.Instance.SendStatusChange(Connection.ActiveChar, true);

            _log.Info("NotifyInGame");
        }
    }
}
