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
        }
    }

    private static int ParseMirrorGraceMs()
    {
        var raw = System.Environment.GetEnvironmentVariable("AAEMU_MIRROR_NPC_GRACE_MS");
        return int.TryParse(raw, out var n) && n >= 0 ? n : 0;
    }
}
