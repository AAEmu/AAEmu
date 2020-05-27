using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifyInGamePacket : GamePacket
    {
        public CSNotifyInGamePacket() : base(0x029, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            DbLoggerCategory.Database.Connection.ActiveChar.IsOnline = true;
            
            DbLoggerCategory.Database.Connection.ActiveChar.Spawn();
            DbLoggerCategory.Database.Connection.ActiveChar.StartRegen();

            // Joining channel 1 (shout) will automatically also join /lfg and /trade for that zone on the client-side
            // Back in 1.x /trade was zone base, not faction based
            ChatManager.Instance.GetZoneChat(DbLoggerCategory.Database.Connection.ActiveChar.Position.ZoneId).JoinChannel(DbLoggerCategory.Database.Connection.ActiveChar); // shout, trade, lfg
            ChatManager.Instance.GetNationChat(DbLoggerCategory.Database.Connection.ActiveChar.Race).JoinChannel(DbLoggerCategory.Database.Connection.ActiveChar); // nation
            DbLoggerCategory.Database.Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Judge, 0, DbLoggerCategory.Database.Connection.ActiveChar.Faction.MotherId)); //trial
            ChatManager.Instance.GetFactionChat(DbLoggerCategory.Database.Connection.ActiveChar.Faction.MotherId).JoinChannel(DbLoggerCategory.Database.Connection.ActiveChar); // faction

            // TODO - MAYBE MOVE TO SPAWN CHARACTER
            TeamManager.Instance.UpdateAtLogin(DbLoggerCategory.Database.Connection.ActiveChar);
            DbLoggerCategory.Database.Connection.ActiveChar.Expedition?.OnCharacterLogin(DbLoggerCategory.Database.Connection.ActiveChar);

            _log.Info("NotifyInGame");
        }
    }
}
