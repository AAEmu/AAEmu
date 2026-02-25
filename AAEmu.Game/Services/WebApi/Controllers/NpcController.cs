using System.Text.RegularExpressions;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

/// <summary>
/// NPC controller for the WebApi — provides live NPC status for boss monitoring
/// </summary>
internal class NpcController : BaseController
{
    /// <summary>
    /// Collects all NPCs from all world instances
    /// </summary>
    private static List<Npc> GetAllNpcs()
    {
        var result = new List<Npc>();
        foreach (var world in WorldManager.Instance.GetWorlds())
        {
            result.AddRange(world.GetAllNpcs());
        }
        return result;
    }

    /// <summary>
    /// GET /api/npc/bytemplate/{templateId} — returns all live NPCs matching a template ID
    /// </summary>
    [WebApiGet("/api/npc/bytemplate/(\\d+)")]
    public HttpResponse GetNpcsByTemplate(HttpRequest request, MatchCollection matches)
    {
        if (!uint.TryParse(matches[0].Groups[1].Value, out var templateId))
            return BadRequestJson(new { message = "Invalid template ID" });

        var allNpcs = GetAllNpcs();
        var results = allNpcs
            .Where(n => n.TemplateId == templateId)
            .Select(MapNpcToDto)
            .ToList();

        return OkJson(results);
    }

    /// <summary>
    /// GET /api/npc/live-bosses — returns all live NPCs that match a list of known boss template IDs
    /// </summary>
    [WebApiGet("/api/npc/live-bosses")]
    public HttpResponse GetLiveBosses(HttpRequest request)
    {
        // Known boss template IDs
        uint[] bossTemplateIds = [7607, 7606, 9444, 6330, 12411, 13451, 8837, 8952, 8766, 8554, 11180, 808, 13279, 13285];

        var allNpcs = GetAllNpcs();
        var results = allNpcs
            .Where(n => bossTemplateIds.Contains(n.TemplateId))
            .Select(MapNpcToDto)
            .ToList();

        return OkJson(results);
    }

    private static object MapNpcToDto(Npc n)
    {
        // Get top aggro info safely (extension throws if table is empty)
        string topAggroName = null;
        int topAggroDamage = 0;
        if (!n.AggroTable.IsEmpty)
        {
            var topAggroObjId = n.AggroTable.GetTopDamageAggroAbuserObjId();
            if (topAggroObjId > 0 && n.AggroTable.TryGetValue(topAggroObjId, out var topAggro))
            {
                topAggroDamage = topAggro.DamageAggro;
                topAggroName = topAggro.Owner?.Name;
            }
        }

        // Build aggro table summary (top 10)
        var aggroEntries = n.AggroTable
            .OrderByDescending(kv => kv.Value.TotalAggro)
            .Take(10)
            .Select(kv => new
            {
                objId = kv.Key,
                name = kv.Value.Owner?.Name ?? "Unknown",
                damageAggro = kv.Value.DamageAggro,
                healAggro = kv.Value.HealAggro,
                totalAggro = kv.Value.TotalAggro,
            })
            .ToList();

        return new
        {
            objId = n.ObjId,
            templateId = n.TemplateId,
            name = n.Name,
            level = n.Level,
            hp = n.Hp,
            maxHp = n.MaxHp,
            hpp = n.Hpp,
            mp = n.Mp,
            maxMp = n.MaxMp,
            isDead = n.IsDead,
            position = new
            {
                x = n.Transform?.World?.Position.X ?? 0,
                y = n.Transform?.World?.Position.Y ?? 0,
                z = n.Transform?.World?.Position.Z ?? 0,
            },
            stats = new
            {
                dps = n.Dps,
                armor = n.Armor,
                magicResist = n.MagicResistance,
                str = n.Str,
                dex = n.Dex,
                sta = n.Sta,
                @int = n.Int,
                spi = n.Spi,
            },
            combat = new
            {
                topAggroName,
                topAggroDamage,
                aggroTableSize = n.AggroTable.Count,
                aggroEntries,
                currentTarget = n.CurrentAggroTarget?.Name,
                currentTargetObjId = n.CurrentAggroTarget?.ObjId ?? 0,
            },
        };
    }
}
