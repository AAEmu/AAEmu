using System.Collections.Generic;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Transfers
{
    public class VehicleModels
    {
        public uint Id { get; set; }
        public uint Normal { get; set; }
        public uint Damaged50 { get; set; }
        public uint Dying { get; set; }
        public uint Dead { get; set; }
        public string Wheel { get; set; }
        public float TurretPitchAngleMax { get; set; }
        public float LinInertia { get; set; }
        public float LinDeaccelInertia { get; set; }
        public float RotInertia { get; set; }
        public float RotDeaccelInertia { get; set; }
        public float Velocity { get; set; }
        public float AngVel { get; set; }
        public bool CanFly { get; set; }
        public uint DriverWalk { get; set; }
        public string Wheel2 { get; set; }
        public float TurretYawAngvel { get; set; }
        public float TurretPitchAngvel { get; set; }
        public uint Damaged25 { get; set; }
        public uint Damaged75 { get; set; }
        public float FloatingHeight { get; set; }
        public float FloatingWaveHeight { get; set; }
        public float FloatingWavePeriodRatio { get; set; }
        public bool AutoLevel { get; set; }
        public float TrailAlignRatio { get; set; }
        public uint SoundId { get; set; }
        public float SuspStroke { get; set; }
        public float MaxClimbAng { get; set; }
        public uint SuspAxle { get; set; }
        public float TurretYawAngleMax { get; set; }
        public uint InstalledTurret { get; set; }
        public bool UseProxyCollision { get; set; }
        public bool UseCenterSpindle { get; set; }
        public float TurretPitchAngleMin { get; set; }
        public float TurretYawAngleMin { get; set; }
        public bool UseWheeledVehicleSimulation { get; set; }
        public float WheeledVehicleMass { get; set; }
        public float WheeledVehiclePower { get; set; }
        public float WheeledVehicleBrakeTorque { get; set; }
        public uint WheeledVehicleMaxGear { get; set; }
        public float WheeledVehicleGearSpeedRatioReverse { get; set; }
        public float WheeledVehicleGearSpeedRatio1 { get; set; }
        public float WheeledVehicleGearSpeedRatio2 { get; set; }
        public float WheeledVehicleGearSpeedRatio3 { get; set; }
        public float WheeledVehicleSuspStroke { get; set; }
        public float WheeledVehicleSuspDamping { get; set; }
        public uint WheeledVehicleDrive { get; set; }
        public float WheeledVehicleFrontOptimalSa { get; set; }
        public float WheeledVehicleRearOptimalSa { get; set; }
        public float WheeledVehicleBallastMass { get; set; }
        public float WheeledVehicleBallastPosY { get; set; }

        public VehicleModels()
        {
        }

    }
}
