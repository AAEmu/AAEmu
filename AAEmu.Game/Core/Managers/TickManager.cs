using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using AAEmu.Commons.Utils;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class TickManager : Singleton<TickManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public delegate void OnTickEvent(TimeSpan delta);
        public TickEventHandler OnTick = new TickEventHandler();

        private void TickLoop()
        {
            var sw = new Stopwatch();
            sw.Start();
            while(true)
            {
                OnTick.Invoke();
                Thread.Sleep(200);
            }
        }

        public void Initialize()
        {
            var TickThread = new Thread(TickLoop);
            TickThread.Start();
        }
    }

    public class TickEventEntity
    {
        public TickEventHandler.OnTickEvent Event;
        public TimeSpan LastExecution;

        public TickEventEntity(TickEventHandler.OnTickEvent ev)
        {
            Event = ev;
        }
    }
    public class TickEventHandler
    {
        public delegate void OnTickEvent(TimeSpan delta);
        private List<TickEventEntity> _eventList;
        private Queue<OnTickEvent> _eventsToAdd;
        private Queue<OnTickEvent> _eventsToRemove;
        private Stopwatch _sw;

        public TickEventHandler()
        {
            _eventList = new List<TickEventEntity>();
            _eventsToAdd = new Queue<OnTickEvent>();
            _eventsToRemove = new Queue<OnTickEvent>();
            _sw = new Stopwatch();
            _sw.Start();
        }

        public void Invoke()
        {
            lock (_eventsToAdd)
            {
                while (_eventsToAdd.Count > 0)
                {
                    var ev = _eventsToAdd.Dequeue();
                    _eventList.Add(new TickEventEntity(ev));
                }
                while (_eventsToRemove.Count > 0)
                {
                    var ev = _eventsToRemove.Dequeue();
                    var evToRemove = _eventList.FirstOrDefault(o => o.Event.GetHashCode() == ev.GetHashCode());
                    if (evToRemove.Event != null)
                        _eventList.Remove(evToRemove);
                }
            }

            foreach (var ev in _eventList)
            {
                var delta = _sw.Elapsed - ev.LastExecution;
                ev.LastExecution = _sw.Elapsed;
                ev.Event(delta);
            }
        }

        public static TickEventHandler operator +(TickEventHandler handler, OnTickEvent tickEvent)
        {
            lock (handler._eventsToAdd)
            {
                handler._eventsToAdd.Enqueue(tickEvent);
            }
            return handler;
        }

        public static TickEventHandler operator -(TickEventHandler handler, OnTickEvent tickEvent)
        {
            lock (handler._eventsToAdd)
            {
                handler._eventsToRemove.Enqueue(tickEvent);
            }
            return handler;
        }
    }
}
