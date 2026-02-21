using System.Diagnostics.CodeAnalysis;
using AAEmu.Commons.Utils;
using Xunit;

namespace AAEmu.UnitTests.Commons.Utils;

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

    [Fact]
    public void Instance_WithParameterlessConstructor_ReturnsInstance()
    {
        // Reset by assigning via DI path isn't needed — just ensure it constructs
        // (may already be set from a previous test run; that's fine)
        var instance = LeafSingleton.Instance;
        Assert.NotNull(instance);
    }

    [Fact]
    public void Instance_WithNoParameterlessConstructor_ThrowsInvalidOperationException()
    {
        // SingletonContainer.ServiceProvider is null in unit tests
        // so OnInit() reflection fallback is invoked, which should throw
        Assert.Throws<InvalidOperationException>(() => _ = DependentSingleton.Instance);
    }
}
