//! Unit movement payloads (port of `AAEmu.Game.Models.Game.Units.Movements`).
//!
//! In C# `MoveType` is an abstract class whose subclasses all inherit the same bag of base
//! properties (`X`/`Y`/`Z`, `VelX..`, `RotationX..`, `Rot`, `Velocity`, `WorldPos`), and
//! `VehicleMoveType`/`ShipMoveType` shadow `RotationX/Y/Z` with `new short` properties. Here the
//! common header (`Time`, `Flags`, `ScType`, `Phase`) lives on [`MoveType`] and each subclass is a
//! [`MoveKind`] variant carrying exactly the fields it reads and writes. Accessors such as
//! [`MoveType::position`] and [`MoveType::velocity`] report what the C# base-class properties
//! would hold after `Read`.
//!
//! Also contains the bodies of `SCOneUnitMovementPacket` / `SCUnitMovementsPacket` and the
//! `MathUtil` direction helpers used by `CSMoveUnitPacket`.

use aaemu_commons::{Marshal, MarshalError, PacketStream};

type Result<T> = std::result::Result<T, MarshalError>;

// ---- enums / flags ------------------------------------------------------------------------------

/// C# `MoveTypeEnum`. The wire value is a raw byte; see [`MoveType::type_raw`] for values outside
/// this enum.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
#[repr(u8)]
pub enum MoveTypeEnum {
    Default = 0,
    Unit = 1,
    Vehicle = 2,
    Ship = 3,
    ShipRequest = 4,
    Transfer = 5,
}

impl MoveTypeEnum {
    /// Maps a raw type byte to the enum, `None` for values C# has no name for.
    pub fn from_u8(v: u8) -> Option<Self> {
        Some(match v {
            0 => Self::Default,
            1 => Self::Unit,
            2 => Self::Vehicle,
            3 => Self::Ship,
            4 => Self::ShipRequest,
            5 => Self::Transfer,
            _ => return None,
        })
    }
}

/// C# `MoveTypeFlags` (`[Flags] enum : byte`). Kept as a newtype so any byte round-trips.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub struct MoveTypeFlags(pub u8);

impl MoveTypeFlags {
    pub const NONE: Self = Self(0x00);
    pub const FLAG1: Self = Self(0x01);
    pub const MOVING: Self = Self(0x02);
    /// Might be more accurately named "braking".
    pub const STOPPING: Self = Self(0x04);
    /// Set when fighting.
    pub const IN_COMBAT: Self = Self(0x08);
    pub const HAS_SC_TYPE_AND_PHASE: Self = Self(0x10);
    pub const FLAG6: Self = Self(0x20);
    pub const STANDING_ON_OBJECT: Self = Self(0x40);
    pub const FLAG8: Self = Self(0x80);
    /// `Moving | Stopping`.
    pub const JUMPING: Self = Self(0x06);

    /// C# `Enum.HasFlag`: all bits of `other` are set (so `contains(NONE)` is `true`).
    pub fn contains(self, other: Self) -> bool {
        self.0 & other.0 == other.0
    }
}

impl std::ops::BitOr for MoveTypeFlags {
    type Output = Self;
    fn bitor(self, rhs: Self) -> Self {
        Self(self.0 | rhs.0)
    }
}

/// C# `MoveTypeActorFlags` (`[Flags] enum : byte`), the `UnitMoveType.ActorFlags` byte.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub struct MoveTypeActorFlags(pub u8);

impl MoveTypeActorFlags {
    pub const NONE: Self = Self(0x00);
    pub const FLAG1: Self = Self(0x01);
    pub const FLAG2: Self = Self(0x02);
    pub const STANDING_ON_SOLID: Self = Self(0x04);
    pub const FLAG4: Self = Self(0x08);
    pub const JUMPING: Self = Self(0x10);
    /// Same as `MoveTypeFlags::StandingOnObject` (0x40). On the wire: gc block present.
    pub const STANDING_ON_OBJECT: Self = Self(0x20);
    /// "Sticking" to another object without having it as parent. On the wire: climb data present.
    pub const HANGING_FROM_OBJECT: Self = Self(0x40);
    /// On the wire: fall velocity present.
    pub const FLAG8: Self = Self(0x80);

    pub fn contains(self, other: Self) -> bool {
        self.0 & other.0 == other.0
    }
}

/// C# `GameStanceType : byte` (packet values; the DB uses these +1). Newtype so unknown values
/// round-trip like the C# enum cast does.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub struct GameStanceType(pub u8);

impl GameStanceType {
    pub const COMBAT: Self = Self(0);
    pub const RELAXED: Self = Self(1);
    pub const SWIM: Self = Self(2);
    pub const CO_SWIM: Self = Self(3);
    /// Also called "combat" in the DB.
    pub const COMBAT2: Self = Self(4);
    pub const STEALTH: Self = Self(5);
    pub const CROUCH: Self = Self(6);
    pub const PRONE: Self = Self(7);
    pub const FLY: Self = Self(8);
}

/// C# `MoveTypeAlertness : byte`. Newtype so unknown values round-trip.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub struct MoveTypeAlertness(pub u8);

impl MoveTypeAlertness {
    pub const IDLE: Self = Self(0);
    pub const ALERT: Self = Self(1);
    pub const COMBAT: Self = Self(2);
}

/// C# `WorldPos`: x/y as 64-bit fixed point (`Helpers.ConvertLongX/Y`), z as float.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct WorldPos {
    pub x: i64,
    pub y: i64,
    pub z: f32,
}

impl WorldPos {
    /// `new WorldPos(Helpers.ConvertLongX(x), Helpers.ConvertLongY(y), z)`.
    pub fn from_xyz(x: f32, y: f32, z: f32) -> Self {
        Self {
            x: convert_long(x),
            y: convert_long(y),
            z,
        }
    }
}

/// `Helpers.ConvertLongX(float)` / `ConvertLongY(float)`: `(long)(v * 4096) << 32`.
fn convert_long(v: f32) -> i64 {
    // C# (net10) float->long conversion saturates, as does Rust `as`.
    ((v * 4096.0) as i64).wrapping_shl(32)
}

