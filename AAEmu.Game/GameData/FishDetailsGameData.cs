using System;
using System.Collections.Generic;

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
            var template = new FishDetails();
            template.Id = reader.GetInt32("id");
            template.Name = LocalizationManager.Instance.Get("fish_details", "name", template.Id, reader.GetString("name"));
            template.ItemId = reader.GetUInt32("item_id");
            template.MinWeight = reader.GetInt32("min_weight");
            template.MaxWeight = reader.GetInt32("max_weight");
            template.MinLength = reader.GetInt32("min_length");
            template.MaxLength = reader.GetInt32("max_length");

            _fishDetails.TryAdd(template.ItemId, template);
        }
    }

    public BigFish Create(uint templateId, int count = 1, byte grade = 0, bool generateId = true)
    {
        //var itemTemplate = ItemManager.Instance.GetItemTemplateFromItemId(trophy.TemplateId);
        //var newItem = ItemManager.Instance.Create(itemTemplate.Id, trophy.Count, trophy.Grade);
        //character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Fishing, newItem);

        var template = ItemManager.Instance.GetItemTemplateFromItemId(templateId); ;
        if (template == null)
        {
            return null;
        }

        var newItem = ItemManager.Instance.Create(templateId, 1, 0);

        var fish = new BigFish(newItem.Id, template, 1);
        (fish.Length, fish.Weight) = GetFishSize(templateId);

        return fish;
    }

    public BigFish Create(Item item)
    {
        var template = ItemManager.Instance.GetItemTemplateFromItemId(item.TemplateId);
        if (template == null)
        {
            return null;
        }

        var fish = new BigFish(item.Id, template, 1);
        fish.CreateTime = DateTime.UtcNow;
        (fish.Length, fish.Weight) = GetFishSize(item.MadeUnitId);

        var byteArray = new byte[16];
        Buffer.BlockCopy(BitConverter.GetBytes(fish.Weight), 0, byteArray, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(fish.Length), 0, byteArray, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(Helpers.UnixTime(fish.CreateTime)), 0, byteArray, 8, 8);

        fish.Detail = byteArray;

        ItemManager.Instance.AddItem(fish);

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
        return Rand.Next(_fishDetails[templateId].MinLength, _fishDetails[templateId].MaxLength);
    }
    public float GetFishWeight(uint templateId)
    {
        return Rand.Next(_fishDetails[templateId].MinWeight, _fishDetails[templateId].MaxWeight);
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
