using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Procs;

namespace AAEmu.Game.Models.Game.Units
{
    public class UnitProcs
    {
        private List<ItemProc> _procs;
        private Dictionary<ProcChanceKind, List<ItemProc>> _procsByChanceKind;
        
        public Unit Owner { get; set; }

        public UnitProcs(Unit owner)
        {
            Owner = owner;
            _procsByChanceKind = new Dictionary<ProcChanceKind, List<ItemProc>>();
            _procs = new List<ItemProc>();
        }

        public void AddProc(uint procId)
        {
            var proc = new ItemProc(procId);
            _procs.Add(proc);
            if (!_procsByChanceKind.ContainsKey(proc.Template.ChanceKind))
                _procsByChanceKind.Add(proc.Template.ChanceKind, new List<ItemProc>());
            _procsByChanceKind[proc.Template.ChanceKind].Add(proc);
        }

        public void RemoveProc(uint procId)
        {
            var procTemplate = ItemManager.Instance.GetItemProcTemplate(procId);

            if (_procsByChanceKind.ContainsKey(procTemplate.ChanceKind))
                _procsByChanceKind[procTemplate.ChanceKind].RemoveAll(p => p.TemplateId == procId);
        }

        public void RollProcsForKind(ProcChanceKind kind)
        {
            if (!_procsByChanceKind.ContainsKey(kind))
                return;
            var procs = _procsByChanceKind[kind];
            
            foreach (var proc in procs)
            {
                if (proc.LastProc.AddSeconds(proc.Template.CooldownSec) <= DateTime.Now)
                    continue;

                proc.Apply(Owner);
                proc.LastProc = DateTime.Now;
            }
        }
    }

}
