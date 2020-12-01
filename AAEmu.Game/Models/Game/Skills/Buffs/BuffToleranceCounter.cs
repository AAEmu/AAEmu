using System;

namespace AAEmu.Game.Models.Game.Skills.Buffs
{
    public class BuffToleranceCounter
    {
        public BuffTolerance Tolerance { get; set; }
        public BuffToleranceStep CurrentStep { get; set; }
        public DateTime LastStep { get; set; }
    }
}
