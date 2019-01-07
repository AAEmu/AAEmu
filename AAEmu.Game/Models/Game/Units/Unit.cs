using System.Collections.Generic;
using AAEmu.Game.Models.Game.Bonuses;

namespace AAEmu.Game.Models.Game.Units
{
    public class Unit : BaseUnit
    {
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

        public Dictionary<uint, List<Bonus>> Bonuses { get; set; }

        public Unit()
        {
            Bonuses = new Dictionary<uint, List<Bonus>>();
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