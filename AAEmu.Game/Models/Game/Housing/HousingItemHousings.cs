namespace AAEmu.Game.Models.Game.Housing;

public class HousingItemHousings
{
    public uint Id { get; set; }
    public uint Item_Id { get; set; }
    public uint Design_Id { get; set; }

    /// <summary>
    /// The <c>item_housings.completion</c> flag: true for the complete kit whose blueprint places
    /// the house already finished, false for the plain design that builds through
    /// <c>housing_build_steps</c>.
    /// </summary>
    public bool Completion { get; set; }

    public HousingItemHousings()
    {
    }
}
