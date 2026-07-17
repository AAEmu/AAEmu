using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AAEmu.Game.Models.Game.Skills.DynamicEffects;

public class ItemSelection
{
    [JsonProperty("item")]
    public ulong Item { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }
}

public class SelectiveItem
{
    public string Effect { get; set; }
    public int Select { get; set; }
    public int ConsumeItemCount { get; set; }
    public List<ItemSelection> ItemSelections { get; set; }

    public SelectiveItem(JObject obj)
    {
        Effect = obj.GetValue("effect")?.ToString() ?? string.Empty;
        Select = obj.GetValue("select")?.ToObject<int>() ?? 0;
        ConsumeItemCount = obj.GetValue("consume_item_count")?.ToObject<int>() ?? 0;
        ItemSelections = obj.GetValue("list")?.ToObject<List<ItemSelection>>() ?? new List<ItemSelection>();
    }
}
