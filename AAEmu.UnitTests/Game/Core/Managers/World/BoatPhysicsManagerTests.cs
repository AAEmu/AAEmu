using System;
using System.Numerics;
using System.Threading;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Forces;
using AAEmu.Game.Utils;

using Jitter.Collision;
using Jitter.Collision.Shapes;
using Jitter.Dynamics;
using Jitter.LinearMath;

using Moq;

using Xunit;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World
{
    public class BoatPhysicsManagerTests
    {
        //private readonly Mock<WorldManager> _mockWorldManager;
        private readonly Mock<InstanceWorld> _mockWorld;
        //private readonly Mock<SlaveManager> _mockSlaveManager;
        private readonly Mock<Slave> _mockSlave;
        private readonly Mock<RigidBody> _mockRigidBody;
        //private readonly Mock<ModelManager> _mockModelManager;
        private readonly BoatPhysicsManager _boatPhysicsManager;

        public BoatPhysicsManagerTests()
        {
            //_mockWorldManager = new Mock<WorldManager>();
            _mockWorld = new Mock<InstanceWorld>();
            //_mockSlaveManager = new Mock<SlaveManager>();
            _mockSlave = new Mock<Slave>();
            var mockShipModel = new Mock<ShipModel>();
            _mockRigidBody = new Mock<RigidBody>(new BoxShape(1, 1, 1));

            // Configure ModelManager to return _mockShipModel.Object for GetShipModel
            //_mockModelManager = new Mock<ModelManager>();
            //_mockModelManager.Setup(mm => mm.GetShipModel(It.IsAny<uint>())).Returns(mockShipModel.Object);

            _boatPhysicsManager = new BoatPhysicsManager()
            {
                _thread = null,
                _collisionSystem = null,
                _physWorld = null,
                _buoyancy = null,
                ThreadRunning = false,
                SimulationWorld = _mockWorld.Object
            };
        }

        [Fact]
        public void Initialize_Should_Initialize_Physics_World()
        {
            // Arrange
            _mockWorld.Setup(w => w.Name).Returns("main_world");
            _mockWorld.Setup(w => w.HeightMaps).Returns(new ushort[1, 1]);
            _mockWorld.Setup(w => w.HeightMaxCoefficient).Returns(1.0f);

            // Act
            _boatPhysicsManager.Initialize();

            // Assert
            Assert.NotNull(_boatPhysicsManager._collisionSystem);
            Assert.NotNull(_boatPhysicsManager._physWorld);
            Assert.NotNull(_boatPhysicsManager._buoyancy);
            //_mockWorld.Verify(w => w.HeightMaps, Times.Once);
        }

        [Fact]
        public void StartPhysics_WhenCalled_StartsPhysicsThread()
        {
            // Arrange
            _mockWorld.Object.Name = "main_world";
            _boatPhysicsManager.SimulationWorld = _mockWorld.Object;

            // Act
            _boatPhysicsManager.StartPhysics();

            // Assert
            Assert.True(_boatPhysicsManager.ThreadRunning);

            // Verify that the thread is started
            Assert.NotNull(_boatPhysicsManager._thread);

            Assert.NotEqual("Physics-main_world", _boatPhysicsManager._thread.Name);
            Assert.Equal("Physics-???", _boatPhysicsManager._thread.Name);

            _boatPhysicsManager.Stop();
        }

        [Fact]
        public void RemoveShip_WhenCalled_RemovesRigidBodyFromPhysicsWorld()
        {
            // Arrange
            _boatPhysicsManager._physWorld = new Jitter.World(new CollisionSystemSAP());
            _boatPhysicsManager._buoyancy = new Buoyancy(_boatPhysicsManager._physWorld);
            _boatPhysicsManager.SimulationWorld = _mockWorld.Object;

            _mockSlave.Setup(s => s.RigidBody).Returns(_mockRigidBody.Object);

            // Use reflection to set property values
            //_mockRigidBody.Setup(rb => rb.IsActive).Returns(true);
            var isActiveProperty = typeof(RigidBody).GetProperty("IsActive");
            isActiveProperty?.SetValue(_mockRigidBody.Object, true);

            // Add the rigid body to the physics world
            _boatPhysicsManager._physWorld.AddBody(_mockRigidBody.Object);

            // Act
            _boatPhysicsManager.RemoveShip(_mockSlave.Object);

            // Assert
            Assert.False(_mockRigidBody.Object.IsActive);
            Assert.DoesNotContain(_mockRigidBody.Object, _boatPhysicsManager._physWorld.RigidBodies);
        }

        [Fact]
        public void GetRollAngle_WhenCalled_ReturnsRollAngle()
        {
            // Arrange
            var orientation = JMatrix.CreateRotationY(45f.DegToRad()); // 45 градусов
            var rollAngle = Math.Round(BoatPhysicsManager.GetRollAngle(orientation).RadToDeg());

            // Assert
            Assert.Equal(45f, rollAngle);
        }

        [Fact]
        public void Stop_WhenCalled_StopsPhysicsThread()
        {
            // Arrange
            _boatPhysicsManager.ThreadRunning = true;
            _boatPhysicsManager._thread = new Thread(() => { });

            // Act
            _boatPhysicsManager.Stop();

            // Assert
            Assert.False(_boatPhysicsManager.ThreadRunning);
        }

        [Fact]
        public void TestCustomWater()
        {
            // Проверяем, что метод CustomWater корректно определяет водную область
            _boatPhysicsManager.SimulationWorld = _mockWorld.Object;
            _mockWorld.Setup(w => w.IsWater(It.IsAny<Vector3>())).Returns(true);

            var area = new JVector(0, 0, 0);
            var isWater = _boatPhysicsManager.CustomWater(ref area);

            Assert.True(isWater);
        }

        [Fact]
        public void TestGetRollAngle()
        {
            // Проверяем вычисление угла крена из ориентации
            var orientation = JMatrix.Identity;
            var rollAngle = BoatPhysicsManager.GetRollAngle(orientation);

            Assert.Equal(0f, rollAngle);
        }

        [Fact]
        public void TestGetYawPitchRollFromJMatrix()
        {
            // Проверяем извлечение углов поворота из матрицы
            var mat = JMatrix.Identity;
            var (yaw, pitch, roll) = BoatPhysicsManager.GetYawPitchRollFromJMatrix(mat);

            Assert.Equal(0f, yaw);
            Assert.Equal(0f, pitch);
            Assert.Equal(0f, roll);
        }

        [Fact]
        public void TestJMatrixToQuaternion()
        {
            // Проверяем преобразование матрицы в кватернион
            var matrix = JMatrix.Identity;
            var quaternion = BoatPhysicsManager.JMatrixToQuaternion(matrix);

            Assert.Equal(0f, quaternion.X);
            Assert.Equal(0f, quaternion.Y);
            Assert.Equal(0f, quaternion.Z);
            Assert.Equal(1f, quaternion.W);
        }

        //[Fact]
        //public void AddShip_WhenCalled_AddsRigidBodyToPhysicsWorld()
        //{
        //    // Arrange
        //    _boatPhysicsManager._physWorld = new Jitter.World(new CollisionSystemSAP());
        //    _boatPhysicsManager._buoyancy = new Buoyancy(_boatPhysicsManager._physWorld);
        //    _boatPhysicsManager.SimulationWorld = _mockWorld.Object;

        //    _mockSlave.Setup(s => s.ModelId).Returns(1);
        //    _mockModelManager.Setup(mm => mm.GetShipModel(1)).Returns(_mockShipModel.Object);
        //    _mockShipModel.Setup(sm => sm.MassBoxSizeX).Returns(1f);
        //    _mockShipModel.Setup(sm => sm.MassBoxSizeZ).Returns(1f);
        //    _mockShipModel.Setup(sm => sm.MassBoxSizeY).Returns(1f);
        //    _mockShipModel.Setup(sm => sm.Mass).Returns(1f);
        //    //ModelManager.Instance = _mockModelManager.Object;

        //    // Act
        //    _boatPhysicsManager.AddShip(_mockSlave.Object);

        //    // Assert
        //    Assert.NotNull(_mockSlave.Object.RigidBody);
        //    Assert.Contains(_mockSlave.Object.RigidBody, _boatPhysicsManager._physWorld.RigidBodies);
        //}

        //[Fact]
        //public void AddShip_Should_Add_Ship_To_Physics_World()
        //{
        //    // Arrange
        //    var mockShipModel = new Mock<ShipModel>();
        //    mockShipModel.Setup(m => m.Mass).Returns(100f);
        //    mockShipModel.Setup(m => m.MassBoxSizeX).Returns(1f);
        //    mockShipModel.Setup(m => m.MassBoxSizeY).Returns(1f);
        //    mockShipModel.Setup(m => m.MassBoxSizeZ).Returns(1f);

        //    // Создаем реальный объект Transform
        //    var transform = new Transform();
        //    transform.World = new PositionAndRotation();
        //    transform.World.Position = new Vector3(0, 0, 0);

        //    var mockSlave = new Mock<Slave>();
        //    mockSlave.Setup(s => s.ModelId).Returns(1);
        //    mockSlave.Setup(s => s.Transform).Returns(transform);

        //    // Act
        //    _boatPhysicsManager.AddShip(mockSlave.Object);

        //    // Assert
        //    mockSlave.VerifySet(s => s.RigidBody = It.IsAny<RigidBody>(), Times.Once);
        //    Assert.NotNull(mockSlave.Object.RigidBody);
        //}

        //[Fact]
        //public void StartPhysicsWhenCalledStartsPhysicsThread()
        //{
        //    // Arrange
        //    var mockThread = new Mock<Thread>();
        //    _boatPhysicsManager._thread = mockThread.Object;

        //    // Act
        //    _boatPhysicsManager.StartPhysics();

        //    // Assert
        //    mockThread.Verify(t => t.Start(), Times.Once());
        //    Assert.True(_boatPhysicsManager.ThreadRunning);
        //}

        //[Fact]
        //public void BoatPhysicsTick_WhenOnWaterAppliesBuoyancyAndDrag()
        //{
        //    // Arrange
        //    var mockModelManager = new Mock<IModelManager>();
        //    mockModelManager.Setup(mm => mm.GetShipModel(It.IsAny<uint>())).Returns(_mockShipModel.Object);

        //    var _boatPhysicsManager = new BoatPhysicsManager(mockModelManager.Object);
        //    _boatPhysicsManager._physWorld = new Jitter.World(new CollisionSystemSAP());
        //    _boatPhysicsManager._buoyancy = new Buoyancy(_boatPhysicsManager._physWorld);
        //    _boatPhysicsManager.SimulationWorld = _mockWorld.Object;

        //    // Set RigidBody properties using reflection
        //    //_mockSlave.Setup(s => s.RigidBody).Returns(_mockRigidBody.Object);
        //    var rigidBodyProperty = typeof(Slave).GetProperty("RigidBody");
        //    rigidBodyProperty?.SetValue(_mockSlave.Object, _mockRigidBody.Object);
        //    //_mockRigidBody.Setup(rb => rb.Position).Returns(new JVector(0, 90, 0));
        //    var positionProperty = typeof(RigidBody).GetProperty("Position");
        //    positionProperty?.SetValue(_mockRigidBody.Object, new JVector(0, 90, 0)); // Position.Y < _waterLevel to be on water
        //    //_mockRigidBody.Setup(rb => rb.LinearVelocity).Returns(new JVector(1, 0, 1));
        //    var linearVelocityProperty = typeof(RigidBody).GetProperty("LinearVelocity");
        //    linearVelocityProperty?.SetValue(_mockRigidBody.Object, new JVector(1, 0, 1));

        //    // Set ShipModel properties using reflection
        //    //_mockShipModel.Setup(sm => sm.Mass).Returns(4000f);
        //    var massProperty = typeof(ShipModel).GetProperty("Mass");
        //    massProperty?.SetValue(_mockShipModel.Object, 4000f);

        //    //_mockShipModel.Setup(sm => sm.WaterDensity).Returns(1f);
        //    var waterDensityProperty = typeof(ShipModel).GetProperty("WaterDensity");
        //    waterDensityProperty?.SetValue(_mockShipModel.Object, 1f);
        //    //_mockShipModel.Setup(sm => sm.WaterResistance).Returns(0.1f);
        //    var waterResistanceProperty = typeof(ShipModel).GetProperty("WaterResistance");
        //    waterResistanceProperty?.SetValue(_mockShipModel.Object, 0.1f);

        //    // Set slave.Template and its ModelId
        //    var templateProperty = typeof(Slave).GetProperty("Template");
        //    var mockTemplate = new Mock<SlaveTemplate>().Object;
        //    templateProperty?.SetValue(_mockSlave.Object, mockTemplate);

        //    var templateModelIdProperty = typeof(SlaveTemplate).GetProperty("ModelId");
        //    templateModelIdProperty?.SetValue(mockTemplate, 1u);

        //    // Set slave.Transform
        //    var transformProperty = typeof(Slave).GetProperty("Transform");
        //    transformProperty?.SetValue(_mockSlave.Object, new Mock<Transform>().Object);

        //    // Set slave.AttachedCharacters
        //    var attachedCharactersProperty = typeof(Slave).GetProperty("AttachedCharacters");
        //    attachedCharactersProperty?.SetValue(_mockSlave.Object, new Dictionary<AttachPointKind, Character>());

        //    // Set other necessary properties on slave
        //    var moveSpeedMulProperty = typeof(Slave).GetProperty("MoveSpeedMul");
        //    moveSpeedMulProperty?.SetValue(_mockSlave.Object, 1f);

        //    var turnSpeedProperty = typeof(Slave).GetProperty("TurnSpeed");
        //    turnSpeedProperty?.SetValue(_mockSlave.Object, 10f);

        //    var throttleProperty = typeof(Slave).GetProperty("Throttle");
        //    throttleProperty?.SetValue(_mockSlave.Object, 0);

        //    var steeringProperty = typeof(Slave).GetProperty("Steering");
        //    steeringProperty?.SetValue(_mockSlave.Object, 0);

        //    var speedProperty = typeof(Slave).GetProperty("Speed");
        //    speedProperty?.SetValue(_mockSlave.Object, 0f);

        //    var rotSpeedProperty = typeof(Slave).GetProperty("RotSpeed");
        //    rotSpeedProperty?.SetValue(_mockSlave.Object, 0f);

        //    // Act
        //    _boatPhysicsManager.BoatPhysicsTick(_mockSlave.Object, _mockRigidBody.Object);

        //    // Assert
        //    // Verify that buoyancy and drag forces are added
        //    _mockRigidBody.Verify(rb => rb.AddForce(It.IsAny<JVector>()), Times.Exactly(2));
        //}
    }
}
