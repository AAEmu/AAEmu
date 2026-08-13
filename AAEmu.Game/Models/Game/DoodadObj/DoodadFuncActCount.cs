using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// Applies <c>doodad_funcs.act_count</c>: that many successful uses must complete before
/// <see cref="DoodadFunc.NextPhase"/>. Progress is stored in <see cref="Doodad.Data"/>
/// (same convention as devote / react-devote).
/// </summary>
/// <remarks>
/// <c>act_count == 0</c> means no quota (advance on the first successful use). Abyssal crystals
/// 8411/8412 use 55; small crystals 9360–9362 use 30.
/// </remarks>
public static class DoodadFuncActCount
{
    /// <summary>
    /// When <paramref name="func"/> has a positive act count, record one use on <see cref="Doodad.Data"/>.
    /// Caller should broadcast via <see cref="PublishProgress"/> when this returns true.
    /// </summary>
    /// <returns>
    /// <c>true</c> if an act-count gate applied; then <paramref name="stayOnPhase"/> is
    /// <c>true</c> until the quota is reached (caller must not advance), or <c>false</c> when
    /// the quota was just met (caller may advance; Data was reset to 0).
    /// <c>false</c> return means no gate — caller uses the normal single-use advance path.
    /// </returns>
    public static bool TryApply(Doodad owner, DoodadFunc func, out bool stayOnPhase)
    {
        stayOnPhase = false;
        if (owner == null || func == null || func.Count <= 0)
            return false;

        var uses = owner.Data + 1;
        if (uses < func.Count)
        {
            owner.Data = uses;
            stayOnPhase = true;
            return true;
        }

        owner.Data = 0;
        stayOnPhase = false;
        return true;
    }

    public static void PublishProgress(Doodad owner)
    {
        if (owner == null)
            return;
        owner.BroadcastPacket(new SCDoodadChangedPacket(owner.ObjId, owner.Data), true);
    }
}
