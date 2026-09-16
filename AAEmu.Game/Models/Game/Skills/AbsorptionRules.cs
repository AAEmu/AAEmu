namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What one absorption buff did to one hit: the damage it could not take, the damage it did take, the
/// charge it is left holding, and whether that charge is now spent.
/// </summary>
public readonly record struct AbsorptionOutcome(int Remaining, int Absorbed, int Charge, bool Consumed);

/// <summary>
/// <c>buffs.damage_absorption_type_id</c> and <c>buffs.damage_absorption_per_hit</c>: how a shield decides
/// what one hit costs it.
/// </summary>
/// <remarks>
/// <c>damage_absorption_type_id</c> is <c>enum_damage_absorption_type</c>: 0 none, 1 count, 2 amount. It
/// names the thing <c>Buff.Charge</c> holds, and the two populated kinds are read differently:
/// <list type="bullet">
/// <item><b>1 count</b> (16 rows) — Charge is a number of hits. 27892 피안의 편린 is "피해를 흡수할 때마다
/// 보호막이 하나씩 사라집니다" at charge 10 and <c>per_hit 7000</c>, and 127 의기 충전 absorbs "5000 이하의
/// 피해" at charge 1 with <c>max_charge 3</c>; <c>per_hit</c> is the ceiling on one hit, and one hit always
/// costs one count. The nine 은신 rows (6160, 6183, 6243, 29217, 29446, 29938, 30905, 30908, 30911) carry
/// count with <c>per_hit 0</c> and <c>charge 0</c>: they absorb nothing and end on the first hit, which is
/// exactly what "해당 강화는 타격시 해제됩니다" (this buff is removed when struck) asks for — and what the
/// old charge-arithmetic happened to do as well.</item>
/// <item><b>2 amount</b> (215 rows) — Charge is a pool of damage. 1011 보호막 holds 103 and "103 의 피해를
/// 흡수하면 사라집니다", 429 보호막 (5단계) holds a 50,000 pool. Only 79 빛의 보호막 (2레벨) and 221 빛의
/// 갑옷 (1레벨) put a number in <c>per_hit</c> instead (315 and 296) with no pool above 1, and both describe
/// themselves as absorbing exactly that, so for this kind the pool is whichever of the two columns is
/// larger. Every row with <c>per_hit 0</c> resolves to exactly the charge arithmetic that was there before.</item>
/// <item><b>0 none with a per_hit</b> (71 rows) — no pool to draw on at all: <c>per_hit</c> is a per-hit
/// ceiling that holds for as long as the buff does. 389 마상 수비 absorbs 10,000 a hit for 5 s
/// ("원거리 피해를 받지 않으며") and the 돌파 family is the same shape. These rows were not absorbed at all
/// before, because the filter only looked at the type column — which is the only group of the 302 shields
/// whose behaviour was not already partly there.</item>
/// </list>
/// </remarks>
public static class AbsorptionRules
{
    /// <summary><c>enum_damage_absorption_type</c> 1: <c>Buff.Charge</c> counts hits.</summary>
    public const uint Count = 1;

    /// <summary><c>enum_damage_absorption_type</c> 2: <c>Buff.Charge</c> is a pool of damage.</summary>
    public const uint Amount = 2;

    /// <summary>
    /// Whether a shield of this shape takes part in absorbing at all. A row with neither column set is not
    /// a shield, so it stays out of the damage path — which is the 30,423 rows that author type 0.
    /// </summary>
    public static bool IsShield(uint typeId, int perHit) => typeId > 0 || perHit > 0;

    /// <summary>
    /// Applies one hit to one shield.
    /// </summary>
    /// <param name="typeId">The row's <c>damage_absorption_type_id</c>.</param>
    /// <param name="perHit">The row's <c>damage_absorption_per_hit</c>.</param>
    /// <param name="charge">The shield's current <c>Buff.Charge</c>.</param>
    /// <param name="damage">The hit, already reduced by anything that came before this shield.</param>
    public static AbsorptionOutcome Apply(uint typeId, int perHit, int charge, int damage)
    {
        var held = Math.Max(0, charge);
        if (!IsShield(typeId, perHit))
            return new AbsorptionOutcome(damage, 0, held, false);

        var incoming = Math.Max(0, damage);
        var cap = perHit > 0 ? Math.Min(incoming, perHit) : incoming;

        switch (typeId)
        {
            case Count:
            {
                // One hit costs one count whether or not the ceiling left anything to absorb: that is what
                // makes a per_hit 0, charge 0 은신 buff end on the first hit it takes.
                var absorbed = perHit > 0 ? cap : 0;
                var left = held - 1;
                return new AbsorptionOutcome(incoming - absorbed, absorbed, Math.Max(0, left), left <= 0);
            }

            case Amount:
            {
                // per_hit stands in for the pool when the row authored no charge of its own (79: charge 0,
                // per_hit 315; 221: charge 1, per_hit 296), so the pool is the larger of the two.
                var pool = Math.Max(held, Math.Max(0, perHit));
                var absorbed = Math.Min(cap, pool);
                // The pool is only written back once a hit has actually spent something, so a shield that
                // has absorbed nothing keeps the charge it was given.
                var left = absorbed > 0 ? pool - absorbed : held;
                return new AbsorptionOutcome(incoming - absorbed, absorbed, left, left <= 0);
            }

            default:
            {
                // Type 0 with a per_hit: a ceiling with no pool behind it, so nothing is consumed.
                return new AbsorptionOutcome(incoming - cap, cap, held, false);
            }
        }
    }
}
