using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterActability(Character owner)
{
    public Dictionary<uint, Actability> Actabilities { get; set; } = [];

    public Character Owner { get; set; } = owner;

    /// <summary>
    /// Gets the character's points for an actability, optionally including the unit-attribute bonuses
    /// identified by <c>actability_groups.unit_attr_id</c>.
    /// </summary>
    public int GetPoint(uint id, bool includeBonuses)
    {
        if (!Actabilities.TryGetValue(id, out var actability))
            return 0;

        var point = (double)actability.Point;
        if (includeBonuses && actability.Template.UnitAttributeId >= 0)
            point = Owner.CalculateWithBonuses(point, (UnitAttribute)(uint)actability.Template.UnitAttributeId);

        return (int)Math.Clamp(point, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Adds points to a specific ActAbility (life skill)
    /// </summary>
    /// <param name="id"></param>
    /// <param name="point"></param>
    /// <returns>The amount that was actually changed</returns>
    public int AddPoint(uint id, int point)
    {
        if (!Actabilities.TryGetValue(id, out var actability))
            return 0;
        var previousPoints = actability.Point;
        var template = CharacterManager.Instance.GetPointCapLimit(id, actability.Step);
        var cap = template?.UpLimit ?? int.MaxValue;
        actability.Point = ExpertLimitRules.AddEarnedPoints(actability.Point, point, cap);
        return actability.Point - previousPoints;
    }

    /// <summary>
    /// GM / test helper: set points (and optional expert step) and push <see cref="SCActabilityPacket"/>.
    /// Points are clamped to the expert-limit cap for the resulting step.
    /// </summary>
    public bool TrySet(uint id, int point, int? step)
    {
        if (!Actabilities.TryGetValue(id, out var actability))
            return false;

        if (step.HasValue)
        {
            var stepLimit = ExpertLimitRules.UsesLanguageLadder(id)
                ? CharacterManager.Instance.GetLanguageExpertLimit(step.Value)
                    ?? CharacterManager.Instance.GetExpertLimit(step.Value)
                : CharacterManager.Instance.GetExpertLimit(step.Value);
            if (stepLimit == null)
                return false;
            actability.Step = (byte)step.Value;
        }

        var template = CharacterManager.Instance.GetPointCapLimit(id, actability.Step);
        if (template == null)
            return false;

        actability.Point = ExpertLimitRules.ClampPoints(template, point);
        Send();
        return true;
    }

    /// <summary>
    /// </summary>
    public bool Regrade(uint id, bool isUpgrade, bool autoUseAaPoint)
    {
        if (!Actabilities.TryGetValue(id, out var actability))
        {
            Owner.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        // Rank buttons always walk the production ladder (same index the UI uses).
        var currentTemplate = CharacterManager.Instance.GetExpertLimit(actability.Step);
        if (isUpgrade)
        {
            var targetStep = actability.Step + 1;
            var targetTemplate = CharacterManager.Instance.GetExpertLimit(targetStep);
            var hasSlot = ExpertLimitRules.HasSelectionSlot(
                Actabilities.Values,
                targetTemplate,
                targetStep,
                Owner.ExpandedExpert,
                actability.Template.ViewGroupId);
            var upgradeError = ExpertLimitRules.UpgradeError(
                currentTemplate,
                targetTemplate,
                actability.Point,
                hasSlot);
            if (upgradeError != null)
            {
                Owner.SendErrorMessage(upgradeError.Value);
                return false;
            }

            if (!TryPay(currentTemplate.UpCurrencyId, currentTemplate.UpPrice, autoUseAaPoint))
                return false;

            actability.Step = (byte)targetStep;
        }
        else
        {
            var targetTemplate = actability.Step == 0
                ? null
                : CharacterManager.Instance.GetExpertLimit(actability.Step - 1);
            var downgradeError = ExpertLimitRules.DowngradeError(actability.Step, currentTemplate, targetTemplate);
            if (downgradeError != null)
            {
                Owner.SendErrorMessage(downgradeError.Value);
                return false;
            }

            var ticketItemId = CharacterManager.Instance.DowngradeIntensifiedExpertTicketItemId;
            var needsTicket = ExpertLimitRules.RequiresIntensifiedDowngradeTicket(currentTemplate);
            var hasTicket = !needsTicket || Owner.Inventory.CheckItems(
                SlotType.Inventory, ticketItemId, ExpertLimitRules.IntensifiedDowngradeTicketCount);
            var ticketError = ExpertLimitRules.DowngradeTicketError(currentTemplate, ticketItemId, hasTicket);
            if (ticketError != null)
            {
                Owner.SendErrorMessage(ticketError.Value);
                return false;
            }

            if (!TryPay(currentTemplate.DownCurrencyId, currentTemplate.DownPrice, autoUseAaPoint))
                return false;

            if (needsTicket)
            {
                var consumed = Owner.Inventory.Bag.ConsumeItem(
                    ItemTaskType.ChangeExpertLimit,
                    ticketItemId,
                    ExpertLimitRules.IntensifiedDowngradeTicketCount,
                    null);
                if (consumed != ExpertLimitRules.IntensifiedDowngradeTicketCount)
                {
                    Owner.SendErrorMessage(ErrorMessageType.NotEnoughItem);
                    return false;
                }
            }

            // Rank is only the selected slot. Earned points stay so the same total can
            // walk back up as long as each rank still has a free slot.
            actability.Step--;
        }

        Owner.SendPacket(new SCExpertLimitModifiedPacket(isUpgrade, id, actability.Point, actability.Step));
        return true;
    }

    private bool TryPay(uint currencyId, int price, bool autoUseAaPoint)
    {
        if (price < 0)
        {
            Owner.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }
        if (price == 0)
            return true;

        var currency = (ContentCurrencyType)currencyId;
        switch (currency)
        {
            case ContentCurrencyType.Gold:
            case ContentCurrencyType.GoldWithAaPoint:
                return autoUseAaPoint
                    ? Owner.SubtractAAPoint(SlotType.Inventory, price, ItemTaskType.ChangeExpertLimit)
                    : Owner.SubtractMoney(SlotType.Inventory, price, ItemTaskType.ChangeExpertLimit);
            case ContentCurrencyType.HonorPoint:
                if (Owner.HonorPoint < price)
                {
                    Owner.SendErrorMessage(ErrorMessageType.NotEnoughHonorPoint);
                    return false;
                }
                Owner.ChangeGamePoints(GamePointKind.Honor, -price);
                return true;
            case ContentCurrencyType.LivingPoint:
                if (Owner.VocationPoint < price)
                {
                    Owner.SendErrorMessage(ErrorMessageType.NotEnoughLivingPoint);
                    return false;
                }
                Owner.ChangeGamePoints(GamePointKind.Vocation, -price);
                return true;
            case ContentCurrencyType.AaPoint:
                return Owner.SubtractAAPoint(SlotType.Inventory, price, ItemTaskType.ChangeExpertLimit);
            case ContentCurrencyType.ContributionPoint:
                if (Owner.Expedition?.GetMember(Owner)?.ContributionPoint < price)
                {
                    Owner.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
                    return false;
                }
                return global::AAEmu.Game.Core.Managers.ExpeditionManager.Instance
                    .TryChangeContributionPoints(Owner, -price, false);
            default:
                Owner.SendErrorMessage(ErrorMessageType.Invalid);
                return false;
        }
    }

    public bool ExpandExpert()
    {
        var expand = CharacterManager.Instance.GetExpandExpertLimit(Owner.ExpandedExpert);
        var hasItems = expand == null
            || expand.ItemId == 0
            || expand.ItemCount == 0
            || Owner.Inventory.CheckItems(Items.SlotType.Inventory, expand.ItemId, expand.ItemCount);
        var expandError = ExpertLimitRules.ExpandError(expand, Owner.VocationPoint, hasItems);
        if (expandError != null)
        {
            Owner.SendErrorMessage(expandError.Value);
            return false;
        }

        if (expand.ItemId != 0 && expand.ItemCount != 0)
        {
            var consumed = Owner.Inventory.Bag.ConsumeItem(
                ItemTaskType.ExpandExpert,
                expand.ItemId,
                expand.ItemCount,
                null);
            if (consumed != expand.ItemCount)
                return false;
        }

        if (expand.LifePoint > 0)
            Owner.ChangeGamePoints(GamePointKind.Vocation, -expand.LifePoint);

        Owner.ExpandedExpert = expand.ExpandCount;
        Owner.SendPacket(new SCExpertExpandedPacket(Owner.ExpandedExpert));
        return true;
    }

    public void Send()
    {
        Owner.SendPacket(new SCActabilityPacket(true, Actabilities.Values.ToArray()));
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM actabilities WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetUInt32("id");
                    var template = CharacterManager.Instance.GetActability(id);

                    var actability = new Actability(template)
                    {
                        Id = id,
                        Point = reader.GetInt32("point"),
                        Step = reader.GetByte("step")
                    };
                    Actabilities.Add(actability.Id, actability);
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        foreach (var actability in Actabilities.Values)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO actabilities(`id`,`point`,`step`,`owner`) VALUES (@id, @point, @step, @owner)";
                command.Parameters.AddWithValue("@id", (byte)actability.Id);
                command.Parameters.AddWithValue("@point", actability.Point);
                command.Parameters.AddWithValue("@step", actability.Step);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }
        }
    }
}