// ---- move type ----------------------------------------------------------------------------------

/// C# `MoveType` (abstract base): the common header plus the concrete subclass in [`MoveKind`].
#[derive(Debug, Clone, PartialEq)]
pub struct MoveType {
    /// `MoveType.Type` as the raw wire byte. `MoveType.GetType` keeps the raw value even when it
    /// picks `DefaultMoveType` for an unknown type.
    pub type_raw: u8,
    /// `MoveType.Time`.
    pub time: u32,
    /// `MoveType.Flags`.
    pub flags: MoveTypeFlags,
    /// `MoveType.ScType`, only on the wire when `flags` has `HAS_SC_TYPE_AND_PHASE`.
    pub sc_type: u32,
    /// `MoveType.Phase`, only on the wire when `flags` has `HAS_SC_TYPE_AND_PHASE`.
    pub phase: u8,
    pub kind: MoveKind,
}

/// The concrete `MoveType` subclass.
#[derive(Debug, Clone, PartialEq)]
pub enum MoveKind {
    Default(DefaultMove),
    Unit(UnitMove),
    Vehicle(VehicleMove),
    Ship(ShipMove),
    ShipRequest(ShipRequestMove),
    Transfer(TransferMove),
}

impl MoveKind {
    /// C# `MoveType.GetType`: a fresh (default-valued) subclass for `type_byte`; any value that is
    /// not 1..=5 yields `DefaultMoveType`.
    pub fn for_type(type_byte: u8) -> Self {
        match MoveTypeEnum::from_u8(type_byte) {
            Some(MoveTypeEnum::Unit) => Self::Unit(UnitMove::default()),
            Some(MoveTypeEnum::Vehicle) => Self::Vehicle(VehicleMove::default()),
            Some(MoveTypeEnum::Ship) => Self::Ship(ShipMove::default()),
            Some(MoveTypeEnum::ShipRequest) => Self::ShipRequest(ShipRequestMove::default()),
            Some(MoveTypeEnum::Transfer) => Self::Transfer(TransferMove::default()),
            Some(MoveTypeEnum::Default) | None => Self::Default(DefaultMove::default()),
        }
    }

    /// The `MoveTypeEnum` this subclass is created for by `GetType`.
    pub fn natural_type(&self) -> MoveTypeEnum {
        match self {
            Self::Default(_) => MoveTypeEnum::Default,
            Self::Unit(_) => MoveTypeEnum::Unit,
            Self::Vehicle(_) => MoveTypeEnum::Vehicle,
            Self::Ship(_) => MoveTypeEnum::Ship,
            Self::ShipRequest(_) => MoveTypeEnum::ShipRequest,
            Self::Transfer(_) => MoveTypeEnum::Transfer,
        }
    }

    fn read_body(&mut self, s: &mut PacketStream) -> Result<()> {
        match self {
            Self::Default(m) => m.read(s),
            Self::Unit(m) => m.read(s),
            Self::Vehicle(m) => m.read(s),
            Self::Ship(m) => m.read(s),
            Self::ShipRequest(m) => m.read(s),
            Self::Transfer(m) => m.read(s),
        }
    }

    fn write_body(&self, s: &mut PacketStream) {
        match self {
            Self::Default(m) => m.write(s),
            Self::Unit(m) => m.write(s),
            Self::Vehicle(m) => m.write(s),
            Self::Ship(m) => m.write(s),
            Self::ShipRequest(m) => m.write(s),
            Self::Transfer(m) => m.write(s),
        }
    }
}

impl MoveType {
    /// A movement with zeroed header whose `type_raw` is the variant's natural type.
    pub fn new(kind: MoveKind) -> Self {
        Self {
            type_raw: kind.natural_type() as u8,
            time: 0,
            flags: MoveTypeFlags::NONE,
            sc_type: 0,
            phase: 0,
            kind,
        }
    }

    /// `MoveType.GetType((MoveTypeEnum)typeByte)` followed by `Read(stream)`.
    pub fn read(type_byte: u8, stream: &mut PacketStream) -> Result<Self> {
        let mut kind = MoveKind::for_type(type_byte);
        // MoveType.Read
        let time = stream.read_u32()?;
        let flags = MoveTypeFlags(stream.read_u8()?);
        let (mut sc_type, mut phase) = (0, 0);
        if flags.contains(MoveTypeFlags::HAS_SC_TYPE_AND_PHASE) {
            sc_type = stream.read_u32()?;
            phase = stream.read_u8()?;
        }
        kind.read_body(stream)?;
        Ok(Self {
            type_raw: type_byte,
            time,
            flags,
            sc_type,
            phase,
            kind,
        })
    }

    /// C# `MoveType.Write` (and the subclass override). Does NOT write the type byte.
    pub fn write(&self, stream: &mut PacketStream) {
        stream.write_u32(self.time);
        stream.write_u8(self.flags.0);
        if self.flags.contains(MoveTypeFlags::HAS_SC_TYPE_AND_PHASE) {
            stream.write_u32(self.sc_type);
            stream.write_u8(self.phase);
        }
        self.kind.write_body(stream);
    }

    /// `MoveType.Type` as an enum (`None` for raw values C# has no name for).
    pub fn move_type(&self) -> Option<MoveTypeEnum> {
        MoveTypeEnum::from_u8(self.type_raw)
    }

    /// Base `X`/`Y`/`Z`. `None` for `ShipRequestMoveType`, which never sets them (C# leaves 0).
    pub fn position(&self) -> Option<(f32, f32, f32)> {
        match &self.kind {
            MoveKind::Default(m) => Some((m.x, m.y, m.z)),
            MoveKind::Unit(m) => Some((m.x, m.y, m.z)),
            MoveKind::Vehicle(m) => Some((m.x, m.y, m.z)),
            MoveKind::Ship(m) => Some((m.x, m.y, m.z)),
            MoveKind::Transfer(m) => Some((m.x, m.y, m.z)),
            MoveKind::ShipRequest(_) => None,
        }
    }

