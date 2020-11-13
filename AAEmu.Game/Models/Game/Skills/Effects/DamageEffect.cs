using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Tasks.UnitMove;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class DamageEffect : EffectTemplate
    {
        public DamageType DamageType { get; set; }
        public int FixedMin { get; set; }
        public int FixedMax { get; set; }
        public float Multiplier { get; set; }
        public bool UseMainhandWeapon { get; set; }
        public bool UseOffhandWeapon { get; set; }
        public bool UseRangedWeapon { get; set; }
        public int CriticalBonus { get; set; }
        public uint TargetBuffTagId { get; set; }
        public int TargetBuffBonus { get; set; }
        public bool UseFixedDamage { get; set; }
        public bool UseLevelDamage { get; set; }
        public float LevelMd { get; set; }
        public int LevelVaStart { get; set; }
        public int LevelVaEnd { get; set; }
        public float TargetBuffBonusMul { get; set; }
        public bool UseChargedBuff { get; set; }
        public uint ChargedBuffId { get; set; }
        public float ChargedMul { get; set; }
        public float AggroMultiplier { get; set; }
        public int HealthStealRatio { get; set; }
        public int ManaStealRatio { get; set; }
        public float DpsMultiplier { get; set; }
        public int WeaponSlotId { get; set; }
        public bool CheckCrime { get; set; }
        public uint HitAnimTimingId { get; set; }
        public bool UseTargetChargedBuff { get; set; }
        public uint TargetChargedBuffId { get; set; }
        public float TargetChargedMul { get; set; }
        public float DpsIncMultiplier { get; set; }
        public bool EngageCombat { get; set; }
        public bool Synergy { get; set; }
        public uint ActabilityGroupId { get; set; }
        public int ActabilityStep { get; set; }
        public float ActabilityMul { get; set; }
        public float ActabilityAdd { get; set; }
        public float ChargedLevelMul { get; set; }
        public bool AdjustDamageByHeight { get; set; }
        public bool UsePercentDamage { get; set; }
        public int PercentMin { get; set; }
        public int PercentMax { get; set; }
        public bool UseCurrentHealth { get; set; }
        public int TargetHealthMin { get; set; }
        public int TargetHealthMax { get; set; }
        public float TargetHealthMul { get; set; }
        public int TargetHealthAdd { get; set; }
        public bool FireProc { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("DamageEffect");

            if (!(target is Unit))
            {
                return;
            }

            var trg = (Unit)target;
            var min = 0.0f;
            var max = 0.0f;
            
            if (UseFixedDamage)
            {
                min += FixedMin;
                max += FixedMax;
            }

            // Used for NPCs, I think
            if (UseLevelDamage)
            {
                var lvlMd = caster.LevelDps * LevelMd;
                var levelModifier = ((skill.Level - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;
            
                min += (lvlMd - levelModifier * lvlMd) + 0.5f;
                max += (levelModifier + 1) * lvlMd + 0.5f;
            }
            
            // Stats/Weapon DPS
            if (caster is Character)
            {
                var dpsInc = 0;
                switch (DamageType)
                {
                    case DamageType.Melee:
                        dpsInc = caster.Dps;
                        break;
                    case DamageType.Magic:
                        dpsInc = caster.MDps;
                        break;
                    case DamageType.Ranged:
                        dpsInc = caster.RangedDps;
                        break;
                }

                max = (dpsInc * 0.001f) * DpsIncMultiplier;
                min = 0;

                if (UseMainhandWeapon)
                    min = caster.Dps * 0.001f; // TODO : Use only weapon value!
                if (UseOffhandWeapon)
                    min = (caster.OffhandDps * 0.001f) + min;
                if (UseRangedWeapon)
                    min = (caster.RangedDps * 0.001f) + min; // TODO : Use only weapon value!

                max = (DpsMultiplier * min) + max;
                
                var minCastBonus = 1000f;
                var castTimeMod = skill.Template.CastingTime; // This mod depends on casting_inc too!
                if (castTimeMod <= 1000)
                    minCastBonus = min > 0 ? min : minCastBonus;
                else
                    minCastBonus = castTimeMod;

                max = max * minCastBonus * 0.001f;
            }


            min *= Multiplier;
            max *= Multiplier;

            var damageMultiplier = 0.0f;
            switch (DamageType)
            {
                case DamageType.Melee:
                    // damageMultiplier = caster.Dps???
                    damageMultiplier = 1.0f;
                    break;
                case DamageType.Magic:
                    damageMultiplier = 1.0f;
                    break;
                case DamageType.Ranged:
                    damageMultiplier = 1.0f;
                    break;
                case DamageType.Siege:
                    // TODO 
                    damageMultiplier = 1.0f;
                    break;
            }

            var iVar1 = (int)(min * (damageMultiplier + 1000));
            var uVar3 = iVar1 / 1000 + (iVar1 >> 0x1f);
            min = (uVar3 >> 0x1f) + uVar3;
            iVar1 = (int)(max * (damageMultiplier + 1000));
            uVar3 = iVar1 / 1000 + (iVar1 >> 0x1f);
            max = (uVar3 >> 0x1f) + uVar3;
            
            

            var value = (int)max;
            trg.ReduceCurrentHp(caster, value);
            caster.SummarizeDamage += value;
            
            // TODO : Use proper chance kinds (melee, magic etc.)
            if (trg is Character procTarget)
                procTarget.Procs.RollProcsForKind(ProcChanceKind.TakeDamageAny);
            if (caster is Character procAttacker)
                procAttacker.Procs.RollProcsForKind(ProcChanceKind.HitAny);

            if (packetBuilder != null) 
                packetBuilder.AddPacket(new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, value));
            else
                trg.BroadcastPacket(new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, value), true);
            if (trg is Npc)
            {
                trg.BroadcastPacket(new SCAiAggroPacket(trg.ObjId, 1, caster.ObjId, caster.SummarizeDamage), true);
            }
            if (trg is Npc npc && npc.CurrentTarget != caster)
            {
                npc.OnDamageReceived(caster);
            }
        }
    }
}
