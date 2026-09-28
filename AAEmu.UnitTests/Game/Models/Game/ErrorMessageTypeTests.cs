using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Models.Game;

/// <summary>
/// Pins ErrorMessageType values to the 10.0.2.13 client's enum_error_messages ids, which the client uses to
/// resolve SCErrorMsg into its localized text.
/// </summary>
public class ErrorMessageTypeTests
{
    [Test]
    [Arguments(ErrorMessageType.AuctionUpdateInventory, 89)]
    [Arguments(ErrorMessageType.MailFailSoulBoundItem, 177)]
    [Arguments(ErrorMessageType.TradeSoulBoundItem, 251)]
    [Arguments(ErrorMessageType.AucInvalidTargetAuctioneer, 387)]
    [Arguments(ErrorMessageType.SlaveEquipmentLoadedItem, 800)]
    [Arguments(ErrorMessageType.CannotAutoRegisterSkill, 802)]
    [Arguments(ErrorMessageType.SlaveEquipErrorRequireSlotAndItem, 803)]
    [Arguments(ErrorMessageType.SlaveUnequipErrorUnequipFirst, 804)]
    [Arguments(ErrorMessageType.AucInvalidPostAuthority, 805)]
    public async Task Value_MatchesClientTable(ErrorMessageType type, int clientId)
    {
        await Assert.That((int)type).IsEqualTo(clientId);
    }
}
