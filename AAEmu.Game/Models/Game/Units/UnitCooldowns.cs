using System;
using System.Collections.Concurrent;

namespace AAEmu.Game.Models.Game.Units
{
    public class UnitCooldowns
    {
        public ConcurrentDictionary<uint, DateTime> Cooldowns { get; set; }

        public UnitCooldowns()
        {
            Cooldowns = new ConcurrentDictionary<uint, DateTime>();
        }

        public void AddCooldown(uint skillId, uint duration)
        {
            if (!Cooldowns.ContainsKey(skillId))
                Cooldowns.TryAdd(skillId, DateTime.UtcNow + TimeSpan.FromMilliseconds(duration)); 
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
            Cooldowns.TryRemove(skillId, out _);
        }
    }
}
