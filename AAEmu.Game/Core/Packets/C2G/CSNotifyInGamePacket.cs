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
        public CSNotifyInGamePacket() : base(CSOffsets.CSNotifyInGamePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
        }

        public override void Execute()
        {
            Connection.ActiveChar.IsOnline = true;
            
            Connection.ActiveChar.Spawn();
            Connection.ActiveChar.StartRegen();

            // Joining channel 1 (shout) will automatically also join /lfg and /trade for that zone on the client-side
            // Back in 1.x /trade was zone base, not faction based
            ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Position.ZoneId).JoinChannel(Connection.ActiveChar); // shout, trade, lfg
            ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).JoinChannel(Connection.ActiveChar); // nation
            Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Judge, 0, Connection.ActiveChar.Faction.MotherId)); //trial
            ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).JoinChannel(Connection.ActiveChar); // faction

            // TODO - MAYBE MOVE TO SPAWN CHARACTER
            TeamManager.Instance.UpdateAtLogin(Connection.ActiveChar);
            Connection.ActiveChar.Expedition?.OnCharacterLogin(Connection.ActiveChar);
            
            Connection.ActiveChar.UpdateGearBonuses(null, null);

            _log.Info("NotifyInGame");
        }
    }
}
