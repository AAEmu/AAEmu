using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPulse : DoodadPhaseFuncTemplate
    {
        public bool Flag { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Debug($"DoodadFuncPulse: Flag {Flag}");

            if (caster is not null)
            {
                var aroundDoodads = WorldManager.Instance.GetAround<Doodad>(caster);
                var doodads = new List<Doodad>();
                if (owner?.Spawner != null)
                {
                    if (owner.Spawner.RelatedIds != null)
                    {
                        foreach (var relatedId in owner.Spawner.RelatedIds)
                        {
                            if (aroundDoodads != null)
                            {
                                doodads.AddRange(aroundDoodads.Where(doodad => doodad.TemplateId == relatedId));
                            }
                        }
                    }

                    foreach (var doodad in doodads)
                    {
                        var funcGroup = DoodadManager.Instance.GetPhaseFunc(doodad.FuncGroupId);
                        foreach (var func in funcGroup)
                        {
                            switch (func.FuncType)
                            {
                                case "DoodadFuncPulseTrigger":
                                    {
                                        DoodadFuncPulseTrigger.Halt = false; // разрешаем однократное выполнение // allow one-time execution
                                        doodad.DoPhaseFuncs(caster, (int)doodad.FuncGroupId);
                                        break;
                                    }
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
