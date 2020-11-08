using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Mails
{
    class MailForSpeciality : BaseMail
    {
        private Character _sender;
        private uint _crafterId;
        private uint _tradedPack;
        private int _tradedRate;
        private uint _itemToSend;
        private int _itemCountBase;
        private int _itemCountBonus;
        private int _itemCountSeller;
        private int _itemCountCrafter;
        private int _interestRate;
        private string _tradePackName;
        private bool _sellerIsCrafter;
        private int _itemCountTotal;

        public static TimeSpan TradePackMailDelay = TimeSpan.FromHours(8); // Default is 8 hours
        public static string TradeDeliveryName = ".sellBackpack";
        public static string TradeDeliveryTitle = "Speciality Payment";
        public static string TradeDeliveryTitleSeller = "Speciality Payment [Delivery]";
        public static string TradeDeliveryTitleCrafter = "Speciality Payment [Crafter]";

        /*
         * Function from LUA in Trino 1.2
         * body = function(a, b, c, d, e, actualGain, receiverCase, coinType, crafterCoinCount, sellerCoinCount)
         * -> function(
         * TradePackName,     // Name string
         * TradeinPercent,    // Trade-Rate of merchant
         * applyPriceMoney,   // money (base x traderate) + bonus
         * totalMoney,        // base (money or item)
         * bargainingMoney,   // amount of bargain bonus that was used (money)
         * actualGain,        // Net payout (money) (regardless of seller/crafter mode), if (crafter = seller), then totalMoney is used as the payout value, and this is ignored
         * receiverCase,      // 0 = crafter payout ; 1 = seller payout ; 2 = seller is crafter
         * coinType,          // 0 = item reward ; 1 = coins reward
         * crafterCoinCount,  // item count for crafter (items only)
         * sellerCoinCount)   // item count for seller (items only)
         *
         *
         * Some extra info for future updates
         * Observed text body values for trade mails in AAFree (3.5.0.3)
         * 
         * Gold reward (crafter = seller)
         * body('Мятные рогалики', 130, 90086, 122968, 0, 0, 122968, 2, 1, 0, 0, 0, 0, 0, 5, 0, 100)
         * 'Мятные рогалики' - Traded pack name
         * 130,              - Traded % rate at outpost
         * 90086,            - Base Price
         * 122968,           - Total Payment
         * 0,                - ?
         * 0,                - ?
         * 122968,           - Total Payment again ?
         * 2,                - 
         * 1,                - Coin Type (1 = gold? ; 0 = resource?)
         * 0, 
         * 0, 
         * 0, 
         * 0, 
         * 0, 
         * 5,                - Interest rate
         * 0, 
         * 100               - Special Merchant Rate % (this could be a aafree custom thing as it seems related to their patron system)
         * 
         * Charcoal reward (crafter = seller)
         * body('Лавандовый чай', 130, 12, 158964, 0, 0, 158964, 2, 0, 16, 0, 0, 0, 0, 5, 0, 100)
         * 
         */

        public MailForSpeciality(Character seller, uint crafterId, uint tradepackTemplate, int tradeRate, uint itemRewardTemplateId, 
            int itemCountBase, int itemCountBonus, int itemCountForSeller, int itemCountForCrafter, int interestRate) : base()
        {
            _sender = seller;
            _sellerIsCrafter = (crafterId == 0) || (crafterId == seller.Id);
            if ((crafterId != 0) && (crafterId != seller.Id))
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
            _itemCountTotal = _itemCountCrafter + _itemCountSeller;
            _interestRate = interestRate;

            // TODO: make name localized based on activeplayer locale
            _tradePackName = LocalizationManager.Instance.Get("items", "name", _tradedPack);

            MailType = MailType.SysSellBackpack;

            Body.RecvDate = DateTime.UtcNow + TradePackMailDelay;
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

            var payout = (int)((_itemCountBase * _tradedRate) / 100f) + _itemCountBonus;
            var payoutWithInterest = (int)((payout * (100 + _interestRate)) / 100f);

            if (_itemToSend == Item.Coins)
            {
                // Body.Text = "Placeholder coins delivery text body";
                AttachMoney(_itemCountSeller);

                Body.Text = string.Format("body('{0}', {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
                    _tradePackName,               // Pack Name
                    _tradedRate,                  // Traded Rate
                    payout,                       // applyPriceMoney => money (base x traderate) + bonus
                    _sellerIsCrafter ? payoutWithInterest : _itemCountBase, // totalMoney (base)
                    _itemCountBonus,              // bargainingMoney => amount of bargain bonus that was used (money)
                    _itemCountSeller,             // actualGain => Net payout (money) (regardless of seller/crafter mode), if (crafter = seller), then totalMoney is used as the payout value, and this is ignored
                    _sellerIsCrafter ? 2 : 1,     // receiverCase => 0 = crafter payout ; 1 = seller payout ; 2 = seller is crafter
                    1, // coinType = 0 => item reward ; 1 = coins reward
                    0, // crafterCoinCount => unused for gold
                    0  // sellerCoinCount => unused for gold
                    );
            }
            else
            {
                // Send items
                var itemGrade = itemTemplate.FixedGrade;
                if (itemGrade <= 0)
                    itemGrade = 0;
                var newItem = ItemManager.Instance.Create(_itemToSend, _itemCountSeller, (byte)itemGrade, true);
                newItem.OwnerId = _sender.Id;
                newItem.SlotType = SlotType.Mail;
                Body.Attachments.Add(newItem);

                // Body.Text = "Placeholder resource delivery text body";
                // For item delivery, the client will calculate the total for you depending on receiverCase
                Body.Text = string.Format("body('{0}', {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
                    _tradePackName,
                    _tradedRate,
                    0, // unused
                    0, // unused
                    0, // unused
                    0, // unused
                    _sellerIsCrafter ? 2 : 1, // receiverCase => 0 = crafter payout ; 1 = seller payout ; 2 = seller is crafter
                    0, // coinType = 0 => item reward ; 1 = coins reward
                    _itemCountCrafter,
                    _itemCountSeller
                    );
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

            Header.ReceiverId = _crafterId ;
            ReceiverName = crafterName;

            var payout = (int)((_itemCountBase * _tradedRate) / 100f) + _itemCountBonus;
            var payoutWithInterest = (int)((payout * (100 + _interestRate)) / 100f);

            Title = TradeDeliveryTitleCrafter;
            if (_itemToSend == Item.Coins)
            {
                // Body.Text = "Placeholder coins delivery for crafter text body";
                AttachMoney(_itemCountCrafter);

                Body.Text = string.Format("body('{0}', {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
                    _tradePackName,
                    _tradedRate,
                    payout,
                    _itemCountBase,
                    _itemCountBonus,
                    _itemCountCrafter,
                    0, // 0 = crafter
                    1, // coinType 1=gold , 
                    0, // unused
                    0  // unused
                    );

            }
            else
            {
                // Body.Text = "Placeholder resource delivery for crafter text body";

                // Send items
                var itemGrade = itemTemplate.FixedGrade;
                if (itemGrade <= 0)
                    itemGrade = 0;
                var newItem = ItemManager.Instance.Create(_itemToSend, _itemCountCrafter, (byte)itemGrade, true);
                newItem.OwnerId = _sender.Id;
                newItem.SlotType = SlotType.Mail;
                Body.Attachments.Add(newItem);

                Body.Text = string.Format("body('{0}', {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
                    _tradePackName,
                    _tradedRate,
                    0, // unused
                    0, // unused
                    0, // unused
                    0, // unused
                    0, // 0 = crafter
                    0, // coinType 0 = item
                    _itemCountCrafter,
                    _itemCountSeller
                    );

            }

            return true;
        }
    }

}
