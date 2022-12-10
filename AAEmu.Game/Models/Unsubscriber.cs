using System;
using System.Collections.Generic;

namespace AAEmu.Game.Models
{
    internal class Unsubscriber<T> : IDisposable
    {
        private readonly List<IObserver<T>> _observers;
        private readonly IObserver<T> _observer;

        internal Unsubscriber(List<IObserver<T>> observers, IObserver<T> observer)
        {
            _observers = observers;
            _observer = observer;
        }

        public void Dispose()
        {
            if (!_observers.Contains(_observer))
            {
                return;
            }

            _observers.Remove(_observer);
        }
    }
}