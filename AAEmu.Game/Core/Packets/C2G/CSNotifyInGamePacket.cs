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

        // NOTE: do NOT deliver the local player via a self SCUnitState. It reaches the X+8 bind
        // (ClientUnitOwner::SetUnit, sub_39685D60) but the Character self-spawn build path is structurally
        // crash-prone: for the local unit the client builds an actor-less EmptyUnitModel placeholder (its
        // vtable[1088] "getActor" is a return-0 stub) and sub_3935F170 then synchronously derefs that null actor
        // (crash sub_3930A2F0 @0x3930A591). RE-verified our modelRef/appearance are valid — the fault is the
        // delivery mechanism, not the data. The reference server sends NO self SCUnitState; the client builds the
        // player natively (fully model-loaded) and binds X+8 there. Fixing X+8 must go through that native path.

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
        // world-level manager binds this data to the local player unit, so the unit has to exist or its link
        // (*(ClientPlayer+104)+8) stays null and the provider null-derefs when the player-frame event window shows.
        // The reference emits 0x038A ~4s after NotifyInGame, never in the select burst.
        Connection.ActiveChar.SendPacket(new SCWorldLevelInfoPacket());

        Logger.Info($"NotifyInGame: {Connection.ActiveChar?.Name} ({Connection.ActiveChar?.Id})");
    }
}
