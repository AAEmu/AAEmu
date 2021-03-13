﻿using System;
using System.Collections.Generic;
using System.Threading;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;

namespace AAEmu.Game.Core.Managers
{
    public class TimeManager : Singleton<TimeManager>, IObservable<float>
    {
        private readonly List<IObserver<float>> _observers;
        private bool _work;
        private const float MaxTime = 86400;
        private float _time = 43200; // TODO 12h 00m
        private readonly float _tick = 3600 * Speed;

        public static float Speed = .0016666f;
        public float GetTime() => _time / 3600f;

        public TimeManager()
        {
            _observers = new List<IObserver<float>>();
        }

        public IDisposable Subscribe(IObserver<float> observer)
        {
            if (_observers.Contains(observer))
            {
                return null;
            }

            _observers.Add(observer);

            return new Unsubscriber<float>(_observers, observer);
        }

        public IDisposable Subscribe(GameConnection connection, IObserver<float> observer)
        {
            connection.SendPacket(new SCDetailedTimeOfDayPacket(GetTime()));
            return Subscribe(observer);
        }

        public void Start()
        {
            var date = DateTime.UtcNow;
            _time = date.Hour * 60 * 60 + date.Minute * 60;
            _work = true;
            new Thread(Tick) { Name = "TimeManagerThread" }.Start();
        }

        public void Stop()
        {
            _work = false;
        }

        private void Tick()
        {
            while (_work)
            {
                _time += _tick * 10;
                if (_time > MaxTime)
                {
                    _time -= MaxTime;
                }

                new Thread(Push) { Name = "TimeManagerPushThread" }.Start();
                Thread.Sleep(10000);
            }
        }

        private void Push()
        {
            var time = GetTime();
            foreach (var observer in _observers)
            {
                observer.OnNext(time);
            }
        }
    }
}
