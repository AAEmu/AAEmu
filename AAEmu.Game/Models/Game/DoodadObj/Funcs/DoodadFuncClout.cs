﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncClout : DoodadPhaseFuncTemplate
    {
        // doodad_phase_funcs
        public int Duration { get; set; }
        public int Tick { get; set; }
        public SkillTargetRelation TargetRelation { get; set; }
        public uint BuffId { get; set; }
        public uint ProjectileId { get; set; }
        public bool ShowToFriendlyOnly { get; set; }
        public int NextPhase { get; set; }
        public uint AoeShapeId { get; set; }
        public uint TargetBuffTagId { get; set; }
        public uint TargetNoBuffTagId { get; set; }
        public bool UseOriginSource { get; set; }
        public List<uint> Effects { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            if (caster is Character)
                _log.Debug("DoodadFuncClout : Duration {0}, Tick {1}, TargetRelationId {2}, BuffId {3}, ProjectileId {4}, ShowToFriendlyOnly {5}, NextPhase {6}, AoeShapeId {7}, TargetBuffTagId {8}, TargetNoBuffTagId {9}, UseOriginSource {10}", Duration, Tick, TargetRelation, BuffId, ProjectileId, ShowToFriendlyOnly, NextPhase, AoeShapeId, TargetBuffTagId, TargetNoBuffTagId, UseOriginSource);
            else
                _log.Trace("DoodadFuncClout : Duration {0}, Tick {1}, TargetRelationId {2}, BuffId {3}, ProjectileId {4}, ShowToFriendlyOnly {5}, NextPhase {6}, AoeShapeId {7}, TargetBuffTagId {8}, TargetNoBuffTagId {9}, UseOriginSource {10}", Duration, Tick, TargetRelation, BuffId, ProjectileId, ShowToFriendlyOnly, NextPhase, AoeShapeId, TargetBuffTagId, TargetNoBuffTagId, UseOriginSource);

            var areaTrigger = new AreaTrigger()
            {
                Shape = WorldManager.Instance.GetAreaShapeById(AoeShapeId),
                Owner = owner,
                Caster = caster,
                InsideBuffTemplate = SkillManager.Instance.GetBuffTemplate(BuffId),
                TargetRelation = TargetRelation,
                TickRate = Tick,
                EffectPerTick = Effects.Select(eid => SkillManager.Instance.GetEffectTemplate(eid)).ToList(),
                //SkillId = skillId
            };

            AreaTriggerManager.Instance.AddAreaTrigger(areaTrigger);

            if (Duration > 0)
            {
                // TODO : Add a proper delay in here
                Task.Run(async () =>
                {
                    await Task.Delay(Duration);
                    if (NextPhase == -1)
                        owner.Delete();
                    owner.DoPhaseFuncs(caster, NextPhase);
                    AreaTriggerManager.Instance.RemoveAreaTrigger(areaTrigger);
                });
            }

            return false;
        }
    }
}