    /// Base `VelX`/`VelY`/`VelZ`. Zero for `ShipRequestMoveType` and `TransferData`, which never
    /// set them (Transfer uses the float `Velocity` vector instead).
    pub fn velocity(&self) -> (i16, i16, i16) {
        match &self.kind {
            MoveKind::Default(m) => (m.vel_x, m.vel_y, m.vel_z),
            MoveKind::Unit(m) => (m.vel_x, m.vel_y, m.vel_z),
            MoveKind::Vehicle(m) => (m.vel_x, m.vel_y, m.vel_z),
            MoveKind::Ship(m) => (m.vel_x, m.vel_y, m.vel_z),
            MoveKind::ShipRequest(_) | MoveKind::Transfer(_) => (0, 0, 0),
        }
    }

    /// Base (sbyte) `RotationX`/`RotationY`/`RotationZ`. Vehicle/Ship shadow these with `short`
    /// properties, so the base values stay 0 for them (as for ShipRequest/Transfer).
    pub fn base_rotation(&self) -> (i8, i8, i8) {
        match &self.kind {
            MoveKind::Default(m) => (m.rotation_x, m.rotation_y, m.rotation_z),
            MoveKind::Unit(m) => (m.rotation_x, m.rotation_y, m.rotation_z),
            _ => (0, 0, 0),
        }
    }
}

impl Marshal for MoveType {
    fn write(&self, stream: &mut PacketStream) {
        MoveType::write(self, stream);
    }
}

/// C# `DefaultMoveType`.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DefaultMove {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub vel_x: i16,
    pub vel_y: i16,
    pub vel_z: i16,
    /// Read as `(sbyte)ReadInt16()` (high byte discarded), written as `(short)` (sign-extended).
    pub rotation_x: i8,
    pub rotation_y: i8,
    pub rotation_z: i8,
}

impl DefaultMove {
    fn read(&mut self, s: &mut PacketStream) -> Result<()> {
        (self.x, self.y, self.z) = s.read_position()?;
        self.vel_x = s.read_i16()?;
        self.vel_y = s.read_i16()?;
        self.vel_z = s.read_i16()?;
        // Mirrors C#: `(sbyte)stream.ReadInt16()` truncates to the low byte.
        self.rotation_x = s.read_i16()? as i8;
        self.rotation_y = s.read_i16()? as i8;
        self.rotation_z = s.read_i16()? as i8;
        Ok(())
    }

    fn write(&self, s: &mut PacketStream) {
        s.write_position(self.x, self.y, self.z)
            .write_i16(self.vel_x)
            .write_i16(self.vel_y)
            .write_i16(self.vel_z)
            .write_i16(i16::from(self.rotation_x))
            .write_i16(i16::from(self.rotation_y))
            .write_i16(i16::from(self.rotation_z));
    }
}

/// C# `UnitMoveType` (players, NPCs, mates).
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct UnitMove {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub vel_x: i16,
    pub vel_y: i16,
    pub vel_z: i16,
    pub rotation_x: i8,
    pub rotation_y: i8,
    pub rotation_z: i8,
    /// `DeltaMovement[0..3]`.
    pub delta_movement: [i8; 3],
    pub stance: GameStanceType,
    pub alertness: MoveTypeAlertness,
    /// `ActorFlags` (raw byte in C#); see [`MoveTypeActorFlags`].
    pub actor_flags: MoveTypeActorFlags,
    /// `actor.fallVel`, present when `actor_flags & 0x80`.
    pub fall_vel: u16,
    /// `actor.gcFlags`, present when `actor_flags & 0x20`.
    pub gc_flags: u8,
    /// `actor.gcPartId`, present when `actor_flags & 0x20`.
    pub gc_part_id: u16,
    /// Position relative to the gc object (`ix`/`iy`/`iz`), present when `actor_flags & 0x20`.
    pub x2: f32,
    pub y2: f32,
    pub z2: f32,
    pub rotation_x2: i8,
    pub rotation_y2: i8,
    pub rotation_z2: i8,
    /// `actor.gcId`, present when `actor_flags & 0x60 != 0`.
    pub gc_id: u32,
    /// `actor.climbData`, present when `actor_flags & 0x40`.
    pub climb_data: u32,
}

impl UnitMove {
    fn read(&mut self, s: &mut PacketStream) -> Result<()> {
        (self.x, self.y, self.z) = s.read_position()?;
        self.vel_x = s.read_i16()?;
        self.vel_y = s.read_i16()?;
        self.vel_z = s.read_i16()?;
        self.rotation_x = s.read_i8()?;
        self.rotation_y = s.read_i8()?;
        self.rotation_z = s.read_i8()?;
        self.delta_movement = [s.read_i8()?, s.read_i8()?, s.read_i8()?];
        self.stance = GameStanceType(s.read_u8()?);
        self.alertness = MoveTypeAlertness(s.read_u8()?);
        let af = s.read_u8()?;
        self.actor_flags = MoveTypeActorFlags(af);
        if af & 0x80 == 0x80 {
            self.fall_vel = s.read_u16()?;
        }
        if af & 0x20 == 0x20 {
            self.gc_flags = s.read_u8()?;
            self.gc_part_id = s.read_u16()?;
            (self.x2, self.y2, self.z2) = s.read_position()?;
            self.rotation_x2 = s.read_i8()?;
            self.rotation_y2 = s.read_i8()?;
            self.rotation_z2 = s.read_i8()?;
        }
        if af & 0x60 != 0 {
            self.gc_id = s.read_u32()?;
        }
        if af & 0x40 == 0x40 {
            self.climb_data = s.read_u32()?;
        }
        Ok(())
    }

    fn write(&self, s: &mut PacketStream) {
        let af = self.actor_flags.0;
        s.write_position(self.x, self.y, self.z)
            .write_i16(self.vel_x)
            .write_i16(self.vel_y)
            .write_i16(self.vel_z)
            .write_i8(self.rotation_x)
            .write_i8(self.rotation_y)
            .write_i8(self.rotation_z)
            .write_i8(self.delta_movement[0])
            .write_i8(self.delta_movement[1])
            .write_i8(self.delta_movement[2])
            .write_u8(self.stance.0)
            .write_u8(self.alertness.0)
            .write_u8(af);
        if af & 0x80 == 0x80 {
            s.write_u16(self.fall_vel);
        }
        if af & 0x20 == 0x20 {
            s.write_u8(self.gc_flags)
                .write_u16(self.gc_part_id)
                .write_position(self.x2, self.y2, self.z2)
                .write_i8(self.rotation_x2)
                .write_i8(self.rotation_y2)
                .write_i8(self.rotation_z2);
        }
        if af & 0x60 != 0 {
            s.write_u32(self.gc_id);
        }
        if af & 0x40 == 0x40 {
            s.write_u32(self.climb_data);
        }
    }
}

