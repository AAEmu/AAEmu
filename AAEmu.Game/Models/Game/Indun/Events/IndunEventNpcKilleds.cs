using System.Collections.Concurrent;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventNpcKilleds : IndunEvent
{
    public uint NpcId { get; set; }

    /// <summary>world id to object ids already counted dead; one entry per copy on the shared event.</summary>
    private readonly ConcurrentDictionary<uint, HashSet<uint>> _fired = new();

    public override void Subscribe(WorldInstance worldInstance)
    {
        _fired[worldInstance.Id] = [];
        worldInstance.Events.OnUnitKilled += OnUnitKilled;
        worldInstance.Events.OnUnitSpawn += OnUnitSpawn;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        _fired.TryRemove(worldInstance.Id, out _);
        worldInstance.Events.OnUnitKilled -= OnUnitKilled;
        worldInstance.Events.OnUnitSpawn -= OnUnitSpawn;
    }

    private void OnUnitSpawn(object sender, OnUnitSpawnArgs args)
    {
        // A respawn under the same object id is a new life.
        if (args.Npc is not Npc npc || sender is not WorldInstance world) { return; }
        if (npc.TemplateId != NpcId || !_fired.TryGetValue(world.Id, out var set)) { return; }

        lock (set)
            set.Remove(npc.ObjId);
    }

    private void OnUnitKilled(object sender, OnUnitKilledArgs args)
    {
        if (args.Victim is not Npc npc || sender is not WorldInstance world) { return; }
        if (npc.TemplateId != NpcId || !_fired.TryGetValue(world.Id, out var set)) { return; }

        // One death raises OnUnitKilled from Unit.ReduceCurrentHp and again from Unit.DoDie.
        bool fire;
        lock (set)
            fire = IndunEventRules.ShouldFireKill(set, npc.ObjId);
        if (!fire) { return; }

        Logger.Debug($"IndunEventNpcKilleds - {NpcId} ({npc.ObjId}), event {Id}");
        IndunManager.Instance.DoIndunActions(StartActionId, world);
    }
}
