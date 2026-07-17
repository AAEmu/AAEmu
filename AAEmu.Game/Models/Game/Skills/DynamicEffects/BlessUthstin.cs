using Newtonsoft.Json.Linq;

namespace AAEmu.Game.Models.Game.Skills.DynamicEffects;

public class BlessUthstin
{
    public string Effect { get; set; }
    public string ItemFunction { get; set; }
    public int Rise { get; set; }
    public int Drop { get; set; }
    public RiseWeight RiseWeight { get; set; }
    public DropWeight DropWeight { get; set; }

    public BlessUthstin(JObject obj, string json)
    {
        Effect = obj.GetValue("effect")?.ToString() ?? string.Empty;
        ItemFunction = obj.GetValue("item_function")?.ToString() ?? string.Empty;
        Rise = obj.GetValue("rise")?.ToObject<int>() ?? 0;
        Drop = obj.GetValue("drop")?.ToObject<int>() ?? 0;

        RiseWeight = obj.TryGetValue("riseweight", out var riseweight)
            ? riseweight.ToObject<RiseWeight>()
            : new RiseWeight();

        DropWeight = obj.TryGetValue("dropweight", out var dropweight)
            ? dropweight.ToObject<DropWeight>()
            : new DropWeight();
    }
}

public class RiseWeight : AttributeWeight { }

public class DropWeight : AttributeWeight { }
