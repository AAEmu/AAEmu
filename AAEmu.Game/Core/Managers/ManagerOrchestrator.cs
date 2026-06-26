using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers;

public class ManagerOrchestrator(IServiceProvider serviceProvider, IServiceCollection services)
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Builds batches of TInterface instances in topological order derived from constructor dependencies.
    /// Each batch can be executed in parallel; batches must be executed sequentially.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<TInterface>> BuildBatches<TInterface>()
    {
        // 1. Collect all concrete types registered as singletons that implement TInterface.
        //    Exclude factory-registered (ImplementationType == null) redirect registrations.
        var participatingTypes = services
            .Where(sd => sd.Lifetime == ServiceLifetime.Singleton
                      && sd.ImplementationType != null
                      && typeof(TInterface).IsAssignableFrom(sd.ImplementationType))
            .Select(sd => sd.ImplementationType!)
            .Distinct()
            .ToList();

        // 2. Build adjacency: for each type, which other participating types it depends on
        //    (derived from constructor parameters, excluding Lazy<T> to avoid false edges from
        //    circular-dependency break patterns).
        var typeSet = participatingTypes.ToHashSet();
        var deps = participatingTypes.ToDictionary(
            t => t,
            t => GetConstructorDeps(t, typeSet));

        // 3. Kahn's topological sort → parallel batches
        var batches = new List<List<Type>>();
        var remaining = typeSet.ToHashSet();
        while (remaining.Count > 0)
        {
            var batch = remaining
                .Where(t => deps[t].All(d => !remaining.Contains(d)))
                .ToList();
            if (batch.Count == 0)
                throw new InvalidOperationException(
                    $"Cycle detected in manager dependency graph among: {string.Join(", ", remaining.Select(t => t.Name))}");
            batches.Add(batch);
            foreach (var t in batch)
                remaining.Remove(t);
        }

        // 4. Resolve instances
        return batches
            .Select(batch => (IReadOnlyList<TInterface>)batch
                .Select(t => (TInterface)serviceProvider.GetRequiredService(t))
                .ToList())
            .ToList();
    }

    private static List<Type> GetConstructorDeps(Type type, HashSet<Type> participating)
    {
        var ctor = type.GetConstructors().MaxBy(c => c.GetParameters().Length);
        if (ctor is null) return [];
        return ctor.GetParameters()
            .Select(p => p.ParameterType)
            // Skip Lazy<T> — these exist specifically to break circular deps at resolution time
            .Where(pt => !(pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(Lazy<>)))
            // Map each interface parameter to the concrete type in the participating set
            .Select(pt => participating.FirstOrDefault(t => pt.IsAssignableFrom(t)))
            .Where(t => t is not null)
            .ToList()!;
    }

    /// <summary>Runs Load() on all ILoadable managers in dependency order, parallelising within each batch.</summary>
    public async Task RunLoadAsync()
    {
        // 10.0.2.13: manager Load() failures are NOT swallowed — they propagate and abort startup so any
        // remaining DB-migration mismatch surfaces immediately with a full stack trace.
        foreach (var batch in BuildBatches<ILoadable>())
            await Task.WhenAll(batch.Select(m => Task.Run(() => m.Load())));
    }

    /// <summary>Runs Initialize() on all IInitializable managers in dependency order, parallelising within each batch.</summary>
    public async Task RunInitializeAsync()
    {
        // 10.0.2.13: Initialize() failures propagate (not swallowed) so startup fails loudly on a real error.
        foreach (var batch in BuildBatches<IInitializable>())
            await Task.WhenAll(batch.Select(m => Task.Run(() => m.Initialize())));
    }
}
