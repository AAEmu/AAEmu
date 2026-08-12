using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// A UI shop request carries no world object, so the mapping is the only thing authorizing it. These
/// cover the refusals: anything the server was not explicitly configured with has to resolve to nothing.
/// </summary>
public class UiMerchantShopMapTests
{
    private UiMerchantShopMap _map;

    [Before(Test)]
    public void Setup()
    {
        _map = new UiMerchantShopMap();
    }

    [Test]
    public async Task GetMerchantPackId_WhenOpenTypeMapped_ReturnsThatPack()
    {
        await Assert.That(_map.TryAdd(2, 192)).IsTrue();

        await Assert.That(_map.GetMerchantPackId(2)).IsEqualTo(192u);
    }

    [Test]
    public async Task GetMerchantPackId_WhenOpenTypeNotMapped_ReturnsZero()
    {
        _map.TryAdd(2, 192);

        await Assert.That(_map.GetMerchantPackId(3)).IsEqualTo(0u);
    }

    [Test]
    public async Task GetMerchantPackId_WhenNothingConfigured_RejectsEveryOpenType()
    {
        for (var openType = 0; openType <= byte.MaxValue; openType++)
        {
            await Assert.That(_map.GetMerchantPackId((byte)openType)).IsEqualTo(0u);
        }
    }

    [Test]
    public async Task GetMerchantPackId_ForOpenTypeZero_ReturnsZero()
    {
        // 0 is a world shop; it must never reach a pack through this path.
        await Assert.That(_map.TryAdd(0, 192)).IsFalse();

        await Assert.That(_map.GetMerchantPackId(0)).IsEqualTo(0u);
        await Assert.That(_map.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryAdd_WithPackIdZero_IsRejected()
    {
        await Assert.That(_map.TryAdd(2, 0)).IsFalse();

        await Assert.That(_map.GetMerchantPackId(2)).IsEqualTo(0u);
        await Assert.That(_map.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryAdd_DoesNotMakeNeighbouringOpenTypesReachable()
    {
        _map.TryAdd(4, 393);

        await Assert.That(_map.GetMerchantPackId(3)).IsEqualTo(0u);
        await Assert.That(_map.GetMerchantPackId(5)).IsEqualTo(0u);
        await Assert.That(_map.GetMerchantPackId(byte.MaxValue)).IsEqualTo(0u);
        await Assert.That(_map.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Clear_MakesPreviouslyMappedOpenTypesUnreachable()
    {
        _map.TryAdd(2, 192);
        _map.TryAdd(4, 393);

        _map.Clear();

        await Assert.That(_map.Count).IsEqualTo(0);
        await Assert.That(_map.GetMerchantPackId(2)).IsEqualTo(0u);
        await Assert.That(_map.GetMerchantPackId(4)).IsEqualTo(0u);
    }
}
