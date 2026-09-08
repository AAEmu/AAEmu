namespace AAEmu.Game.Models.Game.Trading;

public sealed class SpecialtyMaterialContribution(ulong sequence, uint itemId, uint amount)
{
    public ulong Sequence { get; } = sequence;
    public uint ItemId { get; } = itemId;
    public uint Amount { get; } = amount;
}
