using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class VehicleModel
{
    public long? Id { get; set; }

    public string Normal { get; set; }

    public string Damaged50 { get; set; }

    public string Dying { get; set; }

    public string Dead { get; set; }

    public string Wheel { get; set; }

    public double? TurretPitchAngleMax { get; set; }

    public double? LinInertia { get; set; }

    public double? LinDeaccelInertia { get; set; }

    public double? RotInertia { get; set; }

    public double? RotDeaccelInertia { get; set; }

    public double? Velocity { get; set; }

    public double? AngVel { get; set; }

    public byte[] CanFly { get; set; }

    public byte[] DriverWalk { get; set; }

    public string Wheel2 { get; set; }

    public double? TurretYawAngvel { get; set; }

    public double? TurretPitchAngvel { get; set; }

    public string Damaged25 { get; set; }

    public string Damaged75 { get; set; }

    public double? FloatingHeight { get; set; }

    public double? FloatingWaveHeight { get; set; }

    public double? FloatingWavePeriodRatio { get; set; }

    public byte[] AutoLevel { get; set; }

    public double? TrailAlignRatio { get; set; }

    public long? SoundId { get; set; }

    public double? SuspStroke { get; set; }

    public double? MaxClimbAng { get; set; }

    public byte[] SuspAxle { get; set; }

    public double? TurretYawAngleMax { get; set; }

    public byte[] InstalledTurret { get; set; }

    public byte[] UseProxyCollision { get; set; }

    public byte[] UseCenterSpindle { get; set; }

    public double? TurretPitchAngleMin { get; set; }

    public double? TurretYawAngleMin { get; set; }

    public byte[] UseWheeledVehicleSimulation { get; set; }

    public double? WheeledVehicleMass { get; set; }

    public double? WheeledVehiclePower { get; set; }

    public double? WheeledVehicleBrakeTorque { get; set; }

    public long? WheeledVehicleMaxGear { get; set; }

    public double? WheeledVehicleGearSpeedRatioReverse { get; set; }

    public double? WheeledVehicleGearSpeedRatio1 { get; set; }

    public double? WheeledVehicleGearSpeedRatio2 { get; set; }

    public double? WheeledVehicleGearSpeedRatio3 { get; set; }

    public double? WheeledVehicleSuspStroke { get; set; }

    public double? WheeledVehicleSuspDamping { get; set; }

    public long? WheeledVehicleDrive { get; set; }

    public double? WheeledVehicleFrontOptimalSa { get; set; }

    public double? WheeledVehicleRearOptimalSa { get; set; }

    public double? WheeledVehicleBallastMass { get; set; }

    public double? WheeledVehicleBallastPosY { get; set; }
}
