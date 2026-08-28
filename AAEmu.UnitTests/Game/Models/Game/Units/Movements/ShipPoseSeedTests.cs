using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.UnitTests.Game.Models.Game.Units.Movements;

public class ShipPoseSeedTests
{
    private static ShipMoveType Pose() => new()
    {
        Type = MoveTypeEnum.Ship,
        Time = 4321,
        X = 12995.5f,
        Y = 9984.25f,
        Z = 100.5f,
        VelX = 120,
        VelY = -40,
        VelZ = 3,
        AngVelZ = 0.25f,
        Steering = -60,
        Throttle = 100,
        Rpm = 42,
        ZoneId = 218,
    };

    [Test]
    public async Task Build_LeadsWithTheShipMoveType()
    {
        var body = ShipPoseSeed.Build(Pose());

        await Assert.That(body.Length).IsGreaterThan(1);
        await Assert.That(body[0]).IsEqualTo((byte)MoveTypeEnum.Ship);
    }

    [Test]
    public async Task Build_RoundTripsPoseAndMotion()
    {
        var body = ShipPoseSeed.Build(Pose());

        var stream = new PacketStream();
        stream.Insert(0, body);
        stream.Pos = 0;
        var typeByte = stream.ReadByte();
        var parsed = MoveType.GetType((MoveTypeEnum)typeByte);
        parsed.Read(stream);

        await Assert.That(parsed).IsTypeOf<ShipMoveType>();
        var ship = (ShipMoveType)parsed;
        await Assert.That(ship.Time).IsEqualTo(4321u);
        await Assert.That(ship.ZoneId).IsEqualTo((ushort)218);
        await Assert.That(ship.VelX).IsEqualTo((short)120);
        await Assert.That(ship.VelY).IsEqualTo((short)-40);
        await Assert.That(ship.AngVelZ).IsEqualTo(0.25f);
        await Assert.That(ship.Steering).IsEqualTo((sbyte)-60);
        await Assert.That(ship.Throttle).IsEqualTo((sbyte)100);
        await Assert.That(ship.Rpm).IsEqualTo((byte)42);
    }

    private static Slave UnderWay() 
    {
        var slave = new Slave
        {
            SpawnTime = DateTime.UtcNow.AddMinutes(-1),
            Throttle = 80,
            Steering = -20,
            SimulatedShipState = Pose()
        };
        slave.Transform.Local.SetPosition(100f, 200f, 50f, 0f, 0f, 0f);
        return slave;
    }

    [Test]
    public async Task ForSlave_CarriesMotionAcrossTheSeam()
    {
        // The receiving zone's controller pulls the hull towards the velocity on this pose for seconds
        // after the handover. Seeding rest therefore does not merely fail to help — it asks the new zone
        // to brake, against both its own thrust and the seam impulse.
        var pose = ShipPoseSeed.ForSlave(UnderWay(), carryMomentum: true);

        await Assert.That(pose.VelX).IsEqualTo((short)120);
        await Assert.That(pose.VelY).IsEqualTo((short)-40);
        await Assert.That(pose.VelZ).IsEqualTo((short)3);
        await Assert.That(pose.AngVelZ).IsEqualTo(0.25f);
    }

    [Test]
    public async Task ForSlave_CarriesMotionVerbatim_WithNoSpeedCeiling()
    {
        // A hull above its model figure is sailing, not misreporting: that figure is the thrust cut-off,
        // which wind stacks and momentum legitimately carry a rigged hull past. Scaling the vector down
        // here would ask the receiving zone to hold it slower than it arrived, which is the dip.
        var slave = UnderWay();
        slave.SimulatedShipState.VelX = short.MaxValue;
        slave.SimulatedShipState.VelY = short.MinValue;

        var pose = ShipPoseSeed.ForSlave(slave, carryMomentum: true);

        await Assert.That(pose.VelX).IsEqualTo(short.MaxValue);
        await Assert.That(pose.VelY).IsEqualTo(short.MinValue);
    }

    [Test]
    public async Task ForSlave_SeedsRestWhenMomentumCarryIsOff()
    {
        var pose = ShipPoseSeed.ForSlave(UnderWay(), carryMomentum: false);

        await Assert.That(pose.VelX).IsEqualTo((short)0);
        await Assert.That(pose.VelY).IsEqualTo((short)0);
        await Assert.That(pose.VelZ).IsEqualTo((short)0);
        await Assert.That(pose.AngVelX).IsEqualTo(0f);
        await Assert.That(pose.AngVelY).IsEqualTo(0f);
        await Assert.That(pose.AngVelZ).IsEqualTo(0f);
    }

    [Test]
    public async Task ForSlave_TakesPoseAndHelmFromTheLastSimulatorReport()
    {
        // True regardless of the momentum decision: Transform can lag a handoff behind.
        foreach (var carry in new[] { true, false })
        {
            var pose = ShipPoseSeed.ForSlave(UnderWay(), carry);

            await Assert.That(pose.Throttle).IsEqualTo((sbyte)100);
            await Assert.That(pose.Steering).IsEqualTo((sbyte)-60);
            await Assert.That(pose.Rpm).IsEqualTo((byte)42);
            await Assert.That(pose.X).IsEqualTo(12995.5f);
            await Assert.That(pose.Y).IsEqualTo(9984.25f);
            await Assert.That(pose.Stuck).IsFalse();
        }
    }

    [Test]
    public async Task ForSlave_UsesLiveHelmWhenTheLastBodyReportsIdleThrottle()
    {
        // A type-4 body can report throttle 0 for one frame at a seam while the rider still holds W.
        // Seeding that 0 puts the new simulator on its braking branch.
        var slave = UnderWay();
        slave.SimulatedShipState.Throttle = 0;
        slave.ThrottleRequest = 127;
        slave.Throttle = 0;

        var pose = ShipPoseSeed.ForSlave(slave, carryMomentum: true);

        await Assert.That(pose.Throttle).IsEqualTo((sbyte)127);
    }

    [Test]
    public async Task ForSlave_PrefersTheReportedThrottleWhenItIsNonZero()
    {
        var slave = UnderWay();
        slave.ThrottleRequest = 127;

        var pose = ShipPoseSeed.ForSlave(slave, carryMomentum: true);

        await Assert.That(pose.Throttle).IsEqualTo((sbyte)100);
    }

    [Test]
    public async Task ForSlave_WithNoSimulatorReport_StaysAtRest()
    {
        // Nothing to carry: a hull that has never been simulated has no reported motion.
        var slave = new Slave { SpawnTime = DateTime.UtcNow.AddMinutes(-1) };
        slave.Transform.Local.SetPosition(100f, 200f, 50f, 0f, 0f, 0f);

        var pose = ShipPoseSeed.ForSlave(slave, carryMomentum: true);

        await Assert.That(pose.VelX).IsEqualTo((short)0);
        await Assert.That(pose.VelY).IsEqualTo((short)0);
        await Assert.That(pose.VelZ).IsEqualTo((short)0);
    }
}
