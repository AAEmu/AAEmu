using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
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
            Connection.ActiveChar.IsOnline = true;
            
            Connection.ActiveChar.Spawn();
            Connection.ActiveChar.StartRegen();

            // Joining channel 1 (shout) will automatically also join /lfg and /trade for that zone on the client-side
            // Back in 1.x /trade was zone base, not faction based
            var zone = ZoneManager.Instance.GetZoneByKey(Connection.ActiveChar.Position.ZoneId);
            if (zone != null)
                Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Shout, (short)zone.GroupId, 0)); //shout

            Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Region, 0, Connection.ActiveChar.Faction.MotherId)); //nation
            Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Judge, 0, Connection.ActiveChar.Faction.MotherId)); //trial
            Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Ally, 0, Connection.ActiveChar.Faction.MotherId)); //faction

            // TODO - MAYBE MOVE TO SPAWN CHARACTER
            TeamManager.Instance.UpdateAtLogin(Connection.ActiveChar);
            Connection.ActiveChar.Expedition?.OnCharacterLogin(Connection.ActiveChar);

            _log.Info("NotifyInGame");
        }
    }
}