/// C# `VehicleMoveType` (cars, carts). `RotationX/Y/Z` are the `new short` shadowing properties
/// (quaternion components scaled by 32767, see [`get_slave_rotation_in_degrees`]).
#[derive(Debug, Clone, PartialEq, Default)]
pub struct VehicleMove {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub vel_x: i16,
    pub vel_y: i16,
    pub vel_z: i16,
    pub rotation_x: i16,
    pub rotation_y: i16,
    pub rotation_z: i16,
    pub ang_vel_x: f32,
    pub ang_vel_y: f32,
    pub ang_vel_z: f32,
    /// Between -1.0 and +1.0.
    pub steering: f32,
    /// `WheelAngVel`, prefixed on the wire by a `byte` count.
    pub wheel_ang_vel: Vec<f32>,
}

impl VehicleMove {
    fn read(&mut self, s: &mut PacketStream) -> Result<()> {
        (self.x, self.y, self.z) = s.read_position()?;
        self.vel_x = s.read_i16()?;
        self.vel_y = s.read_i16()?;
        self.vel_z = s.read_i16()?;
        self.rotation_x = s.read_i16()?;
        self.rotation_y = s.read_i16()?;
        self.rotation_z = s.read_i16()?;
        self.ang_vel_x = s.read_f32()?;
        self.ang_vel_y = s.read_f32()?;
        self.ang_vel_z = s.read_f32()?;
        self.steering = s.read_f32()?;
        let n = s.read_u8()?;
        // C# appends to the (initially empty) list.
        for _ in 0..n {
            self.wheel_ang_vel.push(s.read_f32()?);
        }
        Ok(())
    }

    fn write(&self, s: &mut PacketStream) {
        s.write_position(self.x, self.y, self.z)
            .write_i16(self.vel_x)
            .write_i16(self.vel_y)
            .write_i16(self.vel_z)
            .write_i16(self.rotation_x)
            .write_i16(self.rotation_y)
            .write_i16(self.rotation_z)
            .write_f32(self.ang_vel_x)
            .write_f32(self.ang_vel_y)
            .write_f32(self.ang_vel_z)
            .write_f32(self.steering)
            // Mirrors C# `(byte)WheelAngVel.Count`: truncates, yet every entry is written below.
            .write_u8(self.wheel_ang_vel.len() as u8);
        for &f in &self.wheel_ang_vel {
            s.write_f32(f);
        }
    }
}

/// C# `ShipMoveType`. `RotationX/Y/Z` are the `new short` shadowing properties.
/// (`UseSlaveBase(Slave)` is not ported here; it needs the slave model.)
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct ShipMove {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub vel_x: i16,
    pub vel_y: i16,
    pub vel_z: i16,
    pub rotation_x: i16,
    pub rotation_y: i16,
    pub rotation_z: i16,
    pub ang_vel_x: f32,
    pub ang_vel_y: f32,
    pub ang_vel_z: f32,
    pub steering: i8,
    pub throttle: i8,
    pub zone_id: u16,
    /// `ReadBoolean` is `byte == 1`; written back as 0/1.
    pub stuck: bool,
}

impl ShipMove {
    fn read(&mut self, s: &mut PacketStream) -> Result<()> {
        (self.x, self.y, self.z) = s.read_position()?;
        self.vel_x = s.read_i16()?;
        self.vel_y = s.read_i16()?;
        self.vel_z = s.read_i16()?;
        self.rotation_x = s.read_i16()?;
        self.rotation_y = s.read_i16()?;
        self.rotation_z = s.read_i16()?;
        self.ang_vel_x = s.read_f32()?;
        self.ang_vel_y = s.read_f32()?;
        self.ang_vel_z = s.read_f32()?;
        self.steering = s.read_i8()?;
        self.throttle = s.read_i8()?;
        self.zone_id = s.read_u16()?;
        self.stuck = s.read_bool()?;
        Ok(())
    }

    fn write(&self, s: &mut PacketStream) {
        s.write_position(self.x, self.y, self.z)
            .write_i16(self.vel_x)
            .write_i16(self.vel_y)
            .write_i16(self.vel_z)
            .write_i16(self.rotation_x)
            .write_i16(self.rotation_y)
            .write_i16(self.rotation_z)
            .write_f32(self.ang_vel_x)
            .write_f32(self.ang_vel_y)
            .write_f32(self.ang_vel_z)
            .write_i8(self.steering)
            .write_i8(self.throttle)
            .write_u16(self.zone_id)
            .write_bool(self.stuck);
    }
}

/// C# `ShipRequestMoveType` (helmsman input). Note the wire order is throttle, then steering.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct ShipRequestMove {
    pub throttle: i8,
    pub steering: i8,
}

impl ShipRequestMove {
    fn read(&mut self, s: &mut PacketStream) -> Result<()> {
        self.throttle = s.read_i8()?;
        self.steering = s.read_i8()?;
        Ok(())
    }

    fn write(&self, s: &mut PacketStream) {
        s.write_i8(self.throttle).write_i8(self.steering);
    }
}

/// C# `TransferData` (carriages/boats on fixed paths).
/// (`UseTransferBase(Transfer)` is not ported here; it needs the transfer model.)
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct TransferMove {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    /// Base `WorldPos`, recomputed from `x`/`y`/`z` on read (the C# constructor sets it from
    /// the zero position). Not on the wire.
    pub world_pos: WorldPos,
    /// Base `Velocity`: read as `ReadVector3Short() * 50`, written as `WriteVector3Short(v * 0.02)`.
    pub velocity: [f32; 3],
    /// Base `Rot` (x, y, z, w): quaternion written without the scalar.
    pub rot: [f32; 4],
    pub ang_vel: [f32; 3],
    pub steering: i32,
    pub path_point_index: i32,
    pub speed: f32,
    pub reverse: bool,
    /// Not on the wire.
    pub rotation_degrees: f32,
    /// Not on the wire.
    pub rot_speed: f32,
    /// Not on the wire.
    pub throttle: i8,
}

