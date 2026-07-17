namespace AAEmu.Game.Models.Game.Models;

public class VehicleModel : Model
{
    public bool AutoLevel { get; set; }
    public int WheeledVehicleSuspRearAxisType { get; set; }
    public int WheeledVehicleSuspFrontAxisType { get; set; }
    public float WheeledVehicleSuspDamping { get; set; }
    public int WheeledVehicleSteer { get; set; }
    public float WheeledVehicleSpeedLimit { get; set; }
    public float WheeledVehicleFrictionRatio { get; set; }
    public bool WheeledVehicleDespawnOnSpeedOver { get; set; }
    public float WheeledVehicleBallastPosZ { get; set; }
    public float WheeledVehicleBallastPosY { get; set; }
    public float WheeledVehicleBallastMass { get; set; }
    public float WheeledVehicleBallastBoxZ { get; set; }
    public float WheeledVehicleBallastBoxY { get; set; }
    public float WheeledVehicleBallastBoxX { get; set; }
    public string Wheel2 { get; set; }
    public string Wheel { get; set; }
    public bool UseWheeledVehicleSimulation { get; set; }
    public bool UseProxyCollision { get; set; }
    public bool UseCenterSpindle { get; set; }
    public float TurretYawAngvel { get; set; }
    public float TurretYawAngleMin { get; set; }
    public float TurretYawAngleMax { get; set; }
    public float TurretPitchAngvel { get; set; }
    public float TurretPitchAngleMin { get; set; }
    public float TurretPitchAngleMax { get; set; }
    public float TrailAlignRatio { get; set; }
    public float SuspStroke { get; set; }
    public bool SuspAxle { get; set; }
    public int SoundId { get; set; }
    public string Normal { get; set; }
    public float MaxClimbAng { get; set; }
    public bool InstalledTurret { get; set; }
    public float FloatingWavePeriodRatio { get; set; }
    public float FloatingWaveHeight { get; set; }
    public float FloatingHeight { get; set; }
    public string Dying { get; set; }
    public bool DriverWalk { get; set; }
    public string Dead { get; set; }
    public string Damaged75 { get; set; }
    public string Damaged50 { get; set; }
    public string Damaged25 { get; set; }
    public float CollisionBoxScaleZ { get; set; }
    public float CollisionBoxScaleY { get; set; }
    public float CollisionBoxScaleX { get; set; }
    public float CollisionBoxOffsetZ { get; set; }
    public float CollisionBoxOffsetY { get; set; }
    public float CollisionBoxOffsetX { get; set; }
    public int CharAnimSteerForwardId { get; set; }
    public int CharAnimSteerBackwardId { get; set; }
    /*
     *id
       normal
       damaged50
       dying
       dead
       wheel
       turret_pitch_angle_max
       lin_inertia
       lin_deaccel_inertia
       rot_inertia
       rot_deaccel_inertia
       velocity
       angVel
       can_fly
       driver_walk
       wheel2
       turret_yaw_angvel
       turret_pitch_angvel
       damaged25
       damaged75
       floating_height
       floating_wave_height
       floating_wave_period_ratio
       auto_level
       trail_align_ratio
       sound_id
       susp_stroke
       max_climb_ang
       susp_axle
       turret_yaw_angle_max
       installed_turret
       use_proxy_collision
       use_center_spindle
       turret_pitch_angle_min
       turret_yaw_angle_min
       use_wheeled_vehicle_simulation
       wheeled_vehicle_mass
       wheeled_vehicle_power
       wheeled_vehicle_brake_torque
       wheeled_vehicle_max_gear
       wheeled_vehicle_gear_speed_ratio_reverse
       wheeled_vehicle_gear_speed_ratio_1
       wheeled_vehicle_gear_speed_ratio_2
       wheeled_vehicle_gear_speed_ratio_3
       wheeled_vehicle_susp_stroke
       wheeled_vehicle_susp_damping
       wheeled_vehicle_drive
       wheeled_vehicle_front_optimal_sa
       wheeled_vehicle_rear_optimal_sa
       wheeled_vehicle_ballast_mass
       wheeled_vehicle_ballast_pos_y
       
     */
    public float LinInertia { get; set; } // lin_inertia
    public float LinDeaccelInertia { get; set; } // lin_deaccel_inertia
    public float RotInertia { get; set; } // rot_inertia
    public float RotDeaccelInertia { get; set; } // rot_deaccel_inertia
    public float Velocity { get; set; } // velocity
    public float AngVel { get; set; } // angVel
    public bool CanFly { get; set; } // can_fly
    public float WheeledVehicleMass { get; set; } // wheeled_vehicle_mass
    public float WheeledVehiclePower { get; set; } // wheeled_vehicle_power
    public float WheeledVehicleBrakeTorque { get; set; } // wheeled_vehicle_brake_torque
    public uint WheeledVehicleMaxGear { get; set; } // wheeled_vehicle_max_gear
    public float WheeledVehicleGearSpeedRatioReverse { get; set; } // wheeled_vehicle_gear_speed_ratio_reverse
    public float WheeledVehicleGearSpeedRatio1 { get; set; } // wheeled_vehicle_gear_speed_ratio_1
    public float WheeledVehicleGearSpeedRatio2 { get; set; } // wheeled_vehicle_gear_speed_ratio_2
    public float WheeledVehicleGearSpeedRatio3 { get; set; } // wheeled_vehicle_gear_speed_ratio_3
    public float WheeledVehicleSuspStroke { get; set; } // wheeled_vehicle_susp_stroke
    public float WheeledVehicleDrive { get; set; } // wheeled_vehicle_drive
    public float WheeledVehicleFrontOptimalSa { get; set; } // wheeled_vehicle_front_optimal_sa
    public float WheeledVehicleRearOptimalSa { get; set; } // wheeled_vehicle_rear_optimal_sa
}
