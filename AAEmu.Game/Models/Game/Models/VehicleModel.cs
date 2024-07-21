namespace AAEmu.Game.Models.Game.Models;

public class VehicleModel : Model
{
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
    public float CanFly { get; set; } // can_fly
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
