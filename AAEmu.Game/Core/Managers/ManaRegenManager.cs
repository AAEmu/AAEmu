using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Core.Managers;

public class ManaRegenManager(ITickManager tickManager) : Singleton<ManaRegenManager>, IManaRegenManager
{
    private int UpdateDelay { get; set; } = 200; // Buff tick interval in milliseconds
    private static object Lock { get; } = new();

    // One registration per character AND buff: several buffs can carry a tick mana cost, and each one has
    // to drain, be paced and end on its own. Keyed by player alone, the first registration won and the
    // rest were dropped — which is why the generalised columns would have been inert next to Dash.
    private Dictionary<(uint PlayerId, uint BuffId), ManaRegenTemplate> Registrations { get; set; }

    public void Initialize()
    {
        Registrations = new Dictionary<(uint PlayerId, uint BuffId), ManaRegenTemplate>();
        tickManager.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(UpdateDelay), true);
    }

    internal void Register(Character player, ManaRegenTemplate template)
    {
        lock (Lock)
        {
            Registrations[(player.Id, template.BuffId)] = template;
        }
    }

    private void Tick(TimeSpan delta)
    {
        lock (Lock)
        {
            if (Registrations.Count <= 0)
                return;

            // Snapshot: a registration that cannot pay is removed inside the loop, and removing from the
            // dictionary being enumerated threw InvalidOperationException out of the tick subscription.
            foreach (var entry in Registrations.Values.ToList())
            {
                if (!entry.ApplyBuff(entry.Owner))
                {
                    UnRegister(entry);
                    entry.Owner.Buffs.RemoveBuff(entry.BuffId);
                }

            } // for each registration
        } // lock
    }

    private void UnRegister(ManaRegenTemplate template)
    {
        lock (Lock)
        {
            Registrations.Remove((template.Owner.Id, template.BuffId));
        }
    }
}
