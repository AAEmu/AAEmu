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
        actability.Point += point;

        var template = CharacterManager.Instance.GetExpertLimit(actability.Step);
        if (actability.Point > template.UpLimit)
            actability.Point = template.UpLimit;
        return actability.Point - previousPoints;
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

        var currentTemplate = CharacterManager.Instance.GetExpertLimit(actability.Step);
        if (currentTemplate == null)
        {
            Owner.SendErrorMessage(isUpgrade
                ? ErrorMessageType.ActabilityCanUpgradeAnyMore
                : ErrorMessageType.ActabilityCanDowngradeAnyMore);
            return false;
        }

        if (isUpgrade)
        {
            if (actability.Point < currentTemplate.UpLimit)
            {
                Owner.SendErrorMessage(ErrorMessageType.ActabilityNotEnoughPoint);
                return false;
            }

            var targetStep = actability.Step + 1;
            var targetTemplate = CharacterManager.Instance.GetExpertLimit(targetStep);
            if (targetTemplate == null)
            {
                Owner.SendErrorMessage(ErrorMessageType.ActabilityCanUpgradeAnyMore);
                return false;
            }

            if (!HasExpertSelectionSlot(actability, targetTemplate, targetStep))
            {
                Owner.SendErrorMessage(ErrorMessageType.ActabilityCanUpgradeSelectionCountLimit);
                return false;
            }

            if (!TryPay(currentTemplate.UpCurrencyId, currentTemplate.UpPrice, autoUseAaPoint))
                return false;

            actability.Step = (byte)targetStep;
        }
        else
        {
            if (actability.Step == 0)
            {
                Owner.SendErrorMessage(ErrorMessageType.ActabilityCanDowngradeAnyMore);
                return false;
            }

            var targetTemplate = CharacterManager.Instance.GetExpertLimit(actability.Step - 1);
            if (targetTemplate == null)
            {
                Owner.SendErrorMessage(ErrorMessageType.ActabilityCanDowngradeAnyMore);
                return false;
            }

            if (!TryPay(currentTemplate.DownCurrencyId, currentTemplate.DownPrice, autoUseAaPoint))
                return false;

            actability.Step--;
            actability.Point = Math.Min(actability.Point, targetTemplate.UpLimit);
        }

        Owner.SendPacket(new SCExpertLimitModifiedPacket(isUpgrade, id, actability.Step));
        return true;
    }

    private bool HasExpertSelectionSlot(Actability actability, ExpertLimit targetTemplate, int targetStep)
    {
        if (targetTemplate.UseIntensified)
        {
            var viewGroupId = actability.Template.ViewGroupId;
            if (!targetTemplate.IntensifiedViewGroupLimits.TryGetValue(viewGroupId, out var groupLimit))
                return false;

            var groupCount = Actabilities.Values.Count(entry =>
                entry.Template.CountsTowardExpertLimit &&
                entry.Template.ViewGroupId == viewGroupId &&
                entry.Step >= targetStep);
            return groupCount < groupLimit;
        }

        if (targetTemplate.ExpertLimitCount == 0)
            return true;

        var selectedCount = Actabilities.Values.Count(entry =>
            entry.Template.CountsTowardExpertLimit && entry.Step >= targetStep);
        return selectedCount < targetTemplate.ExpertLimitCount + Owner.ExpandedExpert;
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
        if (expand == null)
        {
            Owner.SendErrorMessage(ErrorMessageType.ActabilityCanUpgradeSelectionCountLimit);
            return false;
        }

        if (expand.LifePoint > Owner.VocationPoint)
        {
            Owner.SendErrorMessage(ErrorMessageType.NotEnoughExpandItemAndMoney);
            return false;
        }

        if (expand.ItemId != 0 && expand.ItemCount != 0 && !Owner.Inventory.CheckItems(Items.SlotType.Inventory, expand.ItemId, expand.ItemCount))
        {
            Owner.SendErrorMessage(ErrorMessageType.NotEnoughExpandItem);
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
