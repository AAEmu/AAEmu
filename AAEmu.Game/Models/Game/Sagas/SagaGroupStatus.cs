namespace AAEmu.Game.Models.Game.Sagas;

/// <summary>
/// The status byte the chronicle (saga book) wire carries per saga group. The client's story tab
/// renders status 0 as the in-progress "activation" entry and any non-zero status as completed;
/// a group with no record at all is the locked entry that offers the buy button.
/// </summary>
public enum SagaGroupStatus : sbyte
{
    Active = 0,
    Complete = 1
}
