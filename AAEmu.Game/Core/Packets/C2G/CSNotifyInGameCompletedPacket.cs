using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNotifyInGameCompletedPacket() : GamePacket(CSOffsets.CSNotifyInGameCompletedPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        WorldManager.Instance.OnPlayerJoin(Connection.ActiveChar);
        // Load finished — arm mirror interest. Do NOT start during NotifyInGame (mid-load Quit).
        // Grace default 0 (was 3000ms workaround); AAEMU_MIRROR_NPC_GRACE_MS to restore delay.
        Connection.ActiveChar?.ArmMirrorNpcStream(graceMs: ParseMirrorGraceMs());
        Logger.Info(
            $"NotifyInGameCompleted SubZoneId {Connection.ActiveChar?.SubZoneId}, {Connection.ActiveChar?.Name} ({Connection.ActiveChar?.Id}) mirrorStream armed");
        if (Connection.ActiveChar != null)
        {
            WorldIntegration.SyncTowerDefsToCharacter?.Invoke(Connection.ActiveChar);
            SquadManager.Instance.SyncClientSquadAfterLogin(Connection.ActiveChar);
            Connection.ActiveChar.WorldEntryCompleted = true;
            // A cinema the previous session never finished still owes its buff or teleport.
            // Load only queues it — the effect needs the live connection that entry brings.
            Connection.ActiveChar.Quests.FlushPendingCinemaEndEffects();

            // Locks and pending unlocks are not restored by the item bodies the client received; it
            // takes them from the security action. That action is discarded while the client is still
            // on the loading screen (sending it from NotifyInGame produced the packets and no client
            // state change, 2026-09-16), so replay the state here, once the load has finished.
            Connection.ActiveChar.Inventory.SendItemSecurityStates();

            // The reinforcement window reads one level and one bar per slot, and the per-slot packet is
            // its only source. Replay the whole ladder here, with the load finished, for the same reason
            // the security state is replayed here and not earlier.
            Connection.ActiveChar.EquipSlotReinforces.SendAll();

            // My List reads a client-side store that Load replaces. Post fills it in-session;
            // a relog clears it, and opening the board from the folio never runs the doodad
            // func, so the store has to be sent here.
            CraftOrderManager.Instance.SendOwnEntries(Connection.ActiveChar);

            // The collection view is drawn from the achievement list, and discoveries resolved while the
            // load was still running were held back for exactly this point — the load has finished now,
            // so replay the collection rows alongside the other post-entry state.
            CollectionsManager.Instance.FlushInitialSync(Connection.ActiveChar);
        }
    }

    private static int ParseMirrorGraceMs()
    {
        var raw = System.Environment.GetEnvironmentVariable("AAEMU_MIRROR_NPC_GRACE_MS");
        return int.TryParse(raw, out var n) && n >= 0 ? n : 0;
    }
}
