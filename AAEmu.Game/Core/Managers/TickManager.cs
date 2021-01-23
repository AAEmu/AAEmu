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
        private bool DoTickLoop = true;
        private Thread TickThread;

        private void TickLoop()
        {
            var sw = new Stopwatch();
            sw.Start();
            while(DoTickLoop)
            {
                OnTick.Invoke();
                Thread.Sleep(20);
            }
            sw.Stop();
        }

        public void Initialize()
        {
            TickThread = new Thread(TickLoop);
            TickThread.Start();
        }

        public void Stop()
        {
            DoTickLoop = false;
        }
    }

    public class TickEventEntity
    {
        public TickEventHandler.OnTickEvent Event { get; }
        public TimeSpan LastExecution {get; set;}
        public TimeSpan TickRate { get; }

        public TickEventEntity(TickEventHandler.OnTickEvent ev, TimeSpan tickRate)
        {
            Event = ev;
            TickRate = tickRate;
        }
    }
    public class TickEventHandler
    {
        public delegate void OnTickEvent(TimeSpan delta);
        private List<TickEventEntity> _eventList;
        private Queue<TickEventEntity> _eventsToAdd;
        private Queue<OnTickEvent> _eventsToRemove;
        private Stopwatch _sw;
        private object _lock = new object();

        public TickEventHandler()
        {
            _eventList = new List<TickEventEntity>();
            _eventsToAdd = new Queue<TickEventEntity>();
            _eventsToRemove = new Queue<OnTickEvent>();
            _sw = new Stopwatch();
            _sw.Start();
        }

        public void Invoke()
        {
            lock (_lock)
            {
                while (_eventsToAdd.Count > 0)
                {
                    var ev = _eventsToAdd.Dequeue();
                    _eventList.Add(ev);
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
                if (delta > ev.TickRate)
                {
                    ev.LastExecution = _sw.Elapsed;
                    ev.Event(delta);
                }
            }
        }

        public void Subscribe(OnTickEvent tickEvent, TimeSpan tickRate = default)
        {
            lock (_lock)
            {
                _eventsToAdd.Enqueue(new TickEventEntity(tickEvent, tickRate));
            }
        }

        public void UnSubscribe(OnTickEvent tickEvent)
        {
            lock (_lock)
            {
                _eventsToRemove.Enqueue(tickEvent);
            }
        }

        public static TickEventHandler operator +(TickEventHandler handler, OnTickEvent tickEvent)
        {
            handler.Subscribe(tickEvent);
            return handler;
        }

        public static TickEventHandler operator -(TickEventHandler handler, OnTickEvent tickEvent)
        {
            handler.UnSubscribe(tickEvent);
            return handler;
        }
    }
}
