using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Models.Game.Trading;

public sealed record SpecialtySaleWrite(
    ulong PackItemId,
    uint PackTemplateId,
    ulong PackOwnerId,
    ulong PackContainerId,
    SlotType PackSlotType,
    int PackSlot,
    uint AccountId,
    int ExpectedLabor,
    int NewLabor,
    int ExpectedLocalLabor,
    int NewLocalLabor,
    IReadOnlyList<BaseMail> PayoutMails,
    SpecialtyMarketWrite Market)
{
    public int PackCount { get; } = 1;
}
