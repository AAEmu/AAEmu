using System.Linq;
using AAEmu.Commons.Generics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Geo.Basic;
using AAEmu.Game.Utils;

using Commons.Utils;

/*
   Author:Sagara
*/
namespace AAEmu.Game.Models.Game.AI.Abstracts
{
    public abstract class AVisibleObjectAi : AAi
    {
        private const float MaximalVisibleRange = 1000f;
        private const int RecalculateMinimalDelay = 500;

        private long _lastCalcMs;

        public delegate void VisibleObjectsNotifierEventHandler(GameObject obj);
        public delegate void VisibleObjectsNotifierEventHandler<in T>(T obj) where T : GameObject;

        private readonly LockedList<GameObject> _objectsThatISee = new LockedList<GameObject>();
        private readonly LockedList<GameObject> _objectThatSeeMe = new LockedList<GameObject>();

        private float VisibleRange { get; set; }

        protected AVisibleObjectAi(GameObject owner, float visibleRange) : base(owner)
        {
            VisibleRange = visibleRange;

            _objectsThatISee.ElementAddedEvent += IamSeeSomeone;
            _objectsThatISee.ElementRemovedEvent += IamUnseeSomeone;
            _objectThatSeeMe.ElementAddedEvent += SomeoneSeeMe;
            _objectThatSeeMe.ElementRemovedEvent += SomeoneUnseeMee;
        }

        public void NotifyObjectsThatISee(VisibleObjectsNotifierEventHandler notifier)
        {
            _objectsThatISee.Action(notifier.Invoke);
        }

        public void NotifyObjectsThatSeeMe(VisibleObjectsNotifierEventHandler notifier)
        {
            _objectThatSeeMe.Action(notifier.Invoke);
        }

        public void NotifyObjectsThatSeeMe<T>(VisibleObjectsNotifierEventHandler<T> notifier) where T : GameObject
        {
            _objectThatSeeMe.Action((GameObject element) =>
            {
                var tg = element as T;
                if (tg != null)
                {
                    notifier.Invoke(tg);
                }
            });
        }

        public void OwnerMoved(MovementAction action)
        {
            _objectThatSeeMe.ParallelAction(element => element.VisibleAi.SomeoneThatIamSeeWasMoved(Owner, action));
            _objectsThatISee.ParallelAction(element => element.VisibleAi.SomeoneThatSeeMeWasMoved(Owner, action));

            Calculate();
        }

        public override void Activate()
        {
            Calculate();
        }

        public override void Deactivate()
        {
            _objectThatSeeMe.Action(element =>
            {
                element.VisibleAi._objectsThatISee.Remove(Owner);
                element.VisibleAi._objectThatSeeMe.Remove(Owner);
            });
        }

        protected override void Action()
        {
        }

        private void Calculate()
        {
            // will prevent spam of location packets
            if (ExtDatetime.Utc - _lastCalcMs < RecalculateMinimalDelay) { return; }

            var allCreatures = WorldManager.Instance.GetAround<Unit>(Owner, MaximalVisibleRange);

            //foreach (var aVisibleObject in allCreatures.Where(aVisibleObject => aVisibleObject is Npc || aVisibleObject is Character))
            foreach (var aVisibleObject in allCreatures)
            {
                if (CanSeeObject(aVisibleObject))
                {
                    if (_objectsThatISee.AddIfNotPresent(aVisibleObject))
                    {
                        aVisibleObject.VisibleAi._objectThatSeeMe.AddIfNotPresent(Owner);
                    }
                }
                else if (_objectsThatISee.Remove(aVisibleObject))
                {
                    aVisibleObject.VisibleAi._objectThatSeeMe.Remove(Owner);
                }
                if (aVisibleObject.VisibleAi.CanSeeObject(Owner))
                {
                    if (aVisibleObject.VisibleAi._objectsThatISee.AddIfNotPresent(Owner))
                    {
                        _objectThatSeeMe.AddIfNotPresent(aVisibleObject);
                    }
                }
                else
                {
                    aVisibleObject.VisibleAi._objectsThatISee.Remove(Owner);
                    _objectThatSeeMe.Remove(aVisibleObject);
                }
            }
            _objectsThatISee.ParallelAction(element =>
            {
                if (CanSeeObject(element)) { return; }

                _objectsThatISee.Remove(element);
                element.VisibleAi._objectThatSeeMe.Remove(element);
            });
            _lastCalcMs = ExtDatetime.Utc;
        }

        private bool CanSeeObject(GameObject obj)
        {
            return !obj.Equals(Owner)
                   && MathUtil.Distance(Owner, obj.Position) < VisibleRange
                   && CanSee(obj);
        }

        protected abstract bool CanSee(GameObject target);

        protected abstract void IamSeeSomeone(GameObject someone);

        protected abstract void IamUnseeSomeone(GameObject someone);

        protected abstract void SomeoneSeeMe(GameObject someone);

        protected abstract void SomeoneUnseeMee(GameObject someone);

        protected abstract void SomeoneThatIamSeeWasMoved(GameObject someone, MovementAction action);

        protected abstract void SomeoneThatSeeMeWasMoved(GameObject someone, MovementAction action);
    }
}
