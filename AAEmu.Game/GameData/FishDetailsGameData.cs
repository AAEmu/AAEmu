using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.FishSchools;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class FishDetailsGameData : Singleton<FishDetailsGameData>, IGameDataLoader
{
    private Dictionary<uint, FishDetails> _fishDetails;

    public void Load(SqliteConnection connection)
    {
        _fishDetails = [];

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM fish_details";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var template = new FishDetails { Id = reader.GetInt32("id") };
            template.Name = LocalizationManager.Instance.Get("fish_details", "name", template.Id, reader.GetString("name"));
            template.ItemId = reader.GetUInt32("item_id");
            template.MinWeight = reader.GetInt32("min_weight");
            template.MaxWeight = reader.GetInt32("max_weight");
            template.MinLength = reader.GetInt32("min_length");
            template.MaxLength = reader.GetInt32("max_length");

            _fishDetails.TryAdd(template.ItemId, template);
        }
    }

    public bool HasFishDetails(uint itemId)
    {
        return _fishDetails?.ContainsKey(itemId) == true;
    }

    public bool TryGetMaxWeight(uint itemId, out int maxWeight)
    {
        maxWeight = 0;
        if (_fishDetails == null || !_fishDetails.TryGetValue(itemId, out var details))
            return false;

        maxWeight = details.MaxWeight;
        return maxWeight > 0;
    }

    public void InitializeCaughtFish(BigFish fish)
    {
        ArgumentNullException.ThrowIfNull(fish);
        if (!HasFishDetails(fish.TemplateId))
            throw new InvalidOperationException($"Item {fish.TemplateId} has no fish details");

        var captureTime = DateTime.UtcNow;
        fish.CreateTime = captureTime;
        // The client treats the final detail qword as opaque. Retain AAEmu's established creation-time
        // encoding without claiming a native semantic for this field.
        fish.DetailQword = Helpers.UnixTime(captureTime);
        (fish.Length, fish.Weight) = GetFishSize(fish.TemplateId);
    }

    public bool TryCalculateSalePrice(BigFish fish, out long price)
    {
        price = 0;
        if (fish?.Template == null || !TryGetMaxWeight(fish.TemplateId, out var maxWeight))
            return false;

        var grade = ItemManager.Instance.GetGradeTemplate(fish.Grade);
        if (grade == null)
            return false;

        return FishSalePrice.TryCalculate(fish.Template.Refund, grade.RefundMultiplier, fish.Weight, maxWeight, out price);
    }

    public BigFish CreateTrophy(uint outputItemId, BigFish sourceFish)
    {
        ArgumentNullException.ThrowIfNull(sourceFish);
        if (!HasFishDetails(sourceFish.TemplateId))
            return null;

        var fish = ItemManager.Instance.Create<BigFish>(outputItemId, 1, 0);
        if (fish == null)
            return null;

        fish.MadeUnitId = sourceFish.TemplateId;
        fish.DetailQword = sourceFish.DetailQword;
        fish.Length = sourceFish.Length;
        fish.Weight = sourceFish.Weight;

        return fish;
    }

    public (float, float) GetFishSize(uint templateId)
    {
        var length = GetFishLength(templateId);
        var amount = length / _fishDetails[templateId].MaxLength;
        var weight = GetFishWeight(templateId, amount);

        return (length, weight);
    }

    public float GetFishLength(uint templateId)
    {
        return Random.Shared.Next(_fishDetails[templateId].MinLength, _fishDetails[templateId].MaxLength);
    }
    public float GetFishWeight(uint templateId)
    {
        return Random.Shared.Next(_fishDetails[templateId].MinWeight, _fishDetails[templateId].MaxWeight);
    }

    public float GetFishWeight(uint templateId, float amount)
    {

        return Lerp(_fishDetails[templateId].MinWeight, _fishDetails[templateId].MaxWeight, amount);
    }

    private static float Lerp(float v1, float v2, float t)
    {
        return v1 + (v2 - v1) * t;
    }

    public void PostLoad()
    {

    }
}
