using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Procs;

namespace AAEmu.Game.Models.Game.Units;

public class UnitProcs
{
    private List<ItemProc> _procs;
    private Dictionary<ProcChanceKind, List<ItemProc>> _procsByChanceKind;

    public Unit Owner { get; set; }

    public UnitProcs(Unit owner)
    {
        Owner = owner;
        _procsByChanceKind = [];
        _procs = [];
    }

    public void AddProc(uint procId)
    {
        var proc = new ItemProc(procId);
        _procs.Add(proc);
        if (!_procsByChanceKind.ContainsKey(proc.Template.ChanceKind))
            _procsByChanceKind.Add(proc.Template.ChanceKind, []);
        _procsByChanceKind[proc.Template.ChanceKind].Add(proc);
    }

    public void RemoveProc(uint procId)
    {
        var procTemplate = ItemManager.Instance.GetItemProcTemplate(procId);

        if (_procsByChanceKind.TryGetValue(procTemplate.ChanceKind, out var value))
            value.RemoveAll(p => p.TemplateId == procId);
    }

    public void RollProcsForKind(ProcChanceKind kind)
    {
        if (!_procsByChanceKind.TryGetValue(kind, out var procs))
            return;
        foreach (var proc in procs)
        {
            if (proc.LastProc.AddSeconds(proc.Template.CooldownSec) <= DateTime.UtcNow)
                continue;

            proc.Apply(Owner);
            proc.LastProc = DateTime.UtcNow;
        }
    }
}
