using System.Diagnostics.CodeAnalysis;
using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

[NotInParallel]
public class SingletonTests
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
    private sealed class LeafSingleton : Singleton<LeafSingleton>
    {
        // parameterless constructor — DI fallback works
    }

    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
    private sealed class DependentSingleton : Singleton<DependentSingleton>
    {
        // No parameterless constructor — must be resolved from DI
        public DependentSingleton([SuppressMessage("ReSharper", "UnusedParameter.Local")] object dep) { }
    }

    [Test]
    public async Task Instance_WithParameterlessConstructor_ReturnsInstance()
    {
        // Reset by assigning via DI path isn't needed — just ensure it constructs
        // (may already be set from a previous test run; that's fine)
        var instance = LeafSingleton.Instance;
        await Assert.That(instance).IsNotNull();
    }

    [Test]
    public void Instance_WithNoParameterlessConstructor_ThrowsInvalidOperationException()
    {
        // SingletonContainer.ServiceProvider is null in unit tests
        // so OnInit() reflection fallback is invoked, which should throw
        Assert.Throws<InvalidOperationException>(() => _ = DependentSingleton.Instance);
    }
}