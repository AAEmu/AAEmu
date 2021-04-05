using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar
{
    public enum AggroKind
    {
        Damage,
        Heal,
        Etc
    }
    public class Aggro
    {
        private object _lock = new object();
        
        public Unit Owner { get; }

        public Aggro(Unit owner)
        {
            Owner = owner;
        }

        //Considering using interlocked methods instead of a lock, need to research how they work..
        private int _damageAggro;
        public int DamageAggro { 
            get { 
                lock (_lock) {
                    return _damageAggro;
                } 
            }
        }

        private int _healAggro;
        public int HealAggro {
            get
            {
                lock(_lock)
                {
                    return _healAggro;
                }
            }
        }

        public int TotalAggro { 
            get
            {
                lock(_lock)
                {
                    return _damageAggro + _healAggro;
                }
            }
        }

        public void AddAggro(AggroKind kind, int amount)
        {
            lock (_lock)
            {
                if (kind == AggroKind.Damage)
                    _damageAggro += amount;
                else if (kind == AggroKind.Heal)
                    _healAggro += (int)(amount * 0.6f);
            }
        }

        public void RemoveAggro(AggroKind kind, int amount) => AddAggro(kind, -amount);
    }
}
