namespace AAEmu.Game.Models.Game.Items;

public class WearableSlot
{
    public uint SlotTypeId { get; set; }
    public int Coverage { get; set; }

    /// <summary>Per-slot gear-score weight from wearable_slots.gear_score_multiplier.</summary>
    public int GearScoreMultiplier { get; set; }
}
