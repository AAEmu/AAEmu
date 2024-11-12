using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

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

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        if (caster is Character)
            Logger.Debug("DoodadFuncClout : Duration {0}, Tick {1}, TargetRelationId {2}, BuffId {3}, ProjectileId {4}, ShowToFriendlyOnly {5}, NextPhase {6}, AoeShapeId {7}, TargetBuffTagId {8}, TargetNoBuffTagId {9}, UseOriginSource {10}", Duration, Tick, TargetRelation, BuffId, ProjectileId, ShowToFriendlyOnly, NextPhase, AoeShapeId, TargetBuffTagId, TargetNoBuffTagId, UseOriginSource);
        else
            Logger.Trace("DoodadFuncClout : Duration {0}, Tick {1}, TargetRelationId {2}, BuffId {3}, ProjectileId {4}, ShowToFriendlyOnly {5}, NextPhase {6}, AoeShapeId {7}, TargetBuffTagId {8}, TargetNoBuffTagId {9}, UseOriginSource {10}", Duration, Tick, TargetRelation, BuffId, ProjectileId, ShowToFriendlyOnly, NextPhase, AoeShapeId, TargetBuffTagId, TargetNoBuffTagId, UseOriginSource);

        var areaTrigger = new AreaTrigger();
        areaTrigger.Shape = WorldManager.Instance.GetAreaShapeById(AoeShapeId);

        if (UseOriginSource)
        {
            var doodads = WorldManager.GetAround<Doodad>(caster, areaTrigger.Shape.Value1, false);
            foreach (var d in doodads)
            {
                areaTrigger.Owner = d; // нам главное, чтобы рядом был doodad от которого будет искаться на кого наложить бафф
                break;
            }
            areaTrigger.Owner ??= owner;
        }
        else
        {
            areaTrigger.Owner = owner;
        }
        areaTrigger.Caster = caster as Unit;
        areaTrigger.InsideBuffTemplate = SkillManager.Instance.GetBuffTemplate(BuffId);
        areaTrigger.TargetRelation = TargetRelation;
        areaTrigger.TickRate = Tick;
        areaTrigger.EffectPerTick = Effects.Select(eid => SkillManager.Instance.GetEffectTemplate(eid)).ToList(); //SkillId = skillId

        AreaTriggerManager.Instance.AddAreaTrigger(areaTrigger);

        if (Duration > 0)
        {
            // TODO : Add a proper delay in here
            // Отменяем текущую задачу, если она существует
            // Cancel the current task if it exists
            if (owner.FuncTask != null)
            {
                try
                {
                    TaskManager.Instance.Cancel(owner.FuncTask);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to cancel existing FuncTask: {0}", ex.Message);
                }
            }

            // Создаем и назначаем новую задачу
            // Create and assign a new task
            owner.FuncTask = new DoodadFuncCloutTask(caster, owner, 0, NextPhase, areaTrigger);
            TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Duration));
        }
        //owner.OverridePhase = NextPhase; // Since phases trigger all at once let the doodad know its okay to stop here if the roll succeeded

        return false;
    }
}
