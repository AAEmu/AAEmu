using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.Game.Models.Game.OpenPortal;

public static class OpenPortalNpcRules
{
    public static bool TryResolve(OpenPortalEffect effect, out uint enterNpcId, out uint exitNpcId)
    {
        enterNpcId = effect?.EnterPortalNpcId ?? 0;
        exitNpcId = effect?.ExitPortalNpcId ?? 0;
        return effect != null
            && float.IsFinite(effect.Distance)
            && effect.Distance >= 0f
            && enterNpcId != 0
            && exitNpcId != 0;
    }
}
