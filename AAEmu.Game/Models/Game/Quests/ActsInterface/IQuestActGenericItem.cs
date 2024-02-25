namespace AAEmu.Game.Models.Game.Quests.ActsInterface;

/// <summary>
/// Helper interface to help simplify generic item related Quest Acts
/// </summary>
public interface IQuestActGenericItem
{
    public uint ItemId { get; set; }
    public bool Cleanup { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }
}
