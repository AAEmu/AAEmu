
///*
//   Author:Sagara
//*/
//namespace AAEmu.Game.Models.Game.AI.Abstracts
//{
//    public abstract class AVisibleObjectAi : AAi
//    {
//        private const int MaximalVisibleRange = 2500;
//        private const int RecalculateMinimalDelay = 500;

//        private long _lastCalcMs;

//        public delegate void VisibleObjectsNotifierEventHandler(ABdoObject obj);
//        public delegate void VisibleObjectsNotifierEventHandler<in T>(T obj) where T : ABdoObject;

//        private readonly LockedList<ABdoObject> _objectsThatISee = new LockedList<ABdoObject>();
//        private readonly LockedList<ABdoObject> _objectThatSeeMe = new LockedList<ABdoObject>();

//        protected float VisibleRange { get; set; }

//        protected AVisibleObjectAi(ABdoObject owner, float visibleRange) : base(owner)
//        {
//            VisibleRange = visibleRange;

//            _objectsThatISee.ElementAddedEvent += IamSeeSomeone;
//            _objectsThatISee.ElementRemovedEvent += IamUnseeSomeone;
//            _objectThatSeeMe.ElementAddedEvent += SomeoneSeeMe;
//            _objectThatSeeMe.ElementRemovedEvent += SomeoneUnseeMee;
//        }

//        public void NotifyObjectsThatISee(VisibleObjectsNotifierEventHandler notifier)
//        {
//            _objectsThatISee.Action(notifier.Invoke);
//        }

//        public void NotifyObjectsThatSeeMe(VisibleObjectsNotifierEventHandler notifier)
//        {
//            _objectThatSeeMe.Action(notifier.Invoke);
//        }

//        public void NotifyObjectsThatSeeMe<T>(VisibleObjectsNotifierEventHandler<T> notifier) where T : ABdoObject
//        {
//            _objectThatSeeMe.Action((ABdoObject element) =>
//            {
//                var tg = element as T;
//                if (tg != null)
//                    notifier.Invoke(tg);
//            });
//        }

//        public void OwnerMoved(MovementAction action)
//        {
//            _objectThatSeeMe.ParallelAction(element => element.VisibleAi.SomeoneThatIamSeeWasMoved(Owner, action));
//            _objectsThatISee.ParallelAction(element => element.VisibleAi.SomeoneThatSeeMeWasMoved(Owner, action));

//            Calculate();
//        }

//        public override void Activate()
//        {
//            Calculate();
//        }

//        public override void Deactivate()
//        {
//            _objectThatSeeMe.Action(element =>
//            {
//                element.VisibleAi._objectsThatISee.Remove(Owner);
//                element.VisibleAi._objectThatSeeMe.Remove(Owner);
//            });
//        }

//        protected override void Action()
//        {

//        }

//        private void Calculate()
//        {
//            //will prevent spam of location packets
//            if (ExtDatetime.Utc - _lastCalcMs < RecalculateMinimalDelay)
//                return;

//            var allCreatures = Owner.Area.GetObjectsInRange(Owner.Position, MaximalVisibleRange);

//            for (int i = 0; i < allCreatures.Count; i++)
//            {
//                var aVisibleObject = allCreatures[i];
//                if (CanSeeObject(aVisibleObject))
//                {
//                    if (_objectsThatISee.AddIfNotPresent(aVisibleObject))
//                        aVisibleObject.VisibleAi._objectThatSeeMe.AddIfNotPresent(Owner);
//                }
//                else if (_objectsThatISee.Remove(aVisibleObject))
//                    aVisibleObject.VisibleAi._objectThatSeeMe.Remove(Owner);

//                if (aVisibleObject.VisibleAi.CanSeeObject(Owner))
//                {
//                    if (aVisibleObject.VisibleAi._objectsThatISee.AddIfNotPresent(Owner))
//                        _objectThatSeeMe.AddIfNotPresent(aVisibleObject);
//                }
//                else
//                {
//                    aVisibleObject.VisibleAi._objectsThatISee.Remove(Owner);
//                    _objectThatSeeMe.Remove(aVisibleObject);
//                }
//            }

//            _objectsThatISee.ParallelAction(element =>
//            {
//                if (!CanSeeObject(element))
//                {
//                    _objectsThatISee.Remove(element);
//                    element.VisibleAi._objectThatSeeMe.Remove(element);
//                }
//            });

//            _lastCalcMs = ExtDatetime.Utc;
//        }

//        private bool CanSeeObject(ABdoObject obj)
//        {
//            return !obj.Equals(Owner) && Owner.Area == obj.Area && Owner.Position.Distance(obj.Position) < VisibleRange && CanSee(obj);
//        }

//        protected abstract bool CanSee(ABdoObject target);

//        protected abstract void IamSeeSomeone(ABdoObject someone);

//        protected abstract void IamUnseeSomeone(ABdoObject someone);

//        protected abstract void SomeoneSeeMe(ABdoObject someone);

//        protected abstract void SomeoneUnseeMee(ABdoObject someone);

//        protected abstract void SomeoneThatIamSeeWasMoved(ABdoObject someone, MovementAction action);

//        protected abstract void SomeoneThatSeeMeWasMoved(ABdoObject someone, MovementAction action);
//    }
//}
