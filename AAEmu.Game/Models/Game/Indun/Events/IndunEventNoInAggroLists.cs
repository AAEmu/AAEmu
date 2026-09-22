using System.Collections.Concurrent;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

/// <summary>
/// <c>indun_event_no_in_aggro_lists</c> (10 rows; zone groups 105, 120, 146): the NPCs tagged <c>tag_id</c>
/// have nobody left on their aggro lists. Content names it a wipe check ("noinaggrolist로 전멸체크",
/// "칼릴탈몬 전멸") and a fight-over signal ("헤임달과 전투종료"): the tagged NPCs are alive and out of
/// combat. Armed by the first tagged combat start, it fires once when the last tagged NPC leaves combat.
/// </summary>
internal class IndunEventNoInAggroLists : IndunEvent
{
    public uint TagId { get; set; }

    /// <summary>world id to armed; shared event object, one entry per copy.</summary>
    private readonly ConcurrentDictionary<uint, bool> _armed = new();

    public override void Subscribe(WorldInstance worldInstance)
    {
        _armed[worldInstance.Id] = false;
        worldInstance.Events.OnUnitCombatStart += OnUnitCombatStart;
        worldInstance.Events.OnUnitCombatEnd += OnUnitCombatEnd;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        _armed.TryRemove(worldInstance.Id, out _);
        worldInstance.Events.OnUnitCombatStart -= OnUnitCombatStart;
        worldInstance.Events.OnUnitCombatEnd -= OnUnitCombatEnd;
    }

    private bool IsTagged(Npc npc) =>
        npc != null && TagsGameData.Instance.GetIdsByTagId(TagsGameData.TagType.Npcs, TagId).Contains(npc.TemplateId);

    private void OnUnitCombatStart(object sender, OnUnitCombatStartArgs args)
    {
        if (args?.Npc is not Npc npc || sender is not WorldInstance world || !IsTagged(npc))
            return;

        _armed[world.Id] = true;
    }

    private void OnUnitCombatEnd(object sender, OnUnitCombatEndArgs args)
    {
        if (args?.Npc is Npc npc && sender is WorldInstance world)
            CheckDisengaged(world, npc);
    }

    private void CheckDisengaged(WorldInstance world, Npc npc)
    {
        if (!IsTagged(npc) || !_armed.TryGetValue(world.Id, out var armed))
            return;
        if (!IndunEventRules.ShouldFireNoInAggroList(armed, AnyTaggedNpcInCombat(world)))
            return;

        _armed[world.Id] = false;
        Logger.Debug($"IndunEventNoInAggroList {Id}: tag {TagId} disengaged in world {world.Id}");
        IndunManager.Instance.DoIndunActions(StartActionId, world);
    }

    /// <summary>
    /// A tagged NPC still alive with a combat flag or a non-empty aggro table keeps the event armed. A dead
    /// one is already out of the fight, so the check only considers the living; that is what lets the death
    /// of the last tagged NPC fire this.
    /// </summary>
    private bool AnyTaggedNpcInCombat(WorldInstance world)
    {
        var npcs = new List<Npc>();
        foreach (var region in world.Regions)
            region?.GetList(npcs, 0);

        foreach (var npc in npcs)
        {
            if (npc == null || npc.Hp <= 0 || npc.IsDead || !IsTagged(npc))
                continue;
            if (npc.IsInBattle || !npc.AggroTable.IsEmpty)
                return true;
        }

        return false;
    }
}
