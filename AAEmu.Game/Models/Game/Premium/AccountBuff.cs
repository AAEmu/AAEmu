namespace AAEmu.Game.Models.Game.Premium;

/// <summary>
/// Row of <c>account_buffs</c> - a paid membership and the labor it adds on top of the premium grade.
/// The row id is the <c>extraKind</c> the client matches an account attribute against, see
/// <see cref="AccountMembership"/>.
/// </summary>
/// <remarks>
/// The client sums these onto premium_grades itself. With memberships 1001 (上古会员, +10 online,
/// +10 offline, +3000 max) and 1002 (生活会员, +5 online) both active on a grade-6 account it displays
/// a rate of 15+10+5 = 30 and an account pool cap of 6000+3000+0 = 9000, which is exactly what the
/// live client shows. The server has to do the same arithmetic or it pays out less than the client
/// promises.
/// </remarks>
public class AccountBuff
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint BuffId { get; set; }

    /// <summary>Added to (or, with <see cref="ReplacePremiumOnlineLp"/>, substituted for) the grade's online rate.</summary>
    public int OnlineLaborPower { get; set; }
    public bool ReplacePremiumOnlineLp { get; set; }

    /// <summary>Added to (or, with <see cref="ReplacePremiumOfflineLp"/>, substituted for) the grade's offline rate.</summary>
    public int OfflineLaborPower { get; set; }
    public bool ReplacePremiumOfflineLp { get; set; }

    /// <summary>Raises the ACCOUNT pool cap ("Offline Labor").</summary>
    public int AddMaxLp { get; set; }

    /// <summary>Raises the SERVER-LOCAL pool cap ("Online Labor").</summary>
    public int AddMaxLocalLp { get; set; }
}

/// <summary>
/// The labor numbers a premium grade and the account's active memberships add up to.
/// </summary>
public class LaborAllowance
{
    public int OnlineRate { get; set; }
    public int OfflineRate { get; set; }
    public int MaxLabor { get; set; }
    public int MaxLocalLabor { get; set; }
}
