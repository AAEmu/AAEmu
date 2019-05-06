using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Units
{
    public class Unit : BaseUnit
    {
        private Task _regenTask;

        public uint ModelId { get; set; }
        public byte Level { get; set; }
        public int Hp { get; set; }
        public virtual int MaxHp { get; set; }
        public virtual int HpRegen { get; set; }
        public virtual int PersistentHpRegen { get; set; }
        public int Mp { get; set; }
        public virtual int MaxMp { get; set; }
        public virtual int MpRegen { get; set; }
        public virtual int PersistentMpRegen { get; set; }
        public virtual float LevelDps { get; set; }
        public virtual int Dps { get; set; }
        public virtual int DpsInc { get; set; }
        public virtual int OffhandDps { get; set; }
        public virtual int RangedDps { get; set; }
        public virtual int RangedDpsInc { get; set; }
        public virtual int MDps { get; set; }
        public virtual int MDpsInc { get; set; }
        public virtual int Armor { get; set; }
        public virtual int MagicResistance { get; set; }
        public BaseUnit CurrentTarget { get; set; }
        public virtual byte RaceGender => 0;
        public virtual UnitCustomModelParams ModelParams { get; set; }
        public byte ActiveWeapon { get; set; }
        public bool IdleStatus { get; set; }
        public bool ForceAttack { get; set; }
        public bool Invisible { get; set; }

        public uint OwnerId { get; set; }
        public SkillTask SkillTask { get; set; }
        public Dictionary<uint, List<Bonus>> Bonuses { get; set; }
        public Expedition Expedition { get; set; }

        /// <summary>
        /// Unit巡逻
        /// 指明Unit巡逻路线及速度、是否正在执行巡逻等行为
        /// </summary>
        public Patrol Patrol { get; set; }

        public Unit()
        {
            Bonuses = new Dictionary<uint, List<Bonus>>();
        }

        public virtual void ReduceCurrentHp(Unit attacker, int value)
        {
            if (Hp == 0)
                return;
            Hp = Math.Max(Hp - value, 0);
            if (Hp == 0) {
                DoDie(attacker);
                //StopRegen();
            } //else
                //StartRegen();
            BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Hp>0?Mp:0), true);
        }

        public virtual void DoDie(Unit killer)
        {
            Effects.RemoveEffectsOnDeath();
            BroadcastPacket(new SCUnitDeathPacket(ObjId, 1, killer), true);
            var lootDropItems = ItemManager.Instance.CreateLootDropItems(ObjId);
            if (lootDropItems.Count > 0) { 
                BroadcastPacket(new SCLootableStatePacket(ObjId, true), true);
            }
            if (CurrentTarget!=null)
                BroadcastPacket(new SCCombatClearedPacket(CurrentTarget.ObjId), true);
        }

        public void StartRegen()
        {
            if (_regenTask != null || (Hp >= MaxHp && Mp >= MaxMp) || Hp == 0)
                return;
            _regenTask = new UnitPointsRegenTask(this);
            TaskManager.Instance.Schedule(_regenTask, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public async void StopRegen()
        {
            if(_regenTask == null)
                return;
            await _regenTask.Cancel();
            _regenTask = null;
        }

        public void SetInvisible(bool value)
        {
            Invisible = value;
            BroadcastPacket(new SCUnitInvisiblePacket(ObjId, Invisible), true);
        }

        public void SetForceAttack(bool value)
        {
            ForceAttack = value;
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
                return;
            var bonuses = Bonuses[bonusIndex];
            foreach (var bonus in new List<Bonus>(bonuses))
                if (bonus.Template != null && bonus.Template.Attribute == attribute)
                    bonuses.Remove(bonus);
        }

        public List<Bonus> GetBonuses(UnitAttribute attribute)
        {
            var result = new List<Bonus>();
            if (Bonuses == null)
                return result;
            foreach (var bonuses in new List<List<Bonus>>(Bonuses.Values))
            foreach (var bonus in new List<Bonus>(bonuses))
                if (bonus.Template != null && bonus.Template.Attribute == attribute)
                    result.Add(bonus);
            return result;
        }
    }
}
