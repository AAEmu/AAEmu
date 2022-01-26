using System.Collections.Concurrent;

namespace AAEmu.Game.Models.Game.Animation
{
    public class AnimCombatSyncEvent
    {
        public string ModelName { get; set; }
        public ConcurrentDictionary<string, AnimDuration> Animations { get; set; } = new ConcurrentDictionary<string, AnimDuration>();
    }
}
