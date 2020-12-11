using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.AI.Framework
{
    /// <summary>
    /// Any generic AI
    /// </summary>
    public abstract class AbstractAI
    {
        public GameObject Owner { get; set; }
    }
}
