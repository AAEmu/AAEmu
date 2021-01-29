using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units
{
    public class Unit : BaseUnit
    {
        public virtual UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.None;

        public UnitEvents Events { get; }
        private Task _regenTask;
        public uint ModelId { get; set; }

        public override float ModelSize
        {
            get
            {
                return ModelManager.Instance.GetActorModel(ModelId)?.Radius ?? 0 * Scale;
            }
        }

        public byte Level { get; set; }
        public int Hp { get; set; }
        [UnitAttribute(UnitAttribute.GlobalCooldownMul)]
        public virtual float GlobalCooldownMul { get; set; } = 100f;
        [UnitAttribute(UnitAttribute.MaxHealth)]
        public virtual int MaxHp { get; set; }
        [UnitAttribute(UnitAttribute.HealthRegen)]
        public virtual int HpRegen { get; set; }
        [UnitAttribute(UnitAttribute.PersistentHealthRegen)]
        public virtual int PersistentHpRegen { get; set; } = 30;
        public int Mp { get; set; }
        [UnitAttribute(UnitAttribute.MaxMana)]
        public virtual int MaxMp { get; set; }
        [UnitAttribute(UnitAttribute.ManaRegen)]
        public virtual int MpRegen { get; set; }
        [UnitAttribute(UnitAttribute.PersistentManaRegen)]
        public virtual int PersistentMpRegen { get; set; } = 30;
        [UnitAttribute(UnitAttribute.CastingTimeMul)]
        public virtual float CastTimeMul { get; set; } = 1f;
        public virtual float LevelDps { get; set; }
        [UnitAttribute(UnitAttribute.MainhandDps)]
        public virtual int Dps { get; set; }
        [UnitAttribute(UnitAttribute.MeleeDpsInc)]
        public virtual int DpsInc { get; set; }
        [UnitAttribute(UnitAttribute.OffhandDps)]
        public virtual int OffhandDps { get; set; }
        [UnitAttribute(UnitAttribute.RangedDps)]
        public virtual int RangedDps { get; set; }
        [UnitAttribute(UnitAttribute.RangedDpsInc)]
        public virtual int RangedDpsInc { get; set; }
        [UnitAttribute(UnitAttribute.SpellDps)]
        public virtual int MDps { get; set; }
        [UnitAttribute(UnitAttribute.SpellDpsInc)]
        public virtual int MDpsInc { get; set; }
        [UnitAttribute(UnitAttribute.HealDps)]
        public virtual int HDps { get; set; }
        [UnitAttribute(UnitAttribute.HealDpsInc)]
        public virtual int HDpsInc { get; set; }
        [UnitAttribute(UnitAttribute.MeleeAntiMissMul)]
        public virtual float MeleeAccuracy { get; set; } = 100f;
        [UnitAttribute(UnitAttribute.MeleeCritical)]
        public virtual float MeleeCritical { get; set; }
        [UnitAttribute(UnitAttribute.MeleeCriticalBonus)]
        public virtual float MeleeCriticalBonus { get; set; }
        [UnitAttribute(UnitAttribute.MeleeCriticalMul)]
        public virtual float MeleeCriticalMul { get; set; } = 1f;
        [UnitAttribute(UnitAttribute.RangedAntiMiss)]
        public virtual float RangedAccuracy { get; set; } = 100f;
        [UnitAttribute(UnitAttribute.RangedCritical)]
        public virtual float RangedCritical { get; set; }
        [UnitAttribute(UnitAttribute.RangedCriticalBonus)]
        public virtual float RangedCriticalBonus { get; set; }
        [UnitAttribute(UnitAttribute.RangedCriticalMul)]
        public virtual float RangedCriticalMul { get; set; } = 1f;
        [UnitAttribute(UnitAttribute.SpellAntiMiss)]
        public virtual float SpellAccuracy { get; set; } = 100f;
        [UnitAttribute(UnitAttribute.SpellCritical)]
        public virtual float SpellCritical { get; set; }
        [UnitAttribute(UnitAttribute.SpellCriticalBonus)]
        public virtual float SpellCriticalBonus { get; set; }
        [UnitAttribute(UnitAttribute.SpellCriticalMul)]
        public virtual float SpellCriticalMul { get; set; } = 1f;
        [UnitAttribute(UnitAttribute.HealCritical)]
        public virtual float HealCritical { get; set; }
        [UnitAttribute(UnitAttribute.HealCriticalBonus)]
        public virtual float HealCriticalBonus { get; set; }
        [UnitAttribute(UnitAttribute.HealCriticalMul)]
        public virtual float HealCriticalMul { get; set; }
        [UnitAttribute(UnitAttribute.Armor)]
        public virtual int Armor { get; set; }
        [UnitAttribute(UnitAttribute.MagicResist)]
        public virtual int MagicResistance { get; set; }
        [UnitAttribute(UnitAttribute.IgnoreArmor)]
        public virtual int DefensePenetration { get; set; }
        [UnitAttribute(UnitAttribute.MagicPenetration)]
        public virtual int MagicPenetration { get; set; }
        [UnitAttribute(UnitAttribute.Dodge)]
        public virtual float DodgeRate { get; set; }
        [UnitAttribute(UnitAttribute.MeleeParry)]
        public virtual float MeleeParryRate { get; set; }
        [UnitAttribute(UnitAttribute.RangedParry)]
        public virtual float RangedParryRate { get; set; }
        [UnitAttribute(UnitAttribute.Block)]
        public virtual float BlockRate { get; set; }
        [UnitAttribute(UnitAttribute.BattleResist)]
        public virtual int BattleResist { get; set; }
        [UnitAttribute(UnitAttribute.BullsEye)]
        public virtual int BullsEye { get; set; }
        [UnitAttribute(UnitAttribute.Flexibility)]
        public virtual int Flexibility { get; set; }
        [UnitAttribute(UnitAttribute.Facets)]
        public virtual int Facets { get; set; }
        [UnitAttribute(UnitAttribute.MeleeDamageMul)]
        public virtual float MeleeDamageMul { get; set; }
        [UnitAttribute(UnitAttribute.RangedDamageMul)]
        public virtual float RangedDamageMul { get; set; }
        [UnitAttribute(UnitAttribute.SpellDamageMul)]
        public virtual float SpellDamageMul { get; set; }
        [UnitAttribute(UnitAttribute.IncomingHealMul)]
        public virtual float IncomingHealMul { get; set; }
        [UnitAttribute(UnitAttribute.HealMul)]
        public virtual float HealMul { get; set; }
        [UnitAttribute(UnitAttribute.IncomingDamageMul)]
        public virtual float IncomingDamageMul { get; set; } = 1f;
        [UnitAttribute(UnitAttribute.IncomingMeleeDamageMul)]
        public virtual float IncomingMeleeDamageMul { get; set; } = 1f;
        [UnitAttribute(UnitAttribute.IncomingRangedDamageMul)]
        public virtual float IncomingRangedDamageMul { get; set; } = 1f;
        [UnitAttribute(UnitAttribute.IncomingSpellDamageMul)]
        public virtual float IncomingSpellDamageMul { get; set; } = 1f;
        public BaseUnit CurrentTarget { get; set; }
        public virtual byte RaceGender => 0;
        public virtual UnitCustomModelParams ModelParams { get; set; }
        public byte ActiveWeapon { get; set; }
        public bool IdleStatus { get; set; }
        public bool ForceAttack { get; set; }
        public bool Invisible { get; set; }
        public uint OwnerId { get; set; }
        public SkillTask SkillTask { get; set; }
        public SkillTask AutoAttackTask { get; set; }
        public DateTime GlobalCooldown { get; set; }
        public object GCDLock { get; set; }
        public DateTime SkillLastUsed { get; set; }
        public PlotState ActivePlotState { get; set; }
        public Dictionary<uint, List<Bonus>> Bonuses { get; set; }
        public Expedition Expedition { get; set; }
        public bool IsInBattle { get; set; }
        public bool IsInPatrol { get; set; } // so as not to run the route a second time
        public int SummarizeDamage { get; set; }
        public bool IsAutoAttack = false;
        public uint SkillId;
        public ushort TlId { get; set; }
        public ItemContainer Equipment { get; set; }
        public GameConnection Connection { get; set; }

        /// <summary>
        /// Unit巡逻
        /// Unit patrol
        /// 指明Unit巡逻路线及速度、是否正在执行巡逻等行为
        /// Indicates the route and speed of the Unit patrol, whether it is performing patrols, etc.
        /// </summary>
        public Patrol Patrol { get; set; }
        public Simulation Simulation { get; set; }
        
        public UnitProcs Procs { get; set; }
        public object ChargeLock { get; set; }

        public Unit()
        {
            Events = new UnitEvents();
            GCDLock = new object();
            Bonuses = new Dictionary<uint, List<Bonus>>();
            IsInBattle = false;
            Equipment = new ItemContainer(null, SlotType.Equipment, true);
            Equipment.ContainerSize = 28;
            ChargeLock = new object();
        }

        public virtual void SetPosition(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
        {
            var moved = !Position.X.Equals(x) || !Position.Y.Equals(y) || !Position.Z.Equals(z);
            if (moved)
            {
                Events.OnMovement(this, new OnMovementArgs());
            }
            base.SetPosition(x, y, z, rotationX, rotationY, rotationZ);
        }

        public virtual void ReduceCurrentHp(Unit attacker, int value)
        {
            if (Hp <= 0)
                return;

            var absorptionEffects = Buffs.GetAbsorptionEffects().ToList();
            if (absorptionEffects.Count > 0)
            {
                // Handle damage absorb
                foreach (var absorptionEffect in absorptionEffects)
                {
                    value = absorptionEffect.ConsumeCharge(value);
                }
            }
            
            Hp = Math.Max(Hp - value, 0);
            if (Hp <= 0)
            {
                attacker.Events.OnKill(attacker, new OnKillArgs { died = this });
                DoDie(attacker);
                //StopRegen();
            }
            else
            {
                //StartRegen();
            }
            BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Hp > 0 ? Mp : 0), true);
        }
        
        public virtual void ReduceCurrentMp(Unit unit, int value)
        {
            if (Hp == 0)
                return;
            
            Mp = Math.Max(Mp - value, 0);
            if (Mp == 0)
                StopRegen();
            else
                StartRegen();
            BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp), true);
        }

        public virtual void DoDie(Unit killer)
        {
            InterruptSkills();

            Events.OnDeath(this, new OnDeathArgs { });
            Buffs.RemoveEffectsOnDeath();

            killer.BroadcastPacket(new SCUnitDeathPacket(ObjId, 1, killer), true);
            if (killer == this)
                return;

            var lootDropItems = ItemManager.Instance.CreateLootDropItems(ObjId);
            if (lootDropItems.Count > 0)
            {
                killer.BroadcastPacket(new SCLootableStatePacket(ObjId, true), true);
            }

            if (CurrentTarget != null)
            {
                killer.BroadcastPacket(new SCAiAggroPacket(killer.ObjId, 0), true);
                killer.SummarizeDamage = 0;

                if (killer.CurrentTarget != null)
                {
                    killer.BroadcastPacket(new SCCombatClearedPacket(killer.CurrentTarget.ObjId), true);
                }
                killer.BroadcastPacket(new SCCombatClearedPacket(killer.ObjId), true);
                killer.StartRegen();
                killer.BroadcastPacket(new SCTargetChangedPacket(killer.ObjId, 0), true);

                if (killer is Character character)
                {
                    character.StopAutoSkill(character);
                    character.IsInBattle = false; // we need the character to be "not in battle"
                }
                else if (killer.CurrentTarget is Character character2)
                {
                    character2.StopAutoSkill(character2);
                    character2.IsInBattle = false; // we need the character to be "not in battle"
                    character2.DeadTime = DateTime.Now;
                }

                killer.CurrentTarget = null;
            }
        }

        private async void StopAutoSkill(Unit character)
        {
            if (!(character is Character) || character.AutoAttackTask == null)
            {
                return;
            }

            await character.AutoAttackTask.Cancel();
            character.AutoAttackTask = null;
            character.IsAutoAttack = false; // turned off auto attack
            character.BroadcastPacket(new SCSkillEndedPacket(character.TlId), true);
            character.BroadcastPacket(new SCSkillStoppedPacket(character.ObjId, character.SkillId), true);
            TlIdManager.Instance.ReleaseId(character.TlId);
        }

        public void StartRegen()
        {
            // if (_regenTask != null || Hp >= MaxHp && Mp >= MaxMp || Hp == 0)
            // {
            //     return;
            // }
            // _regenTask = new UnitPointsRegenTask(this);
            // TaskManager.Instance.Schedule(_regenTask, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public async void StopRegen()
        {
            if (_regenTask == null)
            {
                return;
            }
            await _regenTask.Cancel();
            _regenTask = null;
        }

        public void SetInvisible(bool value)
        {
            Invisible = value;
            BroadcastPacket(new SCUnitInvisiblePacket(ObjId, Invisible), true);
        }

        public void SetCriminalState(bool criminalState)
        {
            if (criminalState)
            {
                var buff = SkillManager.Instance.GetBuffTemplate((uint) BuffConstants.RETRIBUTION_BUFF);
                var casterObj = new SkillCasterUnit(ObjId);
                Buffs.AddBuff(new Buff(this, this, casterObj, buff, null, DateTime.Now));
            }
            else
            {
                Buffs.RemoveBuff((uint) BuffConstants.RETRIBUTION_BUFF);
            }
        }

        public void SetForceAttack(bool value)
        {
            ForceAttack = value;
            if (ForceAttack)
            {
                var buff = SkillManager.Instance.GetBuffTemplate((uint) BuffConstants.BLOODLUST_BUFF);
                var casterObj = new SkillCasterUnit(ObjId);
                Buffs.AddBuff(new Buff(this, this, casterObj, buff, null, DateTime.Now));
            }
            else
            {
                Buffs.RemoveBuff((uint) BuffConstants.BLOODLUST_BUFF);
            }
            BroadcastPacket(new SCForceAttackSetPacket(ObjId, ForceAttack), true);
        }

        public override void AddBonus(uint bonusIndex, Bonus bonus)
        {
            var bonuses = Bonuses.ContainsKey(bonusIndex) ? Bonuses[bonusIndex] : new List<Bonus>();
            bonuses.Add(bonus);
            Bonuses[bonusIndex] = bonuses;
        }

        public override void RemoveBonus(uint bonusIndex, UnitAttribute attribute)
        {
            if (!Bonuses.ContainsKey(bonusIndex))
            {
                return;
            }
            var bonuses = Bonuses[bonusIndex];
            foreach (var bonus in new List<Bonus>(bonuses))
            {
                if (bonus.Template != null && bonus.Template.Attribute == attribute)
                {
                    bonuses.Remove(bonus);
                }
            }
        }

        public List<Bonus> GetBonuses(UnitAttribute attribute)
        {
            var result = new List<Bonus>();
            if (Bonuses == null)
            {
                return result;
            }
            foreach (var bonuses in new List<List<Bonus>>(Bonuses.Values))
            {
                foreach (var bonus in new List<Bonus>(bonuses))
                {
                    if (bonus.Template != null && bonus.Template.Attribute == attribute)
                    {
                        result.Add(bonus);
                    }
                }
            }
            return result;
        }

        protected double CalculateWithBonuses(double value, UnitAttribute attr)
        {
            foreach (var bonus in GetBonuses(attr))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    value += (value * bonus.Value / 100f);
                else
                    value += bonus.Value;
            }
            return value;
        }

        public void SendPacket(GamePacket packet)
        {
            Connection?.SendPacket(packet);
        }

        public void SendErrorMessage(ErrorMessageType type)
        {
            SendPacket(new SCErrorMsgPacket(type, 0, true));
        }
        
        public float GetDistanceTo(BaseUnit baseUnit, bool includeZAxis = false)
        {
            if (Position == baseUnit.Position)
                return 0.0f;
            
            var rawDist = MathUtil.CalculateDistance(this.Position, baseUnit.Position, includeZAxis);

            rawDist -= ModelManager.Instance.GetActorModel(ModelId)?.Radius ?? 0 * Scale;
            if (baseUnit is Unit unit)
                rawDist -= ModelManager.Instance.GetActorModel(unit.ModelId)?.Radius ?? 0 * unit.Scale;
            
            return Math.Max(rawDist, 0);
        }

        public virtual int GetAbLevel(AbilityType type)
        {
            return Level;
        }

        public string GetAttribute(UnitAttribute attr)
        {
            var props = this.GetType().GetProperties()
                .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                    .Any(a => a.Attributes.Contains(attr)));

            if (props.Count() > 0)
                return props.ElementAt(0).GetValue(this).ToString();
            else
                return "NotFound";
        }

        public string GetAttribute(uint attr) => GetAttribute((UnitAttribute)attr);

        //Uncomment if you need this
        /*
        public string GetAttribute(string attr)
        {
            if (Enum.TryParse(typeof(UnitAttribute), attr, true, out var result))
            {
                return GetAttribute((UnitAttribute)result);
            }
            return "FailedParse";
        }
        */

        public override void InterruptSkills()
        {
            ActivePlotState?.RequestCancellation();
            if (SkillTask == null)
                return;
            switch (SkillTask)
            {
                case EndChannelingTask ect:
                    ect.Skill.Stop(this, ect._channelDoodad);
                    break;
                default:
                    SkillTask.Skill.Stop(this);
                    break;
            }
        }

        public bool IsDead
        {
            get
            {
                return Hp <= 0;
            }
        }

        public bool NeedsRegen
        {
            get
            {
                return Hp < MaxHp || Mp < MaxMp;
            }
        }
    }
}
