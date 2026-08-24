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
        }
    }

    private static int ParseMirrorGraceMs()
    {
        var raw = System.Environment.GetEnvironmentVariable("AAEMU_MIRROR_NPC_GRACE_MS");
        return int.TryParse(raw, out var n) && n >= 0 ? n : 0;
    }
}