impl Default for TransferMove {
    fn default() -> Self {
        Self {
            x: 0.0,
            y: 0.0,
            z: 0.0,
            // `TransferData()` constructor.
            world_pos: WorldPos::from_xyz(0.0, 0.0, 0.0),
            velocity: [0.0; 3],
            // `default(Quaternion)` is all zeros, not identity.
            rot: [0.0; 4],
            ang_vel: [0.0; 3],
            steering: 0,
            path_point_index: 0,
            speed: 0.0,
            reverse: false,
            rotation_degrees: 0.0,
            rot_speed: 0.0,
            throttle: 0,
        }
    }
}

impl TransferMove {
    fn read(&mut self, s: &mut PacketStream) -> Result<()> {
        (self.x, self.y, self.z) = s.read_position()?;
        self.world_pos = WorldPos::from_xyz(self.x, self.y, self.z);
        let v = s.read_vector3_short()?;
        self.velocity = [v[0] * 50.0, v[1] * 50.0, v[2] * 50.0];
        self.rot = s.read_quaternion_short()?;
        self.ang_vel = s.read_vector3_f32()?;
        self.steering = s.read_i32()?;
        self.path_point_index = s.read_i32()?;
        self.speed = s.read_f32()?;
        self.reverse = s.read_bool()?;
        Ok(())
    }

    fn write(&self, s: &mut PacketStream) {
        let v = self.velocity;
        s.write_position(self.x, self.y, self.z)
            .write_vector3_short([v[0] * 0.02, v[1] * 0.02, v[2] * 0.02])
            .write_quaternion_short(self.rot, false)
            .write_vector3_f32(self.ang_vel)
            .write_i32(self.steering)
            .write_i32(self.path_point_index)
            .write_f32(self.speed)
            .write_bool(self.reverse);
    }
}

// ---- packet bodies ------------------------------------------------------------------------------

/// Body of `SCOneUnitMovementPacket.Write`: `WriteBc(id)`, `(byte)type.Type`, `type.Write`.
pub fn write_sc_one_unit_movement(stream: &mut PacketStream, id: u32, move_type: &MoveType) {
    stream.write_bc(id).write_u8(move_type.type_raw);
    move_type.write(stream);
}

