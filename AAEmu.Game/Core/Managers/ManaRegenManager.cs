using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Core.Managers;

public class ManaRegenManager : Singleton<ManaRegenManager>
{
    private int UpdateDelay { get; set; } = 200; // Интервал тика баффа в миллисекундах
    private static object Lock { get; } = new();
    private Dictionary<uint, ManaRegenTemplate> Registrations { get; set; }

    public void Initialize()
    {
        Registrations = new Dictionary<uint, ManaRegenTemplate>();
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(UpdateDelay), true);
    }

    internal void Register(Character player, ManaRegenTemplate template)
    {
        lock (Lock)
        {
            // If no entry, create one
            if (!Registrations.TryGetValue(player.Id, out var entry))
            {
                Registrations.Add(player.Id, template);
            }

            // If nothing set, delete
            //Registrations.Remove(player.Id);
        }
    }

    private void Tick(TimeSpan delta)
    {
        lock (Lock)
        {
            if (Registrations.Count <= 0)
                return;

            foreach (var (_, entry) in Registrations)
            {
                if (!entry.ApplyBuff(entry.Owner))
                {
                    UnRegister(entry.Owner);
                    entry.Owner.Buffs.RemoveBuff((uint)BuffConstants.Dash);
                }
                
            } // for each player
        } // lock
    }

    private void UnRegister(Character player)
    {
        lock (Lock)
        {
            Registrations.Remove(player.Id);
        }
    }
}
