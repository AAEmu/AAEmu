using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using System.Globalization;

namespace AAEmu.Game.Models.Game.Mails;

public class MailForSpeciality : BaseMail
{
    private readonly Character _sender;
    private readonly uint _crafterId;
    private readonly uint _tradedPack;
    private readonly int _tradedRate;
    private readonly uint _itemToSend;
    private readonly int _itemCountBase;
    private readonly int _itemCountBonus;
    private readonly int _itemCountSeller;
    private readonly int _itemCountCrafter;
    private readonly int _payoutBeforeInterest;
    private readonly int _totalPayout;
    private readonly double _interestPercent;
    private readonly double _freshnessPercent;
    private readonly int _sellerSharePercent;
    private readonly bool _sellerIsCrafter;
    // unused private int _itemCountTotal;

    private static readonly string TradeDeliveryName = ".sellBackpackNew";
    private static readonly string TradeDeliveryTitle = "Speciality Payment";
    private static readonly string TradeDeliveryTitleSeller = "Speciality Payment [Delivery]";
    private static readonly string TradeDeliveryTitleCrafter = "Speciality Payment [Crafter]";

    // sellBackpackNew resolves the first argument as an item template ID on the client.

    public MailForSpeciality(
        Character seller,
        uint crafterId,
        uint tradepackTemplate,
        int tradeRate,
        uint itemRewardTemplateId,
        int itemCountBase,
        int itemCountBonus,
        int itemCountForSeller,
        int itemCountForCrafter,
        int payoutBeforeInterest,
        int totalPayout,
        DateTime transactionUtc,
        double interestPercent,
        double freshnessPercent,
        int sellerSharePercent) : base()
    {
        _sender = seller;
        _sellerIsCrafter = crafterId == 0 || crafterId == seller.Id;
        if (crafterId != 0 && crafterId != seller.Id)
            _crafterId = crafterId;
        else
            _crafterId = 0;
        _tradedPack = tradepackTemplate;
        _tradedRate = tradeRate;
        _itemToSend = itemRewardTemplateId;
        _itemCountBase = itemCountBase;
        _itemCountBonus = itemCountBonus;
        _itemCountSeller = itemCountForSeller;
        _itemCountCrafter = itemCountForCrafter;
        // unused _itemCountTotal = _itemCountCrafter + _itemCountSeller;
        _payoutBeforeInterest = payoutBeforeInterest;
        _totalPayout = totalPayout;
        _interestPercent = interestPercent;
        _freshnessPercent = freshnessPercent;
        _sellerSharePercent = sellerSharePercent;

        MailType = MailType.SysSellBackpack;

        Body.RecvDate = transactionUtc.AddMinutes(AppConfiguration.Instance.Specialty.TradePackMailDelayInMinutes);
    }

    /// <summary>
    /// Prepare mail for the person who delivered the pack
    /// </summary>
    /// <returns></returns>
    public bool FinalizeForSeller()
    {
        var itemTemplate = ItemManager.Instance.GetTemplate(_itemToSend);
        if (itemTemplate == null)
            return false;

        Header.SenderId = 0;
        Header.SenderName = TradeDeliveryName;

        Header.ReceiverId = _sender.Id;
        ReceiverName = _sender.Name;

        Title = _crafterId == 0 ? TradeDeliveryTitle : TradeDeliveryTitleSeller;

        if (_itemToSend == Item.Coins)
        {
            // Body.Text = "Placeholder coins delivery text body";
            AttachMoney(_itemCountSeller);

            Body.Text = BuildBody(
                _payoutBeforeInterest,
                _sellerIsCrafter ? _totalPayout : _itemCountBase,
                _itemCountBonus,
                _itemCountSeller,
                _sellerIsCrafter ? 2 : 1,
                1,
                0,
                0);
        }
        else
        {
            // Send items
            var itemGrade = itemTemplate.FixedGrade;
            if (itemGrade <= 0)
                itemGrade = 0;
            var newItem = ItemManager.Instance.CreateUnpersisted(_itemToSend, _itemCountSeller, (byte)itemGrade);
            if (newItem == null)
                return false;
            newItem.OwnerId = _sender.Id;
            newItem.SlotType = SlotType.Mail;
            Body.Attachments.Add(newItem);

            // Body.Text = "Placeholder resource delivery text body";
            // For item delivery, the client will calculate the total for you depending on receiverCase
            Body.Text = BuildBody(
                0,
                0,
                0,
                0,
                _sellerIsCrafter ? 2 : 1,
                0,
                _itemCountCrafter,
                _itemCountSeller);
        }

        return true;
    }

