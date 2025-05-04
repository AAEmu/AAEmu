using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNotifyInGamePacket() : GamePacket(CSOffsets.CSNotifyInGamePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // No data
    }

    public override void Execute()
    {
        Connection.ActiveChar.IsOnline = true;

        Connection.ActiveChar.Spawn();

        // Joining channel 1 (shout) will automatically also join /lfg and /trade for that zone on the client-side
        // Back in 1.x /trade was zone based, not faction based
        ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId).JoinChannel(Connection.ActiveChar); // shout, trade, lfg
        ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).JoinChannel(Connection.ActiveChar); // nation
        // TODO: Implement crime system, actual jury channel doesn't exist yet
        Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Judge, 0, Connection.ActiveChar.Faction.MotherId)); //trial
        ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).JoinChannel(Connection.ActiveChar); // faction

        // TODO: Maybe move to spawn character?
        TeamManager.Instance.UpdateAtLogin(Connection.ActiveChar);
        Connection.ActiveChar.Expedition?.OnCharacterLogin(Connection.ActiveChar);

        Connection.ActiveChar.UpdateGearBonuses(null, null);

        Logger.Info($"NotifyInGame: {Connection.ActiveChar?.Name} ({Connection.ActiveChar?.Id})");
    }
}
