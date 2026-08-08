using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Procs;

namespace AAEmu.Game.Models.Game.Units;

public class UnitProcs(Unit owner)
{
    private readonly List<ItemProc> _procs = [];
    private readonly Dictionary<ProcChanceKind, List<ItemProc>> _procsByChanceKind = [];

    public Unit Owner { get; set; } = owner;

    public void AddProc(uint procId)
    {
        var template = ItemManager.Instance.GetItemProcTemplate(procId);
        if (template == null)
            return;

        if (!_procsByChanceKind.TryGetValue(template.ChanceKind, out var kindProcs))
        {
            kindProcs = [];
            _procsByChanceKind.Add(template.ChanceKind, kindProcs);
        }

        // Has to stay idempotent: ApplyEquipItemSetBonuses re-runs on every equipment change and re-adds
        // the procs of every set still worn, so without this a worn set gained one more copy of its proc
        // per gear update and rolled that many times per trigger.
        if (kindProcs.Exists(p => p.TemplateId == procId))
            return;

        var proc = new ItemProc(procId);
        _procs.Add(proc);
        kindProcs.Add(proc);
    }

    public void RemoveProc(uint procId)
    {
        var procTemplate = ItemManager.Instance.GetItemProcTemplate(procId);
        if (procTemplate == null)
            return;

        _procs.RemoveAll(p => p.TemplateId == procId);
        if (_procsByChanceKind.TryGetValue(procTemplate.ChanceKind, out var value))
            value.RemoveAll(p => p.TemplateId == procId);
    }

    public void RollProcsForKind(ProcChanceKind kind)
    {
        if (!_procsByChanceKind.TryGetValue(kind, out var procs))
            return;

        // Snapshot: a proc casts a skill on the owner, which can come back around into Add/RemoveProc.
        foreach (var proc in procs.ToArray())
        {
            // Apply owns both the cooldown gate and the chance roll, and only a proc that really fired
            // starts a new cooldown. The guard that used to stand here was inverted - it skipped exactly
            // those procs whose cooldown had expired - and since LastProc was advanced only inside the
            // branch that could therefore never run, no item proc in the game ever fired.
            if (proc.Apply(Owner))
                proc.LastProc = DateTime.UtcNow;
        }
    }
}
