﻿using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Mails
{
    public class MailForTax : BaseMail
    {
        /*
         * Working example for 1.2
         * /testmail 6 .houseTax title(25) "body('Test','1606565186','1607169986','1606565186','250000','50','3','0','500000','true','1')" 0 500000
         * 
         * Values for the extra flag look as following
         * Extra = ((long)zoneGroupId << 48) + ((long)extraUnknown << 32) + ((long)houseId);
         * 
         */

        private House _house;

        public static string TaxSenderName = ".houseTax";

        public MailForTax(House house) : base()
        {
            _house = house;

            MailType = MailType.Billing;
            Body.RecvDate = DateTime.UtcNow ;
        }

        public static bool UpdateTaxInfo(BaseMail mail, House house)
        {
            // Check if owner is still valid
            var ownerName = NameManager.Instance.GetCharacterName(house.OwnerId);
            if (ownerName == null)
                return false;
            mail.Header.ReceiverId = house.OwnerId;
            mail.ReceiverName = ownerName;
            
            // Grab the zone the house is in
            var zone = ZoneManager.Instance.GetZoneByKey(house.Position.ZoneId);
            if (zone == null)
                return false;

            // Set mail title
            mail.Title = "title(" + zone.GroupId.ToString() + ")"; // Title calls a function to call zone group name

            // Get Tax info
            if (!HousingManager.Instance.CalculateBuildingTaxInfo(house.AccountId, house.Template, false, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out var hostileTaxRate))
                return false;
            
            var weeksDelta = house.TaxDueDate - DateTime.UtcNow;
            var weeks = 0 ;
            if (weeksDelta.TotalSeconds <= 0)
            {
                weeks = (int)Math.Ceiling(weeksDelta.TotalDays / -7f);
            }

            //testmail 6 .houseTax title(25) "body('Test','1606565186','1607169986','1606565186','250000','50','3','0','500000','true','1')" 0 500000
            mail.Body.Text = string.Format("body('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}', '{8}', '{9}', '{10}')",
                house.Name,                                 // House Name
                Helpers.UnixTime(house.PlaceDate),          // Tax period start (this might need to be the same as tax due date)
                Helpers.UnixTime(house.ProtectionEndDate),  // Tax period end
                Helpers.UnixTime(house.TaxDueDate),         // Tax Due Date
                house.Template.Taxation.Tax,                // This house base tax rate
                hostileTaxRate,                             // dominion tax rate (castle tax rate ?)
                heavyTaxHouseCount,                         // number of heavy tax houses
                weeks,                                      // unpaid week count (listed as late fee)
                totalTaxAmountDue,                          // amount to Pay (as gold reference)
                house.Template.HeavyTax ? "true" : "false", // is this a heavy tax building
                normalTaxHouseCount                         // number of tax-excempt houses
                );
            // In never version this has a extra field at the end, which I assume is would be the hostile tax rate
            // TODO: Make better use of unpaidweekcount

            mail.Body.BillingAmount = totalTaxAmountDue;

            // Extra tag
            ushort extraUnknown = 0;
            mail.Header.Extra = ((long)zone.GroupId << 48) + ((long)extraUnknown << 32) + ((long)house.Id);
            mail.Header.Status = MailStatus.Unpaid;

            return true;
        }

        /// <summary>
        /// Prepare mail
        /// </summary>
        /// <returns></returns>
        public bool FinalizeMail()
        {
            Header.SenderId = 0;
            Header.SenderName = TaxSenderName;

            if (!UpdateTaxInfo(this,_house))
                return false;

            return true;
        }

    }

}
