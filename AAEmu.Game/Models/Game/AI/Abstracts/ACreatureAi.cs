using System.Numerics;

using AAEmu.Game.Models.Game.World;

/*
   Author:Sagara
*/
namespace AAEmu.Game.Models.Game.AI.Abstracts
{
    public abstract class ACreatureAi : AVisibleObjectAi
    {
        protected ACreatureAi(GameObject owner, float visibleRange) : base(owner, visibleRange)
        {
        }

        protected sealed override bool CanSee(GameObject target)
        {
            return true;
        }

        protected virtual void Rotate(Vector3 target, int time)
        {

        }
    }
}
