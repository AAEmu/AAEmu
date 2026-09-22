using System.Collections.Concurrent;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventNpcCombatEndeds : IndunEvent
{
    public uint NpcId { get; set; }

    /// <summary>world id to object ids of this NPC template in combat; one entry per copy on the shared event.</summary>
    private readonly ConcurrentDictionary<uint, HashSet<uint>> _inCombat = new();

    public override void Subscribe(WorldInstance worldInstance)
    {
        _inCombat[worldInstance.Id] = [];
        worldInstance.Events.OnUnitCombatStart += OnUnitCombatStart;
        worldInstance.Events.OnUnitCombatEnd += OnUnitCombatEnd;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        _inCombat.TryRemove(worldInstance.Id, out _);
        worldInstance.Events.OnUnitCombatStart -= OnUnitCombatStart;
        worldInstance.Events.OnUnitCombatEnd -= OnUnitCombatEnd;
    }

    private void OnUnitCombatStart(object sender, OnUnitCombatStartArgs args)
    {
        if (args.Npc is not Npc npc || sender is not WorldInstance world) { return; }
        if (npc.TemplateId != NpcId || !_inCombat.TryGetValue(world.Id, out var set)) { return; }

        lock (set)
            set.Add(npc.ObjId);
    }

    private void OnUnitCombatEnd(object sender, OnUnitCombatEndArgs args)
    {
        if (args.Npc is not Npc npc || sender is not WorldInstance world) { return; }
        if (npc.TemplateId != NpcId || !_inCombat.TryGetValue(world.Id, out var set)) { return; }

        // A kill raises OnUnitCombatEnd from Unit.ReduceCurrentHp and again from the DoDie flag
        // transition; the end counts once per start of that object id.
        bool fire;
        lock (set)
            fire = IndunEventRules.ShouldFireCombatEnd(set, npc.ObjId);
        if (!fire) { return; }

        Logger.Debug($"IndunEventNpcCombatEndeds - {NpcId} ({npc.ObjId}) has left combat.");
        IndunManager.Instance.DoIndunActions(StartActionId, world);
    }
}