    /// <summary>
    /// Prepare mail for the original crafter of the pack
    /// </summary>
    /// <returns></returns>
    public bool FinalizeForCrafter()
    {
        // TODO: test this part of the code (currently no crafter id support on items)

        if (_crafterId == 0)
            return false;
        var crafterName = NameManager.Instance.GetCharacterName(_crafterId);
        var itemTemplate = ItemManager.Instance.GetTemplate(_itemToSend);
        if (itemTemplate == null)
            return false;

        Header.SenderId = 0;
        Header.SenderName = TradeDeliveryName;

        Header.ReceiverId = _crafterId;
        ReceiverName = crafterName;

        Title = TradeDeliveryTitleCrafter;
        if (_itemToSend == Item.Coins)
        {
            // Body.Text = "Placeholder coins delivery for crafter text body";
            AttachMoney(_itemCountCrafter);

            Body.Text = BuildBody(
                _payoutBeforeInterest,
                _itemCountBase,
                _itemCountBonus,
                _itemCountCrafter,
                0,
                1,
                0,
                0);

        }
        else
        {
            // Body.Text = "Placeholder resource delivery for crafter text body";

            // Send items
            var itemGrade = itemTemplate.FixedGrade;
            if (itemGrade <= 0)
                itemGrade = 0;
            var newItem = ItemManager.Instance.CreateUnpersisted(_itemToSend, _itemCountCrafter, (byte)itemGrade);
            if (newItem == null)
                return false;
            newItem.OwnerId = _crafterId;
            newItem.SlotType = SlotType.Mail;
            Body.Attachments.Add(newItem);

            Body.Text = BuildBody(
                0,
                0,
                0,
                0,
                0,
                0,
                _itemCountCrafter,
                _itemCountSeller);

        }

        return true;
    }

    private string BuildBody(
        int earlyMoney,
        int totalMoney,
        int bargainingMoney,
        int actualGain,
        int receiverCase,
        int coinType,
        int crafterCoinCount,
        int sellerCoinCount)
    {
        return BuildBody(
            _tradedPack,
            _tradedRate,
            earlyMoney,
            totalMoney,
            bargainingMoney,
            actualGain,
            receiverCase,
            coinType,
            crafterCoinCount,
            sellerCoinCount,
            _interestPercent,
            _freshnessPercent,
            _sellerSharePercent);
    }

    internal static string BuildBody(
        uint tradePackTemplateId,
        int tradedRate,
        int earlyMoney,
        int totalMoney,
        int bargainingMoney,
        int actualGain,
        int receiverCase,
        int coinType,
        int crafterCoinCount,
        int sellerCoinCount,
        double interestPercent,
        double freshnessPercent,
        int sellerSharePercent)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "body({0}, {1}, {2}, {3}, {4}, 0, {5}, {6}, {7}, {8}, {9}, 0, 0, 0, {10}, {11}, 0, {12})",
            tradePackTemplateId,
            tradedRate,
            earlyMoney,
            totalMoney,
            bargainingMoney,
            actualGain,
            receiverCase,
            coinType,
            crafterCoinCount,
            sellerCoinCount,
            interestPercent,
            freshnessPercent,
            sellerSharePercent);
    }
}
