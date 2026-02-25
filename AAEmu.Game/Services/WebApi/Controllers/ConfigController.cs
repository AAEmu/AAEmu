using System.Text.Json;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

/// <summary>
/// Exposes server configuration (rates, labor, etc.) for the admin panel
/// </summary>
internal class ConfigController : BaseController
{
    [WebApiGet("/api/config")]
    public HttpResponse GetConfig(HttpRequest request)
    {
        var cfg = AppConfiguration.Instance;
        var world = cfg.World ?? new WorldConfig();

        var result = new
        {
            world = new
            {
                expRate = world.ExpRate,
                honorRate = world.HonorRate,
                vocationRate = world.VocationRate,
                lootRate = world.LootRate,
                goldLootMultiplier = world.GoldLootMultiplier,
                growthRate = world.GrowthRate,
                actabilityRate = world.ActabilityRate,
                damageRate = world.DamageRate,
                regradeRate = world.RegradeRate,
                autoSaveInterval = world.AutoSaveInterval,
                daysForTaxPayment = world.DaysForTaxPayment,
                godMode = world.GodMode,
                geoDataMode = world.GeoDataMode,
                maxInstances = world.MaxInstances,
                targetPhysicsTps = world.TargetPhysicsTps,
                motd = world.MOTD,
                logoutMessage = world.LogoutMessage,
            },
            labor = cfg.Labor != null ? new
            {
                @default = cfg.Labor.Default,
                dailyLogin = cfg.Labor.DailyLogin,
                tickMinutes = cfg.Labor.TickMinutes,
                tickAmount = cfg.Labor.TickAmount,
                tickAmountPremium = cfg.Labor.TickAmountPremium,
            } : null,
            laborOffline = cfg.LaborOffline != null ? new
            {
                tickMinutes = cfg.LaborOffline.TickMinutes,
                tickAmount = cfg.LaborOffline.TickAmount,
                tickAmountPremium = cfg.LaborOffline.TickAmountPremium,
            } : null,
            credits = cfg.Credits != null ? new
            {
                @default = cfg.Credits.Default,
                dailyLogin = cfg.Credits.DailyLogin,
                tickMinutes = cfg.Credits.TickMinutes,
                tickAmount = cfg.Credits.TickAmount,
                tickAmountPremium = cfg.Credits.TickAmountPremium,
            } : null,
            loyalty = cfg.Loyalty != null ? new
            {
                @default = cfg.Loyalty.Default,
                dailyLogin = cfg.Loyalty.DailyLogin,
                tickMinutes = cfg.Loyalty.TickMinutes,
                tickAmount = cfg.Loyalty.TickAmount,
                tickAmountPremium = cfg.Loyalty.TickAmountPremium,
            } : null,
            specialty = cfg.Specialty != null ? new
            {
                maxSpecialtyRatio = cfg.Specialty.MaxSpecialtyRatio,
                minSpecialtyRatio = cfg.Specialty.MinSpecialtyRatio,
                ratioDecreasePerPack = cfg.Specialty.RatioDecreasePerPack,
                ratioIncreasePerTick = cfg.Specialty.RatioIncreasePerTick,
                ratioRegenTickMinutes = cfg.Specialty.RatioRegenTickMinutes,
                tradePackMailDelayInMinutes = cfg.Specialty.TradePackMailDelayInMinutes,
            } : null,
            account = cfg.Account != null ? new
            {
                accessLevelDefault = cfg.Account.AccessLevelDefault,
                accessLevelFirstAccount = cfg.Account.AccessLevelFirstAccount,
                accessLevelFirstCharacter = cfg.Account.AccessLevelFirstCharacter,
            } : null,
        };

        return OkJson(result);
    }

    [WebApiPost("/api/config/rates")]
    public HttpResponse SetRates(HttpRequest request, System.Text.RegularExpressions.MatchCollection matches)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(request.Body);
            var world = AppConfiguration.Instance.World;

            if (json.TryGetProperty("expRate", out var expRate) && expRate.ValueKind == JsonValueKind.Number)
                world.ExpRate = Math.Clamp(expRate.GetDouble(), 0.1, 1000.0);

            if (json.TryGetProperty("honorRate", out var honorRate) && honorRate.ValueKind == JsonValueKind.Number)
                world.HonorRate = Math.Clamp(honorRate.GetDouble(), 0.1, 1000.0);

            if (json.TryGetProperty("vocationRate", out var vocationRate) && vocationRate.ValueKind == JsonValueKind.Number)
                world.VocationRate = Math.Clamp(vocationRate.GetDouble(), 0.1, 1000.0);

            if (json.TryGetProperty("lootRate", out var lootRate) && lootRate.ValueKind == JsonValueKind.Number)
                world.LootRate = Math.Clamp(lootRate.GetDouble(), 0.1, 1000.0);

