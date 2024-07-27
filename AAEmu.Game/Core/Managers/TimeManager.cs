using System;
using System.Collections.Generic;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;

namespace AAEmu.Game.Core.Managers;

public class TimeManager : Singleton<TimeManager>, IObservable<float>
{
    private readonly List<IObserver<float>> _observers;
    private bool _work;
    private const float MaxTime = 86400f;
    private float _time = 43200f; // TODO 12h 00m
    private const float TickDelay = 3600f * Speed;

    private const float Speed = .0016666f;
    /// <summary>
    /// Current game time in hours
    /// </summary>
    /// <returns></returns>
    public float GetTime { get => _time / 3600f; }

    private float _lastTime;

    public TimeManager()
    {
        _observers = new List<IObserver<float>>();
    }

    public IDisposable Subscribe(IObserver<float> observer)
    {
        if (_observers.Contains(observer))
            return null;
        _observers.Add(observer);

        return new Unsubscriber<float>(_observers, observer);
    }

    public IDisposable Subscribe(GameConnection connection, IObserver<float> observer)
    {
        connection.SendPacket(new SCDetailedTimeOfDayPacket(GetTime));
        return Subscribe(observer);
    }

    public void Start()
    {
        var curHours = DateTime.UtcNow.TimeOfDay.Hours;
        var curMinutes = DateTime.UtcNow.TimeOfDay.Minutes;
        _time = 12 * 3600f;
        _lastTime = _time;
        //_time = curHours * 3600f + curMinutes;
        _work = true;
        new Thread(Tick) { Name = "TimeManagerThread" }.Start();
    }

    public float Get()
    {
        return _time;
    }

    public void Set(int hour)
    {
        _time = hour * 3600f;
    }

    public void Stop()
    {
        _work = false;
    }

    private void Tick()
    {
        while (_work)
        {
            _time += TickDelay * 10;
            if (_time > MaxTime)
                _time -= MaxTime;

            new Thread(Push) { Name = "TimeManagerPushThread" }.Start();
            Thread.Sleep(10000);
        }
    }

    private void Push()
    {
        var time = GetTime;
        foreach (var observer in _observers)
            observer.OnNext(time);
        WorldManager.Instance.OnTimeOfDayChange(time, _lastTime);
        _lastTime = time;
    }
}