/// Body of `SCUnitMovementsPacket.Write`: `(ushort)count`, then per entry `WriteBc(id)` and
/// `type.Write`.
///
/// NOTE: mirrors the C#, which does NOT write the per-entry type byte (unlike
/// `SCOneUnitMovementPacket`). The C# also notes a TODO that the max count is 400; the count is
/// truncated to `u16` without a check.
pub fn write_sc_unit_movements<'a, I>(stream: &mut PacketStream, movements: I)
where
    I: IntoIterator<Item = (u32, &'a MoveType)>,
    I::IntoIter: ExactSizeIterator,
{
    let it = movements.into_iter();
    stream.write_u16(it.len() as u16);
    for (id, move_type) in it {
        stream.write_bc(id);
        move_type.write(stream);
    }
}

// ---- MathUtil helpers ---------------------------------------------------------------------------

/// `MathUtil.ConvertSbyteDirectionToDegree`: `(direction * (360f / 128)) % 360f`, widened.
pub fn convert_sbyte_direction_to_degree(direction: i8) -> f64 {
    let angle = f32::from(direction) * (360.0f32 / 128.0);
    f64::from(angle % 360.0)
}

/// `MathUtil.ConvertDirectionToRadian(sbyte)`.
pub fn convert_direction_to_radian(direction: i8) -> f64 {
    convert_sbyte_direction_to_degree(direction) * std::f64::consts::PI / 180.0
}

/// `MathUtil.ConvertRadianToDirection(double)`.
///
/// The `(sbyte)` casts of doubles use .NET 9+ saturating semantics (the C# targets net10.0),
/// which is what Rust `as` does.
pub fn convert_radian_to_direction(radian: f64) -> i8 {
    // NumericExtensions.RadToDeg(double)
    let mut degree = radian / std::f64::consts::PI * 180.0;
    if degree < 0.0 {
        degree += 360.0;
    }
    let step = f64::from(360.0f32 / 128.0);
    let mut res = (degree / step) as i8;
    if res > 85 {
        res = ((degree - 360.0) / step) as i8;
    }
    res
}

/// `MathUtil.GetYawPitchRollFromQuat(Quaternion)`; `quat` is (x, y, z, w).
/// Despite the name it returns `(roll, pitch, yaw)` in radians.
pub fn get_yaw_pitch_roll_from_quat(quat: [f32; 4]) -> (f32, f32, f32) {
    let [x, y, z, w] = quat;
    let roll =
        f64::from(2.0 * (w * x + y * z)).atan2(f64::from(1.0 - 2.0 * (x * x + y * y))) as f32;
    let sinp = 2.0 * (w * y - z * x);
    let pitch = if sinp.abs() >= 1.0 {
        std::f64::consts::FRAC_PI_2.copysign(f64::from(sinp)) as f32
    } else {
        f64::from(sinp).asin() as f32
    };
    let yaw = f64::from(2.0 * (w * z + x * y)).atan2(f64::from(1.0 - 2.0 * (y * y + z * z))) as f32;
    (roll, pitch, yaw)
}

/// `MathUtil.GetSlaveRotationInDegrees(short, short, short)`.
///
/// Mirrors the C#: despite the name, the result is `GetYawPitchRollFromQuat`, i.e.
/// `(roll, pitch, yaw)` in RADIANS. Note the scale `0.00003052f` differs slightly from the
/// `1/32767` used by `PacketStream`.
pub fn get_slave_rotation_in_degrees(rot_x: i16, rot_y: i16, rot_z: i16) -> (f32, f32, f32) {
    let qx = f32::from(rot_x) * 0.000_030_52;
    let qy = f32::from(rot_y) * 0.000_030_52;
    let qz = f32::from(rot_z) * 0.000_030_52;
    let norm = qx * qx + qy * qy + qz * qz;
    let qw = if f64::from(norm) < 0.99750 {
        (1.0 - f64::from(norm)).sqrt() as f32
    } else {
        0.0
    };
    get_yaw_pitch_roll_from_quat([qx, qy, qz, qw])
}

#[cfg(test)]
mod tests {
    use super::*;
    use aaemu_commons::helpers::{decode_position, encode_position};

    fn bytes(parts: &[&[u8]]) -> Vec<u8> {
        parts.concat()
    }

    /// Decodes `data` as move type `ty`, asserts all bytes were consumed and that re-encoding
    /// yields the same bytes.
    fn roundtrip(ty: u8, data: &[u8]) -> MoveType {
        let mut s = PacketStream::from_bytes(data.to_vec());
        let m = MoveType::read(ty, &mut s).expect("decode");
        assert_eq!(s.remaining(), 0, "trailing bytes");
        let mut out = PacketStream::new();
        m.write(&mut out);
        assert_eq!(out.as_bytes(), data);
        m
    }

    fn header(time: u32, flags: u8) -> Vec<u8> {
        let mut v = time.to_le_bytes().to_vec();
        v.push(flags);
        v
    }

    fn pos_a() -> [u8; 9] {
        encode_position(17_123.25, 9_876.5, 112.375)
    }

    fn pos_b() -> [u8; 9] {
        encode_position(-3.5, 4.25, -12.0)
    }

    #[test]
    fn position_bytes_roundtrip() {
        for p in [pos_a(), pos_b(), encode_position(0.0, 0.0, 0.0)] {
            let (x, y, z) = decode_position(&p);
            assert_eq!(encode_position(x, y, z), p);
        }
    }

    #[test]
    fn default_roundtrip() {
        let data = bytes(&[
            &header(0x0102_0304, 0x02),
            &pos_a(),
            &10i16.to_le_bytes(),
            &(-20i16).to_le_bytes(),
            &300i16.to_le_bytes(),
            &5i16.to_le_bytes(),
            &(-7i16).to_le_bytes(),
            &127i16.to_le_bytes(),
        ]);
        let m = roundtrip(0, &data);
        assert_eq!(m.type_raw, 0);
        assert_eq!(m.time, 0x0102_0304);
        assert_eq!(m.flags, MoveTypeFlags::MOVING);
        assert_eq!(m.velocity(), (10, -20, 300));
        assert_eq!(m.base_rotation(), (5, -7, 127));
        // z is quantised by the 9-byte packing (22-bit fixed point).
        assert_eq!(m.position(), Some(decode_position(&pos_a())));
        assert_eq!(&m.position().unwrap().0, &17_123.25);
    }

    #[test]
    fn default_rotation_truncates_high_byte() {
        // C#: (sbyte)ReadInt16() then Write((short)sbyte) -> 0x0180 becomes 0xFF80.
        let mut data = bytes(&[&header(1, 0), &pos_b(), &[0; 6]]);
        data.extend_from_slice(&0x0180i16.to_le_bytes());
        data.extend_from_slice(&0x0042i16.to_le_bytes());
        data.extend_from_slice(&0x7F01i16.to_le_bytes());
        let m = MoveType::read(0, &mut PacketStream::from_bytes(data.clone())).unwrap();
        assert_eq!(m.base_rotation(), (-128, 0x42, 1));
        let mut out = PacketStream::new();
        m.write(&mut out);
        let n = data.len();
        assert_eq!(&out.as_bytes()[..n - 6], &data[..n - 6]);
        assert_eq!(
            &out.as_bytes()[n - 6..],
            &[0x80, 0xFF, 0x42, 0x00, 0x01, 0x00]
        );
    }

    #[test]
    fn unknown_type_uses_default_layout() {
        let data = bytes(&[
            &header(99, 0x04),
            &pos_b(),
            &[1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0],
        ]);
        let m = roundtrip(42, &data);
        assert_eq!(m.type_raw, 42);
        assert_eq!(m.move_type(), None);
        assert!(matches!(m.kind, MoveKind::Default(_)));
        let mut out = PacketStream::new();
        write_sc_one_unit_movement(&mut out, 0x0A0B0C, &m);
        assert_eq!(&out.as_bytes()[..4], &[0x0C, 0x0B, 0x0A, 42]);
    }

    #[test]
    fn sc_type_and_phase_flag() {
        let data = bytes(&[
            &header(7, 0x10 | 0x02),
            &0xDEAD_BEEFu32.to_le_bytes(),
            &[9],
            &[0x7F, 0x80],
        ]);
        let m = roundtrip(4, &data);
        assert_eq!((m.sc_type, m.phase), (0xDEAD_BEEF, 9));
        assert!(m.flags.contains(MoveTypeFlags::HAS_SC_TYPE_AND_PHASE));
        assert_eq!(
            m.kind,
            MoveKind::ShipRequest(ShipRequestMove {
                throttle: 127,
                steering: -128
            })
        );
        assert_eq!(m.position(), None);
        assert_eq!(m.velocity(), (0, 0, 0));
    }

    fn unit_bytes(flags: u8, actor_flags: u8) -> Vec<u8> {
        let mut v = header(0xAABB_CCDD, flags);
        if flags & 0x10 != 0 {
            v.extend_from_slice(&[1, 2, 3, 4, 5]); // ScType, Phase
        }
        v.extend_from_slice(&pos_a());
        v.extend_from_slice(&1i16.to_le_bytes());
        v.extend_from_slice(&(-2i16).to_le_bytes());
        v.extend_from_slice(&3i16.to_le_bytes());
        v.extend_from_slice(&[0x10, 0xF0, 0x7F]); // rotation
        v.extend_from_slice(&[0xFF, 0x01, 0x80]); // delta movement
        v.extend_from_slice(&[1, 2, actor_flags]); // stance, alertness, actor flags
        if actor_flags & 0x80 != 0 {
            v.extend_from_slice(&0x1234u16.to_le_bytes());
        }
        if actor_flags & 0x20 != 0 {
            v.push(0x5A);
            v.extend_from_slice(&0x0102u16.to_le_bytes());
            v.extend_from_slice(&pos_b());
            v.extend_from_slice(&[0xFE, 0x02, 0x40]);
        }
        if actor_flags & 0x60 != 0 {
            v.extend_from_slice(&0x0BAD_F00Du32.to_le_bytes());
        }
        if actor_flags & 0x40 != 0 {
            v.extend_from_slice(&0x1357_9BDFu32.to_le_bytes());
        }
        v
    }

    #[test]
    fn unit_all_actor_flag_combinations() {
        for flags in [0x00u8, 0x10, 0x16] {
            for af in 0u16..=0xFF {
                let af = af as u8;
                let data = unit_bytes(flags, af);
                let m = roundtrip(1, &data);
                let MoveKind::Unit(u) = m.kind else {
                    panic!("expected unit")
                };
                assert_eq!(u.actor_flags.0, af);
                assert_eq!((u.rotation_x, u.rotation_y, u.rotation_z), (0x10, -16, 127));
                assert_eq!(u.delta_movement, [-1, 1, -128]);
                assert_eq!(u.stance, GameStanceType::RELAXED);
                assert_eq!(u.alertness, MoveTypeAlertness::COMBAT);
                assert_eq!(u.fall_vel, if af & 0x80 != 0 { 0x1234 } else { 0 });
                assert_eq!(u.gc_flags, if af & 0x20 != 0 { 0x5A } else { 0 });
                assert_eq!(u.gc_id, if af & 0x60 != 0 { 0x0BAD_F00D } else { 0 });
                assert_eq!(u.climb_data, if af & 0x40 != 0 { 0x1357_9BDF } else { 0 });
                if flags & 0x10 != 0 {
                    assert_eq!((m.sc_type, m.phase), (0x0403_0201, 5));
                }
            }
        }
    }

    #[test]
    fn unit_gc_block_fields() {
        let data = unit_bytes(0, 0x20);
        let m = roundtrip(1, &data);
        let MoveKind::Unit(u) = m.kind else { panic!() };
        assert_eq!(u.gc_part_id, 0x0102);
        assert_eq!((u.x2, u.y2, u.z2), decode_position(&pos_b()));
        assert_eq!((u.x2, u.y2), (-3.5, 4.25));
        assert_eq!((u.rotation_x2, u.rotation_y2, u.rotation_z2), (-2, 2, 0x40));
        assert_eq!(u.climb_data, 0);
        assert_eq!(m.velocity(), (1, -2, 3));
    }

    #[test]
    fn vehicle_roundtrip() {
        for wheels in [0usize, 1, 4] {
            let mut data = bytes(&[
                &header(55, 0x02),
                &pos_a(),
                &[1, 0, 2, 0, 3, 0],
                &(-32768i16).to_le_bytes(),
                &1000i16.to_le_bytes(),
                &32767i16.to_le_bytes(),
                &0.5f32.to_le_bytes(),
                &(-1.25f32).to_le_bytes(),
                &f32::MIN_POSITIVE.to_le_bytes(),
                &(-1.0f32).to_le_bytes(),
                &[wheels as u8],
            ]);
            for i in 0..wheels {
                data.extend_from_slice(&(i as f32 * 1.5).to_le_bytes());
            }
            let m = roundtrip(2, &data);
            let MoveKind::Vehicle(v) = &m.kind else {
                panic!()
            };
            assert_eq!(
                (v.rotation_x, v.rotation_y, v.rotation_z),
                (-32768, 1000, 32767)
            );
            assert_eq!(v.steering, -1.0);
            assert_eq!(v.wheel_ang_vel.len(), wheels);
            // Shadowed base rotation stays 0.
            assert_eq!(m.base_rotation(), (0, 0, 0));
        }
    }

    #[test]
    fn ship_roundtrip() {
        let data = bytes(&[
            &header(1234, 0x10),
            &7u32.to_le_bytes(),
            &[3],
            &pos_b(),
            &[0xFF, 0xFF, 0, 0x80, 1, 0],
            &[1, 0, 2, 0, 3, 0],
            &1.0f32.to_le_bytes(),
            &2.0f32.to_le_bytes(),
            &3.0f32.to_le_bytes(),
            &[0x81, 0x7F],
            &0x00D2u16.to_le_bytes(),
            &[1],
        ]);
        let m = roundtrip(3, &data);
        let MoveKind::Ship(s) = m.kind else { panic!() };
        assert_eq!(
            (s.steering, s.throttle, s.zone_id, s.stuck),
            (-127, 127, 210, true)
        );
        assert_eq!(m.velocity(), (-1, -32768, 1));
        assert_eq!((s.ang_vel_x, s.ang_vel_y, s.ang_vel_z), (1.0, 2.0, 3.0));
    }

    #[test]
    fn transfer_roundtrip() {
        let data = bytes(&[
            &header(86_400_000 - 1, 0x02),
            &pos_a(),
            // velocity shorts
            &1000i16.to_le_bytes(),
            &(-327i16).to_le_bytes(),
            &0i16.to_le_bytes(),
            // rot quaternion xyz shorts
            &0i16.to_le_bytes(),
            &(-12345i16).to_le_bytes(),
            &23170i16.to_le_bytes(),
            &0.25f32.to_le_bytes(),
            &(-0.5f32).to_le_bytes(),
            &8.0f32.to_le_bytes(),
            &(-1i32).to_le_bytes(),
            &17i32.to_le_bytes(),
            &5.5f32.to_le_bytes(),
            &[1],
        ]);
        let m = roundtrip(5, &data);
        assert_eq!(m.velocity(), (0, 0, 0));
        let MoveKind::Transfer(t) = m.kind else {
            panic!()
        };
        assert_eq!(t.world_pos, WorldPos::from_xyz(t.x, t.y, t.z));
        assert_eq!(t.world_pos.x, (17_123.25f32 * 4096.0) as i64 * (1i64 << 32));
        assert_eq!(
            t.velocity[0],
            1000.0 * aaemu_commons::packet_stream::SHORT_SCALE * 50.0
        );
        assert!(t.rot[3] > 0.0);
        assert_eq!(t.ang_vel, [0.25, -0.5, 8.0]);
        assert_eq!(
            (t.steering, t.path_point_index, t.speed, t.reverse),
            (-1, 17, 5.5, true)
        );
    }

    #[test]
    fn transfer_velocity_short_roundtrip_all() {
        // Velocity goes short -> *SCALE*50 -> *0.02*32767 -> short; must be stable for all shorts.
        for v in i16::MIN..=i16::MAX {
            let mut data = bytes(&[&header(0, 0), &pos_b()]);
            data.extend_from_slice(&v.to_le_bytes());
            data.extend_from_slice(&[0; 4]);
            data.extend_from_slice(&v.to_le_bytes());
            data.extend_from_slice(&[0; 4]);
            data.extend_from_slice(&[0; 12 + 4 + 4 + 4 + 1]);
            roundtrip(5, &data);
        }
    }

    #[test]
    fn transfer_default_world_pos() {
        assert_eq!(TransferMove::default().world_pos, WorldPos::default());
    }

    #[test]
    fn sc_one_unit_movement_body() {
        let m = MoveType {
            time: 2,
            ..MoveType::new(MoveKind::ShipRequest(ShipRequestMove {
                throttle: -1,
                steering: 1,
            }))
        };
        let mut s = PacketStream::new();
        write_sc_one_unit_movement(&mut s, 0x00_12_34_56, &m);
        assert_eq!(
            s.as_bytes(),
            &[0x56, 0x34, 0x12, 4, 2, 0, 0, 0, 0, 0xFF, 0x01]
        );
    }

    #[test]
    fn sc_unit_movements_body() {
        let a = MoveType::new(MoveKind::ShipRequest(ShipRequestMove {
            throttle: 3,
            steering: 4,
        }));
        let b = MoveType {
            flags: MoveTypeFlags::HAS_SC_TYPE_AND_PHASE,
            sc_type: 1,
            phase: 2,
            ..a.clone()
        };
        let mut s = PacketStream::new();
        write_sc_unit_movements(&mut s, [(1u32, &a), (0x00FF_FFFF, &b)]);
        assert_eq!(
            s.as_bytes(),
            &[
                2, 0, // count
                1, 0, 0, // id (no type byte follows)
                0, 0, 0, 0, 0, 3, 4, //
                0xFF, 0xFF, 0xFF, //
                0, 0, 0, 0, 0x10, 1, 0, 0, 0, 2, 3, 4,
            ]
        );
        let mut empty = PacketStream::new();
        write_sc_unit_movements(&mut empty, std::iter::empty());
        assert_eq!(empty.as_bytes(), &[0, 0]);
    }

    #[test]
    fn new_and_marshal() {
        let m = MoveType::new(MoveKind::Unit(UnitMove::default()));
        assert_eq!(m.move_type(), Some(MoveTypeEnum::Unit));
        let mut a = PacketStream::new();
        a.write(&m);
        let mut b = PacketStream::new();
        m.write(&mut b);
        assert_eq!(a, b);
        // 5 header + 9 pos + 6 vel + 3 rot + 3 delta + 3 (stance, alertness, actor flags)
        assert_eq!(a.len(), 29);
    }

    #[test]
    fn truncated_input_errors() {
        let data = unit_bytes(0, 0xE0);
        for n in 0..data.len() {
            let mut s = PacketStream::from_bytes(data[..n].to_vec());
            assert!(MoveType::read(1, &mut s).is_err(), "len {n}");
        }
    }

    #[test]
    fn direction_conversions() {
        assert_eq!(convert_sbyte_direction_to_degree(0), 0.0);
        assert_eq!(convert_sbyte_direction_to_degree(32), 90.0);
        assert_eq!(convert_sbyte_direction_to_degree(-64), -180.0);
        assert_eq!(convert_sbyte_direction_to_degree(127), 357.1875);
        assert_eq!(
            convert_direction_to_radian(64),
            180.0 * std::f64::consts::PI / 180.0
        );
        assert_eq!(
            convert_direction_to_radian(-32),
            -90.0 * std::f64::consts::PI / 180.0
        );

        assert_eq!(convert_radian_to_direction(0.0), 0);
        assert_eq!(convert_radian_to_direction(std::f64::consts::FRAC_PI_2), 32);
        // 270 deg -> 96 > 85 -> (270-360)/2.8125 = -32
        assert_eq!(
            convert_radian_to_direction(-std::f64::consts::FRAC_PI_2),
            -32
        );
        assert_eq!(convert_radian_to_direction(std::f64::consts::PI), 64);
        // Round trip for every direction the C# produces.
        for d in -42i8..=85 {
            let r = convert_direction_to_radian(d);
            let back = convert_radian_to_direction(r);
            assert!((i16::from(back) - i16::from(d)).abs() <= 1, "{d} -> {back}");
        }
    }

    #[test]
    fn slave_rotation() {
        let (r, p, y) = get_slave_rotation_in_degrees(0, 0, 0);
        assert_eq!((r, p, y), (0.0, 0.0, 0.0));
        // 90 deg yaw: z = sin(45 deg) = 0.7071 -> 23170 / 32767-ish
        let (r, p, y) = get_slave_rotation_in_degrees(0, 0, 23170);
        assert_eq!((r, p), (0.0, 0.0));
        assert!((y - std::f32::consts::FRAC_PI_2).abs() < 1e-3, "{y}");
        // Norm >= 0.9975 -> w = 0.
        let (_, _, y) = get_slave_rotation_in_degrees(0, 0, 32767);
        assert!((y - std::f32::consts::PI).abs() < 1e-3, "{y}");
        // Gimbal: sinp clamps.
        let (_, p, _) = get_yaw_pitch_roll_from_quat([0.0, 0.7072, 0.0, 0.7072]);
        assert_eq!(p, std::f32::consts::FRAC_PI_2);
    }
}

#[cfg(test)]
#[path = "world_pos_tests.rs"]
mod world_pos_tests;
