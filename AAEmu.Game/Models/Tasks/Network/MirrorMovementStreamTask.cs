namespace AAEmu.Game.Models.Tasks.Network;

/// <summary>
/// DISABLED (2026-07-19) — do not schedule. Synthetic idle SCUnitMovements self-stands caused clean
/// System:Quit; Ping/Pong alone is sufficient keepalive. Commercial movement = real ZWUnitMovements only
/// via <c>MovementRelay</c>. Class retained as a no-op stub so old references compile.
/// </summary>
public class MirrorMovementStreamTask : Task
{
    public override void Execute()
    {
    }
}
