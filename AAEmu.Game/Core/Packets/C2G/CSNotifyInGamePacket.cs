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

        // First packet the reference pushes once the context reaches INGAME — enables the client's gameplay
        // feature/HUD systems before the player frame renders.
        Connection.ActiveChar.SendPacket(new SCSystemFeatureStateListPacket());

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

        // The player-frame event window shows during the post-NotifyInGame load and reads its event counts; the
        // client crashes on show without them. The reference server sends this (all-zero, no active events) at
        // world entry — emit it here so the window has data before it renders.
        Connection.ActiveChar.SendPacket(new SCEventInfoCountPacket());

        // World-level state for the GetWorldLevel HUD provider. Must be sent AFTER Spawn() (above): the client's
        // world-level manager binds this data to the local player unit, so the unit has to exist or the link
        // stays null and the provider null-derefs when the player-frame event window shows.
        // The reference emits 0x038A ~4s after NotifyInGame, never in the select burst.
        Connection.ActiveChar.SendPacket(new SCWorldLevelInfoPacket());

        Logger.Info($"NotifyInGame: {Connection.ActiveChar?.Name} ({Connection.ActiveChar?.Id})");
    }
}
