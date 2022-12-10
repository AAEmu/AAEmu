using System;
using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Units
{
    public class UnitCooldowns
    {
        public Dictionary<uint, DateTime> Cooldowns { get; set; }

        public UnitCooldowns()
        {
            Cooldowns = new Dictionary<uint, DateTime>();
        }

        public void AddCooldown(uint skillId, uint duration)
        {
            if (!Cooldowns.ContainsKey(skillId))
                Cooldowns.Add(skillId, DateTime.UtcNow + TimeSpan.FromMilliseconds(duration)); 
        }

        public bool CheckCooldown(uint skillId)
        {
            if (!Cooldowns.ContainsKey(skillId))
                return false;

            var endTime = Cooldowns[skillId];
            if (DateTime.UtcNow < endTime)
                return true;

            RemoveCooldown(skillId);
            return false;
        }

        public void RemoveCooldown(uint skillId)
        {
            Cooldowns.Remove(skillId);
        }
    }
}
