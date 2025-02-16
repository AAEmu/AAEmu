using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char.Static;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNotifyInGamePacket : GamePacket
{
    public CSNotifyInGamePacket() : base(CSOffsets.CSNotifyInGamePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
    }

    public override void Execute()
    {
        Connection.ActiveChar.IsOnline = true;

        Connection.ActiveChar.Spawn();
        //Connection.ActiveChar.StartRegen();

        ResidentManager.Instance.UpdateAtLogin(Connection.ActiveChar);

        //Connection.SendPacket(new SCScheduledEventStartedPacket());

        // Joining channel 1 (shout) will automatically also join /lfg and /trade for that zone on the client-side
        // Back in 1.x /trade was zone base, not faction based
        ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId).JoinChannel(Connection.ActiveChar); // shout, trade, lfg
        ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).JoinChannel(Connection.ActiveChar); // nation
        Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Judge, 0, Connection.ActiveChar.Faction.MotherId)); //trial
        ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).JoinChannel(Connection.ActiveChar); // faction

        // TODO - MAYBE MOVE TO SPAWN CHARACTER
        //TeamManager.Instance.UpdateAtLogin(Connection.ActiveChar);
        //Connection.ActiveChar.Expedition?.OnCharacterLogin(Connection.ActiveChar);
        //ExpeditionManager.SendMyExpeditionDescInfo(Connection.ActiveChar);
        
        Connection.ActiveChar.SendPacket(new SCDailyCountPacket(0, 0, 5));
        Connection.ActiveChar.SendPacket(new SCDailyResetPacket(DailyResetKind.Instance));
        Connection.ActiveChar.SendPacket(new SCDailyResetPacket(DailyResetKind.AbilitySetFreeActivationCount));
        Connection.ActiveChar.SendPacket(new SCCurServerTimePacket(DateTime.UtcNow));

        if (Connection.ActiveChar.Attendances.Attendances?.Count == 0)
        {
            Connection.ActiveChar.Attendances.SendEmptyAttendances();
        }
        else
        {
            Connection.ActiveChar.Attendances.Send();
        }

        Connection.ActiveChar.UpdateGearBonuses(null, null);

        Logger.Info("NotifyInGame");
    }
}
