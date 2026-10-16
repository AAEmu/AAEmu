using AAEmu.Game.Models.Game.Slaves;

namespace AAEmu.UnitTests.Game.Models.Game.Slaves;

public class SlaveVehicleSimRulesTests
{
    [Test]
    [Arguments(SlaveKind.Tank)]
    [Arguments(SlaveKind.Machine)]
    [Arguments(SlaveKind.SiegeWeapon)]
    public async Task LandKinds_StayClientDriven(SlaveKind kind)
    {
        await Assert.That(SlaveVehicleSimRules.IsClientDrivenLandVehicle(kind)).IsTrue();
        await Assert.That(SlaveVehicleSimRules.ShouldArmZoneSimulation(kind)).IsFalse();
        await Assert.That(SlaveVehicleSimRules.ShouldUseBoatSeamHandoff(kind)).IsFalse();
        // Retail: Create in zone + type-2 relay; never arm control=1 while driven.
        await Assert.That(SlaveVehicleSimRules.ShouldAnnounceToZone(kind)).IsTrue();
        await Assert.That(SlaveVehicleSimRules.ShouldRelayLandVehicleMoveToZone(kind)).IsTrue();
    }

    [Test]
    [Arguments(SlaveKind.Boat)]
    [Arguments(SlaveKind.Fishboat)]
    [Arguments(SlaveKind.Speedboat)]
    [Arguments(SlaveKind.MerchantShip)]
    [Arguments(SlaveKind.BigSailingShip)]
    [Arguments(SlaveKind.SmallSailingShip)]
    [Arguments(SlaveKind.Leviathan)]
    public async Task SeaHulls_ArmZoneSimulation(SlaveKind kind)
    {
        await Assert.That(SlaveVehicleSimRules.IsClientDrivenLandVehicle(kind)).IsFalse();
        await Assert.That(SlaveVehicleSimRules.ShouldArmZoneSimulation(kind)).IsTrue();
        await Assert.That(SlaveVehicleSimRules.ShouldUseBoatSeamHandoff(kind)).IsTrue();
        await Assert.That(SlaveVehicleSimRules.ShouldAnnounceToZone(kind)).IsTrue();
    }

    [Test]
    public async Task SlaveEquipment_NeverArmsOrSeamsOrAnnounces()
    {
        await Assert.That(SlaveVehicleSimRules.ShouldArmZoneSimulation(SlaveKind.SlaveEquipment)).IsFalse();
        await Assert.That(SlaveVehicleSimRules.ShouldUseBoatSeamHandoff(SlaveKind.SlaveEquipment)).IsFalse();
        await Assert.That(SlaveVehicleSimRules.ShouldAnnounceToZone(SlaveKind.SlaveEquipment)).IsFalse();
    }

    [Test]
    public async Task LandVehicleEcho_SkipsDriverKeepsObservers()
    {
        await Assert.That(SlaveVehicleSimRules.ShouldEchoLandVehicleMoveTo(100, 100)).IsFalse();
        await Assert.That(SlaveVehicleSimRules.ShouldEchoLandVehicleMoveTo(200, 100)).IsTrue();
        await Assert.That(SlaveVehicleSimRules.ShouldEchoLandVehicleMoveTo(0, 100)).IsFalse();
        await Assert.That(SlaveVehicleSimRules.ShouldEchoLandVehicleCollisionResultTo(100, 100)).IsFalse();
        await Assert.That(SlaveVehicleSimRules.ShouldEchoLandVehicleCollisionResultTo(200, 100)).IsTrue();
    }
}
