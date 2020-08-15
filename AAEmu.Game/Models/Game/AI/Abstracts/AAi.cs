using AAEmu.Game.Models.Game.World;

using NLog;

/*
   Author:Sagara
*/
namespace AAEmu.Game.Models.Game.AI.Abstracts
{
    public abstract class AAi
    {
        protected static readonly Logger _log = LogManager.GetCurrentClassLogger();

        protected GameObject Owner { get; set; }

        protected AAi(GameObject owner)
        {
            Owner = owner;
        }

        public abstract void Activate();

        public abstract void Deactivate();

        protected abstract void Action();
    }
}
