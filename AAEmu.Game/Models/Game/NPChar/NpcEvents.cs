using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar;

public partial class Npc
{
    public void RegisterNpcEvents()
    {
        var np = NpcGameData.Instance.GetNpSkills(TemplateId);

        if (np is null) { return; }

        // Logger.Info($"Registering Events for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}");

        foreach (var skill in np)
        {
            switch (skill.SkillUseCondition)
            {
                case SkillUseConditionKind.InCombat:
                    Logger.Trace($"Registering OnCombat event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnCombatStarted += OnCombatStarted;
                    break;
                case SkillUseConditionKind.InIdle:
                    Logger.Trace($"Registering InIdle event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.InIdle += InIdle;
                    break;
                case SkillUseConditionKind.OnDeath:
                    Logger.Trace($"Registering OnDeath event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnDeath += OnDeath;
                    //int invocationCount = npc.Events.OnDeath.GetInvocationList().GetLength(0);
                    break;
                case SkillUseConditionKind.InAlert:
                    Logger.Trace($"Registering InAlert event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.InAlert += InAlert;
                    break;
                case SkillUseConditionKind.InDead:
                    Logger.Trace($"Registering InDead event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.InDead += InDead;
                    break;
                case SkillUseConditionKind.OnSpawn:
                    Logger.Trace($"Registering OnSpawn event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnSpawn += OnSpawn;
                    break;
                case SkillUseConditionKind.OnDespawn:
                    Logger.Trace($"Registering OnDespawn event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnDespawn += OnDespawn;
                    break;
                case SkillUseConditionKind.OnAlert:
                    Logger.Trace($"Registering OnAlert event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnAlert += OnAlert;
                    break;
                case SkillUseConditionKind.None:
                default:
                    Logger.Warn($"No SkillUseCondition found to register for skill {skill.SkillId} {skill.SkillUseCondition} for npc {TemplateId}");
                    break;
            }
        }
    }

    public void UnregisterNpcEvents()
    {
        var npSkills = NpcGameData.Instance.GetNpSkills(TemplateId);
        if (npSkills is null) { return; }

        // Logger.Info($"Unregistering Npc Events for  objId: {npc.ObjId}, templateId: {npc.TemplateId}");

        foreach (var skill in npSkills)
        {
            switch (skill.SkillUseCondition)
            {
                case SkillUseConditionKind.InCombat:
                    Logger.Trace($"Unregistering OnCombat event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnCombatStarted -= OnCombatStarted;
                    break;
                case SkillUseConditionKind.InIdle:
                    Logger.Trace($"Unregistering InIdle event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.InIdle -= InIdle;
                    break;
                case SkillUseConditionKind.OnDeath:
                    Logger.Trace($"Unregistering OnDeath event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnDeath -= OnDeath;
                    break;
                case SkillUseConditionKind.InAlert:
                    Logger.Trace($"Unregistering InAlert event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.InAlert -= InAlert;
                    break;
                case SkillUseConditionKind.InDead:
                    Logger.Trace($"Unregistering InDead event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.InDead -= InDead;
                    break;
                case SkillUseConditionKind.OnSpawn:
                    Logger.Trace($"Unregistering OnSpawn event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnSpawn -= OnSpawn;
                    break;
                case SkillUseConditionKind.OnDespawn:
                    Logger.Trace($"Unregistering OnDespawn event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnDespawn -= OnDespawn;
                    break;
                case SkillUseConditionKind.OnAlert:
                    Logger.Trace($"Unregistering OnAlert event for npc objId: {ObjId}, templateId: {TemplateId}, skill {skill.SkillId}");
                    Events.OnAlert -= OnAlert;
                    break;
                case SkillUseConditionKind.None:
                default:
                    Logger.Debug($"No SkillUseCondition found to unregister for skill {skill.SkillId} {skill.SkillUseCondition} for npc {TemplateId}");
                    break;
            }
        }
    }

    private void OnCombatStarted(object sender, OnCombatStartedArgs args)
    {
        if (args.Owner is not Npc npc)
        {
            if (args.Target is not Npc target)
            {
                return;
            }

            npc = target;
        }

        if (npc.IsInBattle)
        {
            return;
        }

        npc.IsInBattle = true;

        var skills = NpcGameData.Instance.GetNpSkills(npc.TemplateId, SkillUseConditionKind.InCombat);
        if (skills == null) { return; }

        Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} has started combat.");

        foreach (var npcSkill in skills)
        {
            var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

            if (skill is null) { continue; }

            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = npc.ObjId;

            var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            skillTarget.ObjId = npc.ObjId;

            if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }

            if (skill.Template.CooldownTime == 0)
            {
                npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // Run once / выполняем один раз
            }

            Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
            skill.Use(npc, skillCaster, skillTarget, null, false, out _);
        }
    }

    private void InIdle(object sender, InIdleArgs args)
    {
        if (args.Owner is not Npc npc) { return; }

        var skills = NpcGameData.Instance.GetNpSkills(npc.TemplateId, SkillUseConditionKind.InIdle);
        if (skills == null) { return; }

        Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} has entered idle state.");

        foreach (var npcSkill in skills)
        {
            var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

            if (skill is null) { continue; }

            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = npc.ObjId;

            var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            skillTarget.ObjId = npc.ObjId;

            if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }

            if (skill.Template.CooldownTime == 0)
            {
                npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
            }

            Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
            skill.Use(npc, skillCaster, skillTarget, null, false, out _);
        }
    }

    private void OnDeath(object sender, OnDeathArgs args)
    {
        if (args.Victim is not Npc npc) { return; }

        var skills = NpcGameData.Instance.GetNpSkills(npc.TemplateId, SkillUseConditionKind.OnDeath);
        if (skills == null) { return; }

        Logger.Trace($"Npc objId={npc.ObjId}, templateId={npc.TemplateId} has died.");

        //UnregisterNpcEvents(npc);
        //UnregisterIndunEvents();

        foreach (var npcSkill in skills)
        {
            var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

            if (skill is null) { continue; }

            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = npc.ObjId;

            var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            skillTarget.ObjId = npc.ObjId;

            if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }

            if (skill.Template.CooldownTime == 0)
            {
                npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
            }

            Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
            skill.Use(npc, skillCaster, skillTarget, null, true, out _);
        }
    }

    private void OnSpawn(object sender, OnSpawnArgs args)
    {
        if (args.Npc is not Npc npc) { return; }

        var skills = NpcGameData.Instance.GetNpSkills(npc.TemplateId, SkillUseConditionKind.OnSpawn);
        if (skills == null) { return; }

        Logger.Trace($"Npc objId: {npc.ObjId}, templateId: {npc.TemplateId} OnSpawn triggered.");

        foreach (var npcSkill in skills)
        {
            var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

            if (skill is null) { continue; }

            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = npc.ObjId;

            var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            skillTarget.ObjId = npc.ObjId;

            if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }

            if (skill.Template.CooldownTime == 0)
            {
                npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
            }

            Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
            skill.Use(npc, skillCaster, skillTarget, null, true, out _);
        }
    }

    private void OnDespawn(object sender, OnDespawnArgs args)
    {
        if (args.Npc is not Npc npc) { return; }

        var skills = NpcGameData.Instance.GetNpSkills(npc.TemplateId, SkillUseConditionKind.OnDespawn);
        if (skills == null) { return; }

        Logger.Trace($"Npc objId: {npc.ObjId}, templateId: {npc.TemplateId} OnDespawn triggered.");

        foreach (var npcSkill in skills)
        {
            var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

            if (skill is null) { continue; }

            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = npc.ObjId;

            var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            skillTarget.ObjId = npc.ObjId;

            if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }

            if (skill.Template.CooldownTime == 0)
            {
                npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
            }

            Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
            skill.Use(npc, skillCaster, skillTarget, null, true, out _);
        }
    }

    private void InAlert(object sender, InAlertArgs args)
    {
        Logger.Trace($"Npc={args.Npc.ObjId}:{args.Npc.TemplateId} is in alert.");
        if (args.Npc is not Npc npc) { return; }

        var skills = NpcGameData.Instance.GetNpSkills(npc.TemplateId, SkillUseConditionKind.InAlert);
        if (skills == null) { return; }

        // Logger.Trace($"Npc objId: {npc.ObjId}, templateId: {npc.TemplateId} OnAlert triggered.");

        foreach (var npcSkill in skills)
        {
            var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

            if (skill is null) { continue; }

            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = npc.ObjId;

            var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            skillTarget.ObjId = npc.CurrentTarget?.ObjId ?? 0;

            if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }

            if (skill.Template.CooldownTime == 0)
            {
                npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
            }

            Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
            skill.Use(npc, skillCaster, skillTarget, null, true, out _);
        }
    }

    private void OnAlert(object sender, OnAlertArgs args)
    {
        if (args.Npc is not Npc npc) { return; }

        var skills = NpcGameData.Instance.GetNpSkills(npc.TemplateId, SkillUseConditionKind.OnAlert);
        if (skills == null) { return; }

        Logger.Trace($"Npc objId: {npc.ObjId}, templateId: {npc.TemplateId} OnAlert triggered.");

        foreach (var npcSkill in skills)
        {
            var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

            if (skill is null) { continue; }

            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = npc.ObjId;

            var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            skillTarget.ObjId = npc.ObjId;

            if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }

            if (skill.Template.CooldownTime == 0)
            {
                npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
            }

            Logger.Trace($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
            skill.Use(npc, skillCaster, skillTarget, null, true, out _);
        }
    }
    
    private void InDead(object sender, InDeadArgs args)
    {
        Logger.Trace($"Npc={args.Npc.ObjId} : {args.Npc.TemplateId} is in death state.");
    }
}
