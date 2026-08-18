using System.Globalization;

namespace AAEmu.Game.Models.Game.StreamAoi;

/// <summary>
/// Category radii for mirror SC. Env overrides keep the old kill-switches.
/// <c>AAEMU_MIRROR_NPC_AOI</c> ambient enter (exit = enter+5 when unset).
/// <c>AAEMU_MIRROR_LARGE_AOI_ENTER</c> / <c>_EXIT</c>.
/// <c>AAEMU_MIRROR_SHIP_AOI_ENTER</c> / <c>_EXIT</c>.
/// <c>AAEMU_MIRROR_PRIORITY_AOI</c> event enter+exit.
/// </summary>
public static class StreamAoiTable
{
    private static StreamAoiConfig _config = CreateDefault();
    private static HashSet<uint> _largeTemplates = [.._config.LargeNpcTemplateIds];
    private static HashSet<uint> _largeModels = [.._config.LargeModelIds];

    public static StreamAoiConfig Config => _config;

    public static void ReplaceConfig(StreamAoiConfig config)
    {
        _config = Normalize(config ?? CreateDefault());
        _largeTemplates = [.._config.LargeNpcTemplateIds ?? []];
        _largeModels = [.._config.LargeModelIds ?? []];
        ApplyEnvOverrides(_config);
    }

    public static StreamAoiConfig CreateDefault()
    {
        var cfg = new StreamAoiConfig();
        ApplyEnvOverrides(cfg);
        return cfg;
    }

    public static StreamAoiBand Band(StreamAoiCategory category) =>
        category switch
        {
            StreamAoiCategory.Large => _config.Large,
            StreamAoiCategory.Ship => _config.Ship,
            StreamAoiCategory.Event => _config.Event,
            _ => _config.Ambient
        };

    public static bool IsInside(StreamAoiCategory category, float distanceSq, bool alreadyStreamed)
    {
        // Retail: hull unselects at Ship exit; sails/cannons stay until the region drops.
        if (category == StreamAoiCategory.Part)
            return true;

        var band = Band(category);
        return distanceSq <= (alreadyStreamed ? band.ExitSq : band.EnterSq);
    }

    public static bool IsLargeNpc(uint templateId, uint modelId) =>
        (templateId != 0 && _largeTemplates.Contains(templateId))
        || (modelId != 0 && _largeModels.Contains(modelId));

    /// <summary>
    /// Linear map of a measured reference onto another height. Not used as live metres
    /// (sea hulls and bosses share the measured 225/248 band).
    /// </summary>
    public static StreamAoiBand InterpolateHeight(
        float heightMetres,
        float referenceHeight,
        StreamAoiBand reference,
        StreamAoiBand ambient,
        float personHeight = 1.8f)
    {
        if (heightMetres <= personHeight || referenceHeight <= personHeight)
            return ambient.Clone();

        var t = (heightMetres - personHeight) / (referenceHeight - personHeight);
        t = Math.Clamp(t, 0f, 1f);
        return new StreamAoiBand
        {
            EnterMetres = ambient.EnterMetres + (reference.EnterMetres - ambient.EnterMetres) * t,
            ExitMetres = ambient.ExitMetres + (reference.ExitMetres - ambient.ExitMetres) * t
        };
    }

    private static StreamAoiConfig Normalize(StreamAoiConfig cfg)
    {
        cfg.Ambient = ClampBand(cfg.Ambient, 105f, 110f, minEnter: 20f);
        cfg.Large = ClampBand(cfg.Large, 225f, 248f, minEnter: 50f);
        cfg.Ship = ClampBand(cfg.Ship, 225f, 248f, minEnter: 50f);
        cfg.Event = ClampBand(cfg.Event, 700f, 700f, minEnter: 50f);
        cfg.LargeNpcTemplateIds ??= [];
        cfg.LargeModelIds ??= [];
        return cfg;
    }

    private static StreamAoiBand ClampBand(StreamAoiBand band, float enter, float exit, float minEnter)
    {
        band ??= new StreamAoiBand { EnterMetres = enter, ExitMetres = exit };
        if (band.EnterMetres < minEnter)
            band.EnterMetres = enter;
        if (band.ExitMetres < band.EnterMetres)
            band.ExitMetres = band.EnterMetres;
        return band;
    }

    private static void ApplyEnvOverrides(StreamAoiConfig cfg)
    {
        if (TryMetres("AAEMU_MIRROR_NPC_AOI", 20f, out var ambientEnter))
        {
            cfg.Ambient.EnterMetres = ambientEnter;
            if (!TryMetres("AAEMU_MIRROR_NPC_AOI_EXIT", 20f, out var ambientExit))
                ambientExit = ambientEnter + 5f;
            cfg.Ambient.ExitMetres = Math.Max(ambientExit, cfg.Ambient.EnterMetres);
        }

        if (TryMetres("AAEMU_MIRROR_LARGE_AOI_ENTER", 50f, out var largeEnter))
            cfg.Large.EnterMetres = largeEnter;
        if (TryMetres("AAEMU_MIRROR_LARGE_AOI_EXIT", 50f, out var largeExit))
            cfg.Large.ExitMetres = Math.Max(largeExit, cfg.Large.EnterMetres);

        if (TryMetres("AAEMU_MIRROR_SHIP_AOI_ENTER", 50f, out var shipEnter))
            cfg.Ship.EnterMetres = shipEnter;
        if (TryMetres("AAEMU_MIRROR_SHIP_AOI_EXIT", 50f, out var shipExit))
            cfg.Ship.ExitMetres = Math.Max(shipExit, cfg.Ship.EnterMetres);

        if (TryMetres("AAEMU_MIRROR_PRIORITY_AOI", 50f, out var eventMetres))
        {
            cfg.Event.EnterMetres = eventMetres;
            cfg.Event.ExitMetres = eventMetres;
        }
    }

    private static bool TryMetres(string env, float min, out float metres)
    {
        metres = 0f;
        var raw = Environment.GetEnvironmentVariable(env);
        return !string.IsNullOrEmpty(raw)
               && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out metres)
               && metres >= min;
    }
}