            if (json.TryGetProperty("goldLootMultiplier", out var goldLoot) && goldLoot.ValueKind == JsonValueKind.Number)
                world.GoldLootMultiplier = Math.Clamp(goldLoot.GetDouble(), 0.1, 1000.0);

            if (json.TryGetProperty("growthRate", out var growthRate) && growthRate.ValueKind == JsonValueKind.Number)
                world.GrowthRate = Math.Clamp(growthRate.GetDouble(), 0.1, 1000.0);

            if (json.TryGetProperty("actabilityRate", out var actRate) && actRate.ValueKind == JsonValueKind.Number)
                world.ActabilityRate = Math.Clamp(actRate.GetDouble(), 0.1, 1000.0);

            if (json.TryGetProperty("damageRate", out var damageRate) && damageRate.ValueKind == JsonValueKind.Number)
                world.DamageRate = Math.Clamp(damageRate.GetDouble(), 0.01, 100.0);

            if (json.TryGetProperty("regradeRate", out var regradeRate) && regradeRate.ValueKind == JsonValueKind.Number)
                world.RegradeRate = Math.Clamp(regradeRate.GetDouble(), 0.01, 100.0);

            if (json.TryGetProperty("autoSaveInterval", out var autoSave) && autoSave.ValueKind == JsonValueKind.Number)
                world.AutoSaveInterval = Math.Clamp(autoSave.GetDouble(), 1.0, 60.0);

            if (json.TryGetProperty("godMode", out var godMode) && (godMode.ValueKind == JsonValueKind.True || godMode.ValueKind == JsonValueKind.False))
                world.GodMode = godMode.GetBoolean();

            if (json.TryGetProperty("motd", out var motd) && motd.ValueKind == JsonValueKind.String)
                world.MOTD = motd.GetString() ?? "";

            if (json.TryGetProperty("logoutMessage", out var logoutMsg) && logoutMsg.ValueKind == JsonValueKind.String)
                world.LogoutMessage = logoutMsg.GetString() ?? "";

            return OkJson(new { success = true, message = "Rates updated successfully (in-memory, not persisted to disk)" });
        }
        catch (Exception ex)
        {
            return BadRequestJson(new { success = false, message = ex.Message });
        }
    }

    [WebApiGet("/api/regrade/grades")]
    public HttpResponse GetRegradeGrades(HttpRequest request)
    {
        var grades = ItemManager.Instance.GetAllGradeTemplates();
        var result = grades.Values
            .OrderBy(g => g.GradeOrder)
            .Select(g => new
            {
                grade = g.Grade,
                gradeOrder = g.GradeOrder,
                enchantSuccessRatio = g.EnchantSuccessRatio,
                enchantGreatSuccessRatio = g.EnchantGreatSuccessRatio,
                enchantBreakRatio = g.EnchantBreakRatio,
                enchantDowngradeRatio = g.EnchantDowngradeRatio,
                enchantDowngradeMin = g.EnchantDowngradeMin,
                enchantDowngradeMax = g.EnchantDowngradeMax,
                enchantCost = g.EnchantCost,
            })
            .ToArray();

        return OkJson(result);
    }

    [WebApiPost("/api/regrade/grades")]
    public HttpResponse SetRegradeGrades(HttpRequest request, System.Text.RegularExpressions.MatchCollection matches)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(request.Body);

            if (json.ValueKind != JsonValueKind.Array)
                return BadRequestJson(new { success = false, message = "Expected an array of grade modifications" });

            var modified = 0;
            foreach (var entry in json.EnumerateArray())
            {
                if (!entry.TryGetProperty("grade", out var gradeEl) || gradeEl.ValueKind != JsonValueKind.Number)
                    continue;

                var gradeId = gradeEl.GetInt32();
                var template = ItemManager.Instance.GetGradeTemplate(gradeId);
                if (template == null)
                    continue;

                if (entry.TryGetProperty("enchantSuccessRatio", out var sr) && sr.ValueKind == JsonValueKind.Number)
                    template.EnchantSuccessRatio = Math.Clamp(sr.GetInt32(), 0, 10000);

                if (entry.TryGetProperty("enchantGreatSuccessRatio", out var gsr) && gsr.ValueKind == JsonValueKind.Number)
                    template.EnchantGreatSuccessRatio = Math.Clamp(gsr.GetInt32(), 0, 10000);

                if (entry.TryGetProperty("enchantBreakRatio", out var br) && br.ValueKind == JsonValueKind.Number)
                    template.EnchantBreakRatio = Math.Clamp(br.GetInt32(), 0, 10000);

                if (entry.TryGetProperty("enchantDowngradeRatio", out var dr) && dr.ValueKind == JsonValueKind.Number)
                    template.EnchantDowngradeRatio = Math.Clamp(dr.GetInt32(), 0, 10000);

                modified++;
            }

            return OkJson(new { success = true, message = $"Modified {modified} grade template(s) in-memory", modified });
        }
        catch (Exception ex)
        {
            return BadRequestJson(new { success = false, message = ex.Message });
        }
    }
}
