using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>
/// Groups every integration test that calls <c>IndunGameData.Instance.Load</c>.
/// <para>
/// <c>IndunGameData</c> is a process-wide singleton whose <c>Load</c> replaces every catalog
/// collection. xUnit runs test classes in parallel, so two classes loading the same singleton at once
/// corrupt its non-concurrent collections and produce phantom duplicate diagnostics. Parallelization is
/// therefore disabled for this group; the tests stay independent of each other's execution order
/// because each one reloads the catalog it needs.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IndunRewardContentCollection
{
    public const string Name = "IndunRewardContent";
}
