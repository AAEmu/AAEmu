using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Tasks.Characters;

public class CharacterOnlineTrackingTask : Task
{
    public static TimeSpan CheckPrecision { get; set; } = TimeSpan.FromSeconds(5);
    private DateTime LastCheck { get; set; }
    private readonly object _lock = new();
    private bool Busy { get; set; }

    public CharacterOnlineTrackingTask()
    {
        LastCheck = DateTime.UtcNow;
        lock (_lock)
            Busy = false;
    }

    public override void Execute()
    {
        lock (_lock)
        {
            if (Busy)
                return;
            Busy = true;
        }
        var delta = DateTime.UtcNow - LastCheck;
        LastCheck = DateTime.UtcNow;

        // Loop all online players
        foreach (var character in WorldManager.Instance.GetAllCharacters())
        {
            // Update character time
            var lastSeconds = Math.Floor(character.OnlineTime.TotalSeconds);
            character.OnlineTime += delta;
            var newSeconds = Math.Floor(character.OnlineTime.TotalSeconds);
            var deltaSeconds = (uint)(newSeconds - lastSeconds);

            // Update Account Divine Clock time
            var (time, taken) = AccountManager.Instance.GetDivineClock(character.AccountId);
            time += deltaSeconds;
            AccountManager.Instance.UpdateDivineClock(character.AccountId, time, taken);

            // TODO: Use lastSeconds and newSeconds as a comparison for triggering time played achievements
            // TODO: Add divine clock feedback packets

            if (time % 60 == 0)
            {
                var si = new ScheduleItem
                {
                    ItemTemplateId = 9000003,
                    Gave = (byte)taken,
                    Acumulated = time,
                    Updated = DateTime.UtcNow
                };
                character.SendPacket(new SCScheduleItemUpdatePacket([si]));
                character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Unk136, [], []));

            }
        }

        lock (_lock)
        {
            Busy = false;
        }
    }
}
