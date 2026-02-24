using AAEmu.Game.Core.Managers;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Unit tests for ManagerOrchestrator — DAG building, topological sort, Lazy skipping, cycle detection.
/// These tests use private stub types so they have no external DB/file dependencies.
/// </summary>
public class ManagerOrchestratorTests
{
    // -------------------------------------------------------------------------
    // Stub interfaces / concrete types used across tests
    // -------------------------------------------------------------------------

    private interface IA : ILoadable;
    private interface IB : ILoadable;
    private interface IC : ILoadable;

    /// <summary>A has no dependencies.</summary>
    private class A : IA { public void Load() { } }

    /// <summary>B depends on A (takes IA in its constructor).</summary>
    private class B(IA a) : IB { public void Load() { } }

    /// <summary>C depends on B (takes IB in its constructor).</summary>
    private class C(IB b) : IC { public void Load() { } }

    // For cycle detection
    private interface IX : ILoadable;
    private interface IY : ILoadable;

    private class CycleX(IY y) : IX { public void Load() { } }
    private class CycleY(IX x) : IY { public void Load() { } }

    // For Lazy<T> skip test
    private interface IP : ILoadable;
    private interface IQ : ILoadable;

    /// <summary>P takes Q as Lazy&lt;IQ&gt; — should NOT create a dependency edge.</summary>
    private class P(Lazy<IQ> q) : IP { public void Load() { } }
    private class Q : IQ { public void Load() { } }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (ManagerOrchestrator orchestrator, IServiceProvider sp) Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddSingleton<IServiceCollection>(_ => services);
        services.AddSingleton<ManagerOrchestrator>();
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ManagerOrchestrator>(), sp);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildBatches_ProducesCorrectTopologicalOrder()
    {
        // A → (no deps)  →  batch 0
        // B → depends on A → batch 1
        // C → depends on B → batch 2
        var (orchestrator, _) = Build(services =>
        {
            services.AddSingleton<A>();
            services.AddSingleton<IA>(sp => sp.GetRequiredService<A>());
            services.AddSingleton<B>();
            services.AddSingleton<IB>(sp => sp.GetRequiredService<B>());
            services.AddSingleton<C>();
            services.AddSingleton<IC>(sp => sp.GetRequiredService<C>());
        });

        var batches = orchestrator.BuildBatches<ILoadable>();

        Assert.Equal(3, batches.Count);

        // Each batch should have exactly one manager
        Assert.Single(batches[0]);
        Assert.Single(batches[1]);
        Assert.Single(batches[2]);

        // Verify order: A first, then B, then C
        Assert.IsType<A>(batches[0][0]);
        Assert.IsType<B>(batches[1][0]);
        Assert.IsType<C>(batches[2][0]);
    }

    [Fact]
    public void BuildBatches_IndependentManagersAreInSameBatch()
    {
        // A and Q have no constructor deps on each other → both in batch 0
        var (orchestrator, _) = Build(services =>
        {
            services.AddSingleton<A>();
            services.AddSingleton<IA>(sp => sp.GetRequiredService<A>());
            services.AddSingleton<Q>();
            services.AddSingleton<IQ>(sp => sp.GetRequiredService<Q>());
        });

        var batches = orchestrator.BuildBatches<ILoadable>();

        Assert.Single(batches);
        Assert.Equal(2, batches[0].Count);
    }

    [Fact]
    public void BuildBatches_ThrowsOnCycle()
    {
        // CycleX depends on IY, CycleY depends on IX → cycle
        var (orchestrator, _) = Build(services =>
        {
            services.AddSingleton<CycleX>();
            services.AddSingleton<IX>(sp => sp.GetRequiredService<CycleX>());
            services.AddSingleton<CycleY>();
            services.AddSingleton<IY>(sp => sp.GetRequiredService<CycleY>());
        });

        var ex = Assert.Throws<InvalidOperationException>(() => orchestrator.BuildBatches<ILoadable>());
        Assert.Contains("Cycle detected", ex.Message);
    }

    [Fact]
    public void BuildBatches_SkipsLazyDependencies()
    {
        // P takes Lazy<IQ> — this should NOT be treated as a dependency on Q.
        // Therefore both P and Q have no unresolved deps and appear in batch 0.
        var (orchestrator, _) = Build(services =>
        {
            services.AddSingleton<Q>();
            services.AddSingleton<IQ>(sp => sp.GetRequiredService<Q>());
            // Register Lazy<IQ> explicitly so DI can satisfy P's constructor
            services.AddSingleton(sp => new Lazy<IQ>(sp.GetRequiredService<IQ>));
            services.AddSingleton<P>();
            services.AddSingleton<IP>(sp => sp.GetRequiredService<P>());
        });

        var batches = orchestrator.BuildBatches<ILoadable>();

        // Both should be in the first (and only) batch
        Assert.Single(batches);
        Assert.Equal(2, batches[0].Count);
    }

    [Fact]
    public void BuildBatches_EmptyRegistrations_ReturnsEmptyList()
    {
        var (orchestrator, _) = Build(_ => { });

        var batches = orchestrator.BuildBatches<ILoadable>();

        Assert.Empty(batches);
    }

    [Fact]
    public void BuildBatches_ExcludesFactoryRegistrations()
    {
        // Only factory-registered (no ImplementationType) — should be excluded
        var (orchestrator, _) = Build(services =>
        {
            // Factory lambda → ImplementationType == null → excluded
            services.AddSingleton<IA>(_ => new A());
        });

        var batches = orchestrator.BuildBatches<ILoadable>();

        Assert.Empty(batches);
    }

    [Fact]
    public async Task RunLoadAsync_CallsLoadOnAllManagers()
    {
        var loadCalled = new List<string>();

        // Register the list as a service so TrackingA can be type-registered (ImplementationType != null).
        // Factory lambdas produce ImplementationType == null and are excluded by the orchestrator.
        var services = new ServiceCollection();
        services.AddSingleton(loadCalled);
        services.AddSingleton<TrackingA>();
        services.AddSingleton<IServiceCollection>(_ => services);
        services.AddSingleton<ManagerOrchestrator>();
        var sp = services.BuildServiceProvider();
        var orchestrator = sp.GetRequiredService<ManagerOrchestrator>();

        await orchestrator.RunLoadAsync();

        Assert.Contains("A", loadCalled);
    }

    // Tracking helper — injected via type registration so ImplementationType is set in the descriptor.
    private class TrackingA(List<string> log) : ILoadable
    {
        public void Load() => log.Add("A");
    }
}
