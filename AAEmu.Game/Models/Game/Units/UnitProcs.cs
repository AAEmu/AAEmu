using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// The item procs a unit carries and the events that roll them. A proc reaches a unit from one of three places:
/// <c>equip_item_set_bonuses.proc_id</c> (76 rows, 57 procs) through <see cref="AddProc"/>, and
/// <c>item_proc_bindings</c> (186 rows, 102 procs) or <c>holdables.item_proc_id</c> (holdables 24 and 29, proc 69)
/// through <see cref="SyncItemProcs"/>. The three cover 157 of the 202 <c>item_procs</c> rows; the other 45 are
/// reachable through none of them.
/// </summary>
public class UnitProcs(Unit owner)
{
    private readonly Dictionary<uint, ItemProc> _procs = [];
    private readonly Dictionary<ProcChanceKind, List<ItemProc>> _procsByChanceKind = [];

    /// <summary>
    /// Which source granted a proc, kept per source so that losing one does not take a proc the other still
    /// grants: three procs are both a set bonus and bound to an item.
    /// </summary>
    private readonly HashSet<uint> _setBound = [];
    private readonly HashSet<uint> _itemBound = [];

    public Unit Owner { get; set; } = owner;

    public int Count => _procs.Count;

    /// <summary>
    /// A set-bonus proc. Has to stay idempotent: ApplyEquipItemSetBonuses re-runs on every equipment change and
    /// re-adds the procs of every set still worn.
    /// </summary>
    public void AddProc(uint procId)
    {
        if (Attach(procId))
            _setBound.Add(procId);
    }

    public void RemoveProc(uint procId)
    {
        _setBound.Remove(procId);
        if (!_itemBound.Contains(procId))
            Detach(procId);
    }

    /// <summary>
    /// The procs the worn items themselves carry, as one set: what is new is attached, what is no longer worn is
    /// detached unless a set bonus still grants it.
    /// </summary>
    public void SyncItemProcs(IEnumerable<uint> procIds)
    {
        var desired = new HashSet<uint>(procIds);
        foreach (var procId in _itemBound.Where(id => !desired.Contains(id)).ToList())
        {
            _itemBound.Remove(procId);
            if (!_setBound.Contains(procId))
                Detach(procId);
        }

        foreach (var procId in desired)
        {
            if (Attach(procId))
                _itemBound.Add(procId);
        }
    }

    /// <summary>The owner landed a hit (a DamageEffect whose <c>fire_proc</c> is set): kinds 1-8.</summary>
    public void OnHit(DamageType damageType, SkillHitType hitType, Unit victim, Skill sourceSkill, bool victimDied) =>
        Roll(ItemProcRules.HitKinds(damageType, ItemProcRules.IsCritical(hitType)), victim, sourceSkill, victimDied);

    /// <summary>The owner took a hit: kinds 9-16. The attacker is the other side.</summary>
    public void OnDamageTaken(DamageType damageType, SkillHitType hitType, Unit attacker, Skill sourceSkill) =>
        Roll(ItemProcRules.TakeDamageKinds(damageType, ItemProcRules.IsCritical(hitType)), attacker, sourceSkill, false);

    /// <summary>The owner healed a unit (HealEffect): kinds 17 and 18. The healed unit is the other side.</summary>
    public void OnHeal(bool critical, Unit healed, Skill sourceSkill) =>
        Roll(ItemProcRules.HealKinds(critical), healed, sourceSkill, false);

    /// <summary>The owner's cast reached its fire edge (Skill.Cast, or the plot-only fire costs): kind 19.</summary>
    public void OnSkillFired(Skill skill, BaseUnit target) =>
        Roll(ItemProcRules.SkillFiredKinds(), target as Unit, skill, false);

    private void Roll(IReadOnlyList<ProcChanceKind> kinds, Unit other, Skill sourceSkill, bool victimDied)
    {
        if (!ItemProcRules.SourceMayProc(sourceSkill?.FromItemProc ?? false))
            return;

        var sourceSkillId = sourceSkill?.Id ?? 0;
        foreach (var kind in kinds)
        {
            if (!_procsByChanceKind.TryGetValue(kind, out var procs))
                continue;

            // Snapshot: a proc casts a skill on the owner, which can come back around into the equipment sync.
            foreach (var proc in procs.ToArray())
                proc.Apply(Owner, other, sourceSkillId, victimDied);
        }
    }

    private bool Attach(uint procId)
    {
        var template = ItemManager.Instance.GetItemProcTemplate(procId);
        if (template == null)
            return false;
        if (_procs.ContainsKey(procId))
            return true;

        var proc = new ItemProc(procId);
        _procs.Add(procId, proc);
        if (!_procsByChanceKind.TryGetValue(template.ChanceKind, out var kindProcs))
        {
            kindProcs = [];
            _procsByChanceKind.Add(template.ChanceKind, kindProcs);
        }

        kindProcs.Add(proc);
        return true;
    }

    private void Detach(uint procId)
    {
        if (!_procs.Remove(procId, out var proc))
            return;

        foreach (var kindProcs in _procsByChanceKind.Values)
            kindProcs.Remove(proc);
    }
}
