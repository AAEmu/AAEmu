using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.Network;

/// <summary>
/// Retail-like mirror interest maintenance (not a 1/s spawn drip):
/// soft AOI cull (SCUnitsRemoved), MAX eviction for nearer pending, then flush any
/// pending UnitStates that walked into AOI or were queued during load.
/// Create path is primarily <see cref="Npc.AddVisibleObject"/> → immediate SCUnitState
/// when stream-ready; this task only drains backlog / walk-ins.
/// </summary>
public class MirrorSpawnStreamTask : Task
{
    private static readonly object SendGate = new();
    private static long _lastSendTicks;

    /// <summary>
    /// Optional throttle between drain waves. Default 0 (off) — retail has no artificial
    /// UnitState metronome; AAEMU_MIRROR_NPC_INTERVAL_MS restores the old drip if needed.
    /// </summary>
    private static readonly int MinIntervalMs = ParseIntervalMs();

    private static int ParseIntervalMs()
    {
        var raw = System.Environment.GetEnvironmentVariable("AAEMU_MIRROR_NPC_INTERVAL_MS");
        if (int.TryParse(raw, out var n) && n >= 0)
            return n;
        return 0;
    }

    public override void Execute()
    {
        lock (SendGate)
        {
            var now = Environment.TickCount64;

            foreach (var character in WorldManager.Instance.GetAllCharacters())
            {
                if (!character.MirrorNpcStreamReady)
                    continue;

                if (character.MirrorNpcStreamNotBeforeTick != 0 &&
                    now < character.MirrorNpcStreamNotBeforeTick)
                    continue;

                // Leave-view first (beyond soft AOI) — frees MAX slots before we try to send.
                character.CullStreamedMirrorsBeyondAoi();
                character.CullStreamedSlavesBeyondAoi();
                character.TryFlushPendingSlaves();

                // At cap: nearer pending first, then event/tower priority rifts (steal ambient slot).
                if (Npc.MirrorNpcMaxPerCharacter > 0 &&
                    character.MirrorNpcStatesSentCount >= Npc.MirrorNpcMaxPerCharacter &&
                    character.HasPendingMirrorSpawns)
                {
                    if (!character.TryEvictFarthestStreamedForNearerPending() &&
                        character.TryPeekNearestPendingMirror(out var pendingPri, out _, out _) &&
                        pendingPri is { IsMirrorStreamPriority: true })
                    {
                        character.TryEvictFarthestStreamedForPriority(pendingPri);
                    }
                }

                if (!character.HasPendingMirrorSpawns)
                    continue;

                if (Npc.MirrorNpcMaxPerCharacter > 0 &&
                    character.MirrorNpcStatesSentCount >= Npc.MirrorNpcMaxPerCharacter)
                    continue;

                if (MinIntervalMs > 0 && now - _lastSendTicks < MinIntervalMs)
                    continue;

                // 0 = flush all pending in AOI this tick (up to MAX). Non-zero = old BURST drip.
                var perTick = Npc.MirrorNpcImmediateBurst == 0
                    ? int.MaxValue
                    : Math.Max(1, Npc.MirrorNpcImmediateBurst);

                var sent = 0;
                while (sent < perTick)
                {
                    if (Npc.MirrorNpcMaxPerCharacter > 0 &&
                        character.MirrorNpcStatesSentCount >= Npc.MirrorNpcMaxPerCharacter)
                    {
                        // One more priority peel per burst wave if still capped.
                        if (!character.TryPeekNearestPendingMirror(out var p2, out _, out _) ||
                            p2 is not { IsMirrorStreamPriority: true } ||
                            !character.TryEvictFarthestStreamedForPriority(p2))
                            break;
                    }

                    var npc = character.TryTakeNearestPendingMirror();
                    if (npc == null)
                        break;

                    npc.SendUnitStateTo(character);
                    sent++;
                }

                if (sent > 0)
                    _lastSendTicks = now;
            }
        }
    }
}
