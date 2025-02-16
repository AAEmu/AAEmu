using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.Characters;

public class CharacterOnlineTrackingTask : Task
{
    public static TimeSpan CheckPrecision { get; set; } = TimeSpan.FromSeconds(5);
    private DateTime LastCheck { get; set; }
    private readonly object _lock = new();
    private bool Busy { get; set; }
    private Dictionary<ulong, DateTime> last60SecondCheck = new();

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
            // TODO: Use lastSeconds and newSeconds as a comparison for triggering time played achievements
            // TODO: Add divine clock feedback packets
            // Check if 60 seconds have passed since the last 60-second check
            if (last60SecondCheck.TryGetValue(character.AccountId, out var lastCheckTime))
            {
                var elapsedTime = DateTime.UtcNow - lastCheckTime;
                if (elapsedTime.TotalSeconds >= 60)
                {
                    //// Check if 1 hour has passed
                    //if (character.OnlineTime.TotalHours >= 1)
                    //{
                    //    // Perform additional work when 1 hour is reached
                    //    PerformHourlyCalculations(character);
                    //}

                    // Perform 60-second calculations
                    Perform60SecondCalculations(character, deltaSeconds);

                    // Update the last 60-second check time
                    last60SecondCheck[character.AccountId] = DateTime.UtcNow;
                }
            }
            else
            {
                // Initialize the last 60-second check time
                last60SecondCheck[character.AccountId] = DateTime.UtcNow;
            }
        }

        lock (_lock)
        {
            Busy = false;
        }
    }

    private void Perform60SecondCalculations(Character character, uint deltaSeconds)
    {
        // You can add any other 60-second calculations you need here
        for (var index = 0; index < character.ScheduleItems.Count; index++)
        {
            var scheduleItem = AccountManager.Instance.GetDivineClock(character.AccountId, character.ScheduleItems[index].ScheduleItemId);
            if (scheduleItem is not null)
            {
                character.ScheduleItems[index] = scheduleItem; // updated
            }

            var si = GameScheduleManager.Instance.GetScheduleItem(character.ScheduleItems[index].ScheduleItemId);
            if (character.ScheduleItems[index].Gave == si.GiveMax)
            {
                character.ScheduleItems[index].Cumulated = 0;
            }
            else
            {
                character.ScheduleItems[index].Cumulated += 60;
            }

            character.ScheduleItems[index].Updated = DateTime.UtcNow;

            // Update Account Divine Clock time
            AccountManager.Instance.UpdateDivineClock(character.AccountId, character.ScheduleItems[index].ScheduleItemId, character.ScheduleItems[index].Cumulated, character.ScheduleItems[index].Gave);
            character.SendPacket(new SCScheduleItemUpdatePacket(character.ScheduleItems));
        }
    }

    private void PerformHourlyCalculations(Character character)
    {
        // You can add any other hourly calculations you need here
    }
}
