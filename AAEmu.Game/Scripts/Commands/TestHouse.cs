using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestHouse : ICommand
{
    public void OnLoad()
    {
        string[] names = { "house", "test_house", "testhouse" };
        CommandManager.Instance.Register(names, this);
    }

    public string GetCommandLineHelp()
    {
        return "[command] [options]";
    }

    public string GetCommandHelpText()
    {
        return "Available commands;\n" +
            "|cFFFFFFFFinfo|r -> Shows various house info\n" +
            "|cFFFFFFFFtaxmail|r -> Creates a tax due mail for the house owner\n" +
            "|cFFFFFFFFsetforsale <money> [buyer]|r -> Forces a house for sale to be set, with optional [buyer] specified. Specify 0 money to clear the sale. (does not return any certificates)\n" +
            "|cFFFFFFFFsettaxpaid|r -> Sets the house's taxdue date as if just build and paid for.\n" +
            "|cFFFFFFFFsettaxdue|r -> Sets the house's taxdue date to now.\n" +
            "|cFFFFFFFFsettaxduesoon|r -> Sets the house's taxdue date to 20 seconds from now.\n" +
            "|cFFFFFFFFsetdemosoon|r -> Sets the house's demolition date to 20 seconds from now.\n" +
            "|cFFFFFFFFforcedemo|r -> Forcefully demolishes a house right now, but returns all furniture regardless if it is normally returned or not.\n" +
            "";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length <= 0)
        {
            character.SendMessage("[House] " + GetCommandHelpText());
            return;
        }

        House house = null;
        if ((character.CurrentTarget != null) && (character.CurrentTarget is House selectedHouse))
        {
            house = selectedHouse;
        }

        if (house == null)
        {
            character.SendMessage("[House] No building selected");
            return;
        }

        try
        {
            var a0 = args[0].ToLower();
            if (a0 == "info")
            {
                character.SendMessage($"[House] ObjId: {house.ObjId} - TlId: {house.TlId} - HouseId: {house.Id} - TemplateId: {house.TemplateId} - ModelId: {house.ModelId} - {house.Name}");

                HousingManager.Instance.CalculateBuildingTaxInfo(house.AccountId, house.Template, false,
                    out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount,
                    out var hostileTaxRate, out _);

                if (DateTime.UtcNow >= house.TaxDueDate)
                    character.SendMessage($"[House] Tax Due: {totalTaxAmountDue}{(house.Template.HeavyTax ? "" : "(no heavy tax)")} by {house.TaxDueDate}");
                else if (DateTime.UtcNow >= house.TaxDueDate.AddDays(7))
                    character.SendMessage($"[House] Tax Overdue: {totalTaxAmountDue * 2}{(house.Template.HeavyTax ? "" : "(no heavy tax)")}, demolition at {house.ProtectionEndDate}");
            }
            else if (a0 == "taxmail")
            {
                var newMail = new MailForTax(house);
                newMail.FinalizeMail();
                newMail.Send();
                character.SendMessage($"[House] Created tax for {house.Name}");
            }
            else if (a0 == "settaxpaid")
            {
                house.ProtectionEndDate = DateTime.UtcNow.AddDays(21);
                HousingManager.UpdateTaxInfo(house);
                character.SendMessage($"[House] {house.Name} tax paid");
            }
            else if (a0 == "settaxdue")
            {
                house.ProtectionEndDate = DateTime.UtcNow.AddDays(14);
                HousingManager.UpdateTaxInfo(house);
                character.SendMessage($"[House] {house.Name} tax due");
            }
            else if (a0 == "settaxoverdue")
            {
                house.ProtectionEndDate = DateTime.UtcNow.AddDays(7);
                HousingManager.UpdateTaxInfo(house);
                character.SendMessage($"[House] {house.Name} tax overdue");
            }
            else if (a0 == "setdemosoon")
            {
                house.ProtectionEndDate = DateTime.UtcNow.AddSeconds(20);
                HousingManager.UpdateTaxInfo(house);
                character.SendMessage($"[House] {house.Name} demolished in about 20 seconds");
            }
            else if (a0 == "setforsale")
            {
                var price = 0u;
                var buyer = string.Empty;
                var buyerId = 0u;

                if (args.Length > 1)
                {
                    if (uint.TryParse(args[1], out var priceVal))
                    {
                        price = priceVal;
                    }
                    else
                    {
                        character.SendMessage("[House] Parse error, price");
                        return;
                    }
                }

                if (args.Length > 2)
                {
                    buyer = args[2];
                    buyerId = NameManager.Instance.GetCharacterId(buyer);
                    if (buyerId > 0)
                    {
                        buyer = NameManager.Instance.GetCharacterName(buyerId);
                    }
                    else
                    {
                        character.SendMessage($"[House] invalid buyer name {args[2]}");
                        return;
                    }
                }

                if (buyerId <= 0)
                    buyer = "anyone";

                if (price > 0)
                {
                    // Set for sale
                    if (house.SellPrice > 0)
                    {
                        character.SendMessage("[House] Is already for sale, clear first to assign a new value");
                        return;
                    }

                    if (HousingManager.SetForSale(house, price, buyerId, null))
                        character.SendMessage($"[House] Setting {house.Name} for sale with a price of {price} to buy for {buyer}.");
                    else
                        character.SendMessage($"[House] Failed to set {house.Name} for sale !");
                }
                else
                {
                    if (house.SellPrice <= 0)
                    {
                        character.SendMessage("[House] This house wasn't for sale.");
                        return;
                    }

                    // Remove sale (for GM commands we don't return certificates)
                    if (HousingManager.CancelForSale(house, false))
                        character.SendMessage($"[House] {house.Name} is no longer for sale");
                    else
                        character.SendMessage($"[House] Failed to remove sale from {house.Name} !");
                }

            }
            else if (a0 == "forcedemo")
            {
                HousingManager.Instance.Demolish(null, house, false, true);
                character.SendMessage($"[House] {house.Name} demolished with full restore");
            }
            else
            {
                character.SendMessage($"[House] Unknown command: {a0}");
            }
        }
        catch (Exception e)
        {
            character.SendMessage($"[House] Exception: {e.Message}");
        }
    }

}
