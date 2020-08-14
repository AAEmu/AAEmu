using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.AI.Abstracts
{
    public abstract class AAi
    {
        protected static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public Unit Owner { get; private set; }

        protected AAi(Unit owner)
        {
            Owner = owner;
        }

        public abstract void Activate();

        public abstract void Deactivate();

        protected abstract void Action();
    }
}
