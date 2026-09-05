using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
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
        // Commercial World: zone is sim authority. No healthy zone → do not enter on local Game sim.
        if (WorldIntegration.ZoneAuthority)
        {
            if (Connection.ActiveChar == null)
            {
                Logger.Error("NotifyInGame: no active character is available; closing the session");
                Connection.Shutdown();
                return;
            }

            if (WorldIntegration.TryEnterZone == null)
            {
                const string reason = "zone authority is enabled but its enter route is unavailable";
                Logger.Error("NotifyInGame: {0}; returning to character select", reason);
                if (!EnterWorldManager.Instance.ReturnToCharacterSelect(Connection, reason))
                    Connection.Shutdown();
                return;
            }

            var body = WorldIntegration.BuildWzUnitStateBody(Connection.ActiveChar);
            if (!WorldIntegration.TryEnterZone(Connection.ActiveChar.ObjId, body))
            {
                Logger.Error(
                    "NotifyInGame: zone enter refused for {0}; returning to character select",
                    Connection.ActiveChar.Name);
                if (!EnterWorldManager.Instance.ReturnToCharacterSelect(
                        Connection,
                        "the requested zone is not available"))
                    Connection.Shutdown();
                return;
            }
        }

        Connection.ActiveChar.IsOnline = true;

        // First packet the reference pushes once the context reaches INGAME — enables the client's gameplay
        // feature/HUD systems before the player frame renders.
        Connection.ActiveChar.SendPacket(new SCSystemFeatureStateListPacket());

        // Temporary: still Spawn for CS/SC client glue until World relays ZW→SC fully.
        // Zone already owns presence when ZoneAuthority + TryEnterZone succeeded above.
        Connection.ActiveChar.Spawn();

        // DO NOT seed the physics clock from the server's Environment.TickCount64 here. That is the SERVER
        // uptime domain (~tens of millions of ms), NOT the client's physics clock (which starts near 0 at
        // client launch). Seeding it made every self/NPC stand carry a tPhy ~89,000,000 ms in the client's
        // "future"; the client's real clock (~140,000 ms) then saw its own movements time-stamped far ahead,
        // its client-driven-movement binding broke ("can't load client driven connect info"), and it dropped
        // the connection a few seconds after spawn. The anchor is now seeded ONLY from client-reported values
        // (PingPacket.tm / CSMoveUnit.Time), which are in the client's own clock domain. MirrorMovementStream
        // simply waits (HasPhysTimeAnchor == false) until the first client ping arrives — that happens within
        // ~1s, well before any idle watchdog.

        // NOTE: do NOT deliver the local player via a self SCUnitState. It reaches the X+8 bind
        // crash-prone: for the local unit the client builds an actor-less EmptyUnitModel placeholder (its
        // delivery mechanism, not the data. The reference server sends NO self SCUnitState; the client builds the
        // player natively (fully model-loaded) and binds X+8 there. Fixing X+8 must go through that native path.

        // Joining channel 1 (shout) will automatically also join /lfg and /trade for that zone on the client-side
        // Back in 1.x /trade was zone based, not faction based
        var zoneChat = ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId);
        if (!zoneChat.JoinChannel(Connection.ActiveChar)) // shout, trade, lfg
            zoneChat.AnnounceTo(Connection.ActiveChar);  // already a member from OnZoneChange - tell the client anyway
        ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).JoinChannel(Connection.ActiveChar); // nation
        // TODO: Implement crime system, actual jury channel doesn't exist yet
        Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Judge, 0, Connection.ActiveChar.Faction.MotherId)); //trial
        ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).JoinChannel(Connection.ActiveChar); // faction
        ChatManager.Instance.GetGlobalChat().JoinChannel(Connection.ActiveChar); // CSM - server-wide, both factions

        // TODO: Maybe move to spawn character?
        TeamManager.Instance.UpdateAtLogin(Connection.ActiveChar);
        Connection.ActiveChar.Expedition?.OnCharacterLogin(Connection.ActiveChar);

        Connection.ActiveChar.UpdateGearBonuses(null, null);

        // Combat resources (combat_resources) after Spawn(): seeding applies each pool's bar buff, and
        // both that buff and the point packet address the local player unit, which only exists once the
        // character is spawned. default_point had never been read, so every pool started each session at
        // 0 and the abilities gated on them could not reach their first tier.
        Connection.ActiveChar.InitializeCombatResources();
        Connection.ActiveChar.SendAllCombatResources();

        // The player-frame event window shows during the post-NotifyInGame load and reads its event counts; the
        // client crashes on show without them. The reference server sends this (all-zero, no active events) at
        // world entry — emit it here so the window has data before it renders.
        Connection.ActiveChar.SendPacket(new SCEventInfoCountPacket());

        // World-level state for the GetWorldLevel HUD provider. Must be sent AFTER Spawn() (above): the client's
        // world-level manager binds this data to the local player unit, so the unit has to exist or its link
        // (*(ClientPlayer+104)+8) stays null and the provider null-derefs when the player-frame event window shows.
        // The reference emits 0x038A ~4s after NotifyInGame, never in the select burst.
        Connection.ActiveChar.SendPacket(new SCWorldLevelInfoPacket());

        // Daily schedule: load persisted contracts for today, then reset-count budget.
        TodayAssignmentManager.Instance.OnCharacterEnterWorld(Connection.ActiveChar);

        // Lobby already sent these during FinishState 0, but the in-world player object
        // is built later and does not keep that map. Listing authority is read here.
        Connection.SendPacket(new SCAccountAttributeConfigPacket());
        AccountAttributePublisher.Send(Connection);

        // Mirror interest armed on NotifyInGameCompleted — not here during load.
        Logger.Info($"NotifyInGame: {Connection.ActiveChar?.Name} ({Connection.ActiveChar?.Id}) zoneAuth={WorldIntegration.ZoneAuthority}");
    }
}
