using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
                var before = sw.Elapsed;
                OnTick.Invoke();
                var time = sw.Elapsed - before;
                if(time > TimeSpan.FromMilliseconds(100))
                    _log.Warn("Tick took {0}ms to finish", time.TotalMilliseconds);
                Thread.Sleep(20);
            }
            sw.Stop();
        }

        public void Initialize()
        {
            TickThread = new Thread(() => TickLoop());
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
        public Task ActiveTask { get; set; }
        public bool UseAsync { get; }

        public TickEventEntity(TickEventHandler.OnTickEvent ev, TimeSpan tickRate, bool useAsync)
        {
            Event = ev;
            TickRate = tickRate;
            UseAsync = useAsync;
        }
    }
    public class TickEventHandler
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

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
                    var evToRemove = _eventList.FirstOrDefault(o => o.Event == ev);
                    if (evToRemove?.Event != null)
                        _eventList.Remove(evToRemove);
                }
            }

            foreach (var ev in _eventList)
            {
                var delta = ev.LastExecution != default ? _sw.Elapsed - ev.LastExecution : ev.TickRate.Add(TimeSpan.FromMilliseconds(1));
                if (delta > ev.TickRate)
                {
                    if(ev.UseAsync)
                    {
                        if (ev.ActiveTask == null || ev.ActiveTask.IsCompleted)
                        {
                            ev.LastExecution = _sw.Elapsed;
                            ev.ActiveTask = Task.Run(() => {
                                try
                                {
                                    ev.Event(delta);
                                }
                                catch(Exception e)
                                {
                                    _log.Error("{0}\n{1}", e.Message, e.StackTrace);
                                }
                            });
                        }
                    }
                    else
                    {
                        ev.LastExecution = _sw.Elapsed;
                        try
                        {
                            ev.Event(delta);
                        }
                        catch (Exception e)
                        {
                            _log.Error("{0}\n{1}", e.Message, e.StackTrace);
                        }
                    }
                }
            }
        }

        public void Subscribe(OnTickEvent tickEvent, TimeSpan tickRate = default, bool useAsync = false)
        {
            lock (_lock)
            {
                _eventsToAdd.Enqueue(new TickEventEntity(tickEvent, tickRate, useAsync));
            }
        }

        public void UnSubscribe(OnTickEvent tickEvent)
        {
            lock (_lock)
            {
                _eventsToRemove.Enqueue(tickEvent);
            }
        }
    }
}
