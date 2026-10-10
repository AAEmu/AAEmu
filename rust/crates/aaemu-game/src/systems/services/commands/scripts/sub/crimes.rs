//! `Scripts/SubCommands/Crimes/**` and `Scripts/Commands/CrimeCmd.cs`.

use futures::future::BoxFuture;

use super::super::prelude::*;
use crate::data::world_templates::WorldSpawnPosition;
use crate::models::character::DEFAULT_INSTANCE_ID;
use crate::models::doodad::doodad_owner_type;
use crate::systems::services::commands::output::PlayerCharacter;
use crate::systems::skills::packets_special::SCTeleportUnitPacket;
use crate::systems::social::config::SpawnPosition;
use crate::systems::social::crime::{self, buff, evidence_doodad};
use crate::systems::social::packets::trial::{
    SCAskImprisonOrTrialPacket, SCChangeTrialStatePacket, SCCrimeChangedPacket, SCInviteJuryPacket,
};

/// `TrialStep` member names.
const TRIAL_STEP_NAMES: [&str; 11] = [
    "DefendantAwaitingTrial",
    "VerifyCriminalRecord",
    "AwaitingJurySummons",
    "ConfirmCriminalRecord",
    "ClosingStatement",
    "JuryVerdict",
    "TrialCancelled",
    "PleadGuilty",
    "EndTrial",
    "Cleanup",
    "Invalid",
];

/// `SubCommandBase` with `Title`, `Description`, `CallPrefix` and its parameters.
fn base_with(
    title: &str,
    description: &str,
    call_prefix: &str,
    params: Vec<Box<dyn SubCommandParameter>>,
) -> SubCommandBase {
    let mut b = SubCommandBase::new(
        title,
        description,
        &format!("{COMMAND_PREFIX}{call_prefix}"),
    );
    for p in params {
        b.add_parameter(p)
            .expect("sub command parameters are declared in a valid order");
    }
    b
}

/// `new StringSubCommandParameter(name, displayName, true)`.
fn string_param(name: &str, display: &str) -> Box<dyn SubCommandParameter> {
    Box::new(StringSubCommandParameter::new(
        name,
        Some(display),
        true,
        &[],
    ))
}

/// `new NumericSubCommandParameter<T>(name, displayName, required[, prefix])`.
fn num_param<T: NumericKind + std::fmt::Debug>(
    name: &str,
    display: &str,
    required: bool,
    prefix: Option<&str>,
) -> Box<dyn SubCommandParameter> {
    Box::new(NumericSubCommandParameter::<T>::with_prefix(
        name,
        Some(display),
        required,
        prefix,
    ))
}

use crate::systems::services::commands::params::NumericKind;

/// The `player name||target||self` lookup every crime sub command starts with (`what` is
/// "player" or "victim"). `None` = an error was reported.
fn resolve_character(
    base: &SubCommandBase,
    server: &GameServer,
    self_character: &PlayerRef,
    parameter: &str,
    what: &str,
    out: &mut dyn MessageOutput,
) -> Option<PlayerRef> {
    let found = if parameter == "target" {
        match current_target(server, self_character).and_then(|t| t.as_player().cloned()) {
            Some(p) => Some(p),
            None => {
                base.send_color_message(
                    out,
                    Color::RED,
                    &format!("Please select a valid character {what}"),
                );
                return None;
            }
        }
    } else if parameter == "self" {
        Some(self_character.clone())
    } else {
        server.world.character_by_name(parameter)
    };
    if found.is_none() {
        base.send_color_message(
            out,
            Color::RED,
            &format!("Character {what}: {parameter} was not found."),
        );
    }
    found
}

/// `WorldSpawnPosition.ToString()`.
fn world_spawn_position_string(x: f32, y: f32, z: f32, roll: f32, pitch: f32, yaw: f32) -> String {
    let g = |v: f32| group_1(f64::from(v));
    format!(
        "X:{} Y:{} Z:{}  r:{}° p:{}° y:{}°",
        g(x),
        g(y),
        g(z),
        g(roll),
        g(pitch),
        g(yaw)
    )
}

fn spawn_position_string(p: &SpawnPosition) -> String {
    world_spawn_position_string(p.x, p.y, p.z, 0.0, 0.0, p.yaw)
}

fn npc_spawn_position_string(p: &WorldSpawnPosition) -> String {
    world_spawn_position_string(p.x, p.y, p.z, p.roll, p.pitch, p.yaw)
}

/// `obj.Transform.World.Position`.
fn world_position(server: &GameServer, obj: &WorldObject) -> [f32; 3] {
    match server.world.world_of(obj) {
        Some(w) => {
            let p = w.world_pos_rot(obj);
            [p.x, p.y, p.z]
        }
        None => {
            let t = obj.transform();
            [t.x, t.y, t.z]
        }
    }
}

// ---------------------------------------------------------------------------------------------
// CrimeAddPointSubCommand
// ---------------------------------------------------------------------------------------------

/// `CrimeAddPointSubCommand`.
pub struct CrimeAddPointSubCommand {
    base: SubCommandBase,
}

impl Default for CrimeAddPointSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Crime Points]",
                "Changes the amount of crime, infamy or jury points of target player",
                "crime points",
                vec![
                    string_param("target", "player name||target||self"),
                    num_param::<i32>("crime", "crime point amount", false, Some("crime")),
                    num_param::<i32>("infamy", "infamy amount", false, Some("infamy")),
                    num_param::<i32>("jury", "jury points amount", false, Some("jury")),
                ],
            ),
        }
    }
}

impl SubCommand for CrimeAddPointSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let self_character = ctx.player()?;
        let first_parameter = parameters.require("target")?.to_string();
        let Some(target) = resolve_character(
            &self.base,
            server,
            &self_character,
            &first_parameter,
            "player",
            out,
        ) else {
            return Ok(());
        };

        // `(short)GetOptionalParameterValue(parameters, "crime", 0)`
        let crime_points =
            optional_parameter(parameters, "crime", 0, ParameterValue::as_i32)? as i16;
        let infamy_points = optional_parameter(parameters, "infamy", 0, ParameterValue::as_i32)?;
        let jury_points = optional_parameter(parameters, "jury", 0, ParameterValue::as_i32)?;
        let target_name = name_of(&target);

        if crime_points != 0 || infamy_points != 0 || jury_points != 0 {
            // If something changed, go to change mode
            let now = crate::world::now_ms();
            let (packet, jury_packet) = {
                let mut c = target.data();
                let cp = c.crime_point.wrapping_add(crime_points);
                crime::set_crime_point(server, &mut c, cp, now);
                let ip = c.infamy_point.wrapping_add(infamy_points);
                crime::set_infamy_point(server, &mut c, ip, now);
                let packet = SCCrimeChangedPacket {
                    points: i32::from(crime_points),
                    crime_points: c.crime_point,
                    infamy_points: c.infamy_point,
                    crime_state: crime::crime_state(&c),
                };
                let jp = c.jury_point.wrapping_add(jury_points);
                (packet, crime::set_jury_point(&mut c, jp))
            };
            target.send(packet);
            if let Some(p) = jury_packet {
                target.send(p);
            }
            crate::systems::dispatch_buff_events(server, &WorldObject::from(&target));

            let sign = |v: i32| if v < 0 { "" } else { "+" };
            let modified_string = format!(
                "crime {}{crime_points}, infamy {}{infamy_points}, jury points {}{jury_points}",
                sign(i32::from(crime_points)),
                sign(infamy_points),
                sign(jury_points)
            );
            self.base.send_message(
                out,
                &format!("Adjusted {target_name}'s justice points; {modified_string}"),
            );
            if is_other(&self_character, &target) {
                self.base.send_message_to(
                    &PlayerCharacter(target.clone()),
                    out,
                    &format!(
                        "[GM] {} has changed your justice points; {modified_string}",
                        name_of(&self_character)
                    ),
                );
            }
        } else {
            // If nothing changed, show target player's stats instead.
            let (faction_id, crime_point, infamy_point, jury_point) = {
                let c = target.data();
                (
                    c.unit.faction_id,
                    c.crime_point,
                    c.infamy_point,
                    c.jury_point,
                )
            };
            self.base.send_color_message(
                out,
                Color::WHITE,
                &format!("[{target_name}] justice stats;"),
            );
            // C#: `Faction.Id` of a character without a faction throws.
            let faction = server.game_data.factions.get(faction_id).ok_or_else(|| {
                anyhow::anyhow!("Object reference not set to an instance of an object.")
            })?;
            self.base.send_message(
                out,
                &format!("[Faction] {} (MotherId: {})", faction.id, faction.mother_id),
            );
            self.base
                .send_message(out, &format!("[Crime] {crime_point}"));
            self.base
                .send_message(out, &format!("[Infamy] {infamy_point}"));
            self.base.send_message(out, &format!("[Jury] {jury_point}"));
            // TODO: Add trial/guilty counts
        }
        Ok(())
    }
}

// ---------------------------------------------------------------------------------------------
// CrimeAskGuiltySubCommand
// ---------------------------------------------------------------------------------------------

/// `CrimeAskGuiltySubCommand`.
pub struct CrimeAskGuiltySubCommand {
    base: SubCommandBase,
}

impl Default for CrimeAskGuiltySubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Crime Ask Guilty]",
                "Asks player if they plead guilty or not.",
                "crime ask_guilty",
                vec![
                    string_param("target", "player name||target||self"),
                    num_param::<u32>("crime", "crime=<points>", true, Some("crime")),
                    num_param::<i32>("time", "time=<minutes>", true, Some("time")),
                ],
            ),
        }
    }
}

impl SubCommand for CrimeAskGuiltySubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let self_character = ctx.player()?;
        let first_parameter = parameters.require("target")?.to_string();
        let Some(target) = resolve_character(
            &self.base,
            server,
            &self_character,
            &first_parameter,
            "player",
            out,
        ) else {
            return Ok(());
        };
        let jail_time = parameters.require("time")?.as_i32()?;
        let crime_points = parameters.require("crime")?.as_u32()?;
        self.base.send_color_message(
            out,
            Color::WHITE,
            &format!(
                "Asking {} if they plead guilty for {jail_time} minutes ({crime_points} crime points).",
                name_of(&target)
            ),
        );
        target.send(SCAskImprisonOrTrialPacket {
            crime_value: crime_points,
            jail_minutes: jail_time,
        });
        Ok(())
    }
}

// ---------------------------------------------------------------------------------------------
// CrimeCourtSubCommand
// ---------------------------------------------------------------------------------------------

/// `CrimeCourtSubCommand`.
pub struct CrimeCourtSubCommand {
    base: SubCommandBase,
}

impl Default for CrimeCourtSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Crime Court]",
                "Does a jury invite request",
                "crime court",
                vec![
                    string_param("target", "player name||target||self"),
                    string_param("action", "none||list"),
                    num_param::<u32>("court", "court=<courtRoomId>", false, Some("court")),
                    num_param::<u32>("trial", "trial=<trialId>", false, Some("trial")),
                ],
            ),
        }
    }
}

impl SubCommand for CrimeCourtSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let self_character = ctx.player()?;
        let first_parameter = parameters.require("target")?.to_string();
        let Some(target) = resolve_character(
            &self.base,
            server,
            &self_character,
            &first_parameter,
            "player",
            out,
        ) else {
            return Ok(());
        };
        let target_name = name_of(&target);

        let action = optional_parameter(parameters, "action", "none".to_string(), |v| {
            v.as_str().map(str::to_string)
        })?
        .to_lowercase();
        self.base.send_color_message(
            out,
            Color::WHITE,
            &format!("[Crime Court {action}] Action for {target_name}"),
        );
        let court_room_id = optional_parameter(parameters, "court", 0u32, ParameterValue::as_u32)?;
        let trials = &server.systems.social.trials;
        let court_room = trials
            .court_rooms()
            .into_iter()
            .find(|r| r.id == court_room_id);
        match action.as_str() {
            "list" => {
                let Some(court_room) = court_room else {
                    self.base.send_color_message(
                        out,
                        Color::RED,
                        &format!("Invalid courtroom id: {court_room_id}."),
                    );
                    return Ok(());
                };
                let spawner = court_room.judge_spawner.and_then(|id| {
                    server
                        .systems
                        .npcs
                        .spawns
                        .get(DEFAULT_INSTANCE_ID)?
                        .get_special_npc_spawners("justice.")
                        .into_iter()
                        .find(|s| s.id == id)
                });
                // JudgeSpawner?.SpawnedNpcs?.LastOrDefault().Value?.LastOrDefault()
                let judge = spawner.as_ref().and_then(|s| {
                    s.spawned_npcs()
                        .into_values()
                        .next_back()
                        .and_then(|l| l.last().cloned())
                });
                let judge_pos = judge.map_or_else(
                    || "none".to_string(),
                    |j| vector3_string(world_position(server, &WorldObject::from(&j))),
                );
                self.base
                    .send_message(out, &format!("Name: {} ({court_room_id})", court_room.name));
                self.base.send_message(
                    out,
                    &format!(
                        "Judge: {judge_pos} from {} (spawnerId: {})",
                        spawner
                            .as_ref()
                            .map(|s| npc_spawn_position_string(&s.position()))
                            .unwrap_or_default(),
                        spawner
                            .as_ref()
                            .map(|s| s.id.to_string())
                            .unwrap_or_default()
                    ),
                );
                self.base.send_message(
                    out,
                    &format!(
                        "Defendant location: {}",
                        spawn_position_string(&court_room.defendant)
                    ),
                );
                self.base.send_message(
                    out,
                    &format!("Jail location: {}", spawn_position_string(&court_room.jail)),
                );
                for (seat_id, jury_seat) in &court_room.jury_seats {
                    self.base.send_message(
                        out,
                        &format!(
                            "Seat {seat_id}: {}",
                            vector3_string(world_position(server, &WorldObject::from(jury_seat)))
                        ),
                    );
                }
                match court_room.current_trial.and_then(|id| trials.get_trial(id)) {
                    None => self.base.send_message(out, "No ongoing trial"),
                    Some(trial) => self.base.send_message(
                        out,
                        &format!(
                            "Trial ongoing {} for {}, Step: {}",
                            trial.id,
                            name_of(&trial.defendant),
                            enum_name(i64::from(trial.step), &TRIAL_STEP_NAMES)
                        ),
                    ),
                }
            }
            "jail" => {
                let Some(court_room) = court_room else {
                    self.base.send_color_message(
                        out,
                        Color::RED,
                        &format!("Invalid courtroom id: {court_room_id}."),
                    );
                    return Ok(());
                };
                self.base.send_message(
                    out,
                    &format!(
                        "Moving {target_name} to jail of {} ({court_room_id})",
                        court_room.name
                    ),
                );
                server
                    .systems
                    .social
                    .hooks
                    .get()
                    .force_dismount(server, &target);
                {
                    let mut c = target.data();
                    c.disabled_set_position = true;
                    if !c.unit.buffs.check_buff(buff::FORCIBLY_AWAITING_TRIAL) {
                        crime::add_own_buff(
                            server,
                            &mut c,
                            buff::FORCIBLY_AWAITING_TRIAL,
                            0,
                            crate::world::now_ms(),
                        );
                    }
                }
                crate::systems::dispatch_buff_events(server, &WorldObject::from(&target));
                let jail = &court_room.jail;
                target.send(SCTeleportUnitPacket {
                    reason: 0,
                    error_message: 0,
                    x: jail.x,
                    y: jail.y,
                    z: jail.z,
                    z2: jail.yaw.to_radians(),
                });
            }
            _ => {}
        }
        Ok(())
    }
}

// ---------------------------------------------------------------------------------------------
// CrimeCreateSubCommand
// ---------------------------------------------------------------------------------------------

/// `CrimeCreateSubCommand`.
pub struct CrimeCreateSubCommand {
    base: SubCommandBase,
}

impl Default for CrimeCreateSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Crime Create]",
                &format!(
                    "Add a new crime evidence doodad of a specific template at your current location.\nValid templates: Small Bloodstain ({}), Large Bloodstain ({}), Footprint (male {}, female {})",
                    evidence_doodad::SMALL_BLOODSTAIN,
                    evidence_doodad::LARGE_BLOODSTAIN,
                    evidence_doodad::FOOTPRINT_MALE,
                    evidence_doodad::FOOTPRINT_FEMALE
                ),
                "crime create",
                vec![
                    num_param::<u32>("templateId", "template id", true, None),
                    num_param::<u32>("owner", "owner=<playerId>", true, Some("owner")),
                    num_param::<u32>("victim", "victim=<playerId>", true, Some("victim")),
                    num_param::<u32>("source", "source=<doodadTemplateId>", false, Some("source")),
                ],
            ),
        }
    }
}

impl SubCommand for CrimeCreateSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let unit_template_id = parameters.require("templateId")?.as_u32()?;
        let owner_id = parameters.require("owner")?.as_u32()?;
        let victim_id = parameters.require("victim")?.as_u32()?;
        // C#: `parameters["source"]` of an optional parameter throws KeyNotFoundException when
        // it was not given.
        let source_doodad_template_id = parameters.require("source")?.as_u32()?;
        let server = ctx.server()?;
        if !server.game_data.systems.npcs.doodad.exist(unit_template_id) {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad templateId:{unit_template_id} don't exist"),
            );
            return Ok(());
        }
        if ![
            evidence_doodad::SMALL_BLOODSTAIN,
            evidence_doodad::LARGE_BLOODSTAIN,
            evidence_doodad::FOOTPRINT_MALE,
            evidence_doodad::FOOTPRINT_FEMALE,
        ]
        .contains(&unit_template_id)
        {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad templateId:{unit_template_id} is not related to evidence."),
            );
            return Ok(());
        }

        let player = ctx.player()?;
        let world = parent_world(server, &player).ok_or_else(|| {
            anyhow::anyhow!("Object reference not set to an instance of an object.")
        })?;
        let char_pos = world.world_pos_rot(&WorldObject::from(&player));
        let zone_id = player.data().unit.transform.zone_id;
        // charPos.Local.AddDistanceToFront(3f)
        let (fx, fy) =
            crate::utils::math::add_distance_to_front(3.0, char_pos.x, char_pos.y, char_pos.rot_z);
        // C#: the angle is in degrees and is used as the (radian) yaw as-is.
        let default_yaw =
            crate::utils::math::calculate_angle_from(fx, fy, char_pos.x, char_pos.y) as f32;

        let Some(doodad) = crate::systems::npcs::doodad::manager::create(
            server,
            &world,
            0,
            unit_template_id,
            None,
            false,
        ) else {
            tracing::warn!("Evidence {unit_template_id}, was not able to spawn");
            return Ok(());
        };
        {
            let mut d = doodad.data();
            d.owner_id = owner_id;
            d.owner_type = doodad_owner_type::CHARACTER;
            d.transform.instance_id = world.id;
            d.transform.zone_id = zone_id;
            d.transform
                .set_position(fx, fy, char_pos.z, 0.0, 0.0, default_yaw);
            d.quest_glow = 0;
            d.item_template_id = source_doodad_template_id;
            d.data = victim_id as i32;
            d.plant_time = chrono::Utc::now().timestamp();
        }
        crate::systems::npcs::doodad::manager::spawn(server, &world, &doodad);

        ctx.character.send_message(&format!(
            "Crime Evidence Doodad ObjId:{}, Template {unit_template_id} spawned, Owner: {owner_id}, Victim: {victim_id}, Source: {source_doodad_template_id}",
            doodad.obj_id
        ));
        Ok(())
    }
}

// ---------------------------------------------------------------------------------------------
// CrimeFakeTrialSubCommand
// ---------------------------------------------------------------------------------------------

/// `CrimeFakeTrialSubCommand`.
pub struct CrimeFakeTrialSubCommand {
    base: SubCommandBase,
}

impl Default for CrimeFakeTrialSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Crime Fake Trial]",
                "Starts a fake trial for target player",
                "crime fake",
                vec![
                    string_param("target", "player name||target||self"),
                    string_param("victim", "player name||target||self"),
                    num_param::<u32>("evidence", "evidence=<count>", true, Some("evidence")),
                ],
            ),
        }
    }
}

/// The fake pine (`DoodadManager.CreatePlayerDoodad(victim, 398, ..., 14898, ...)`) of the theft
/// evidence and the uproot skill.
const FAKE_PINE_DOODAD: u32 = 398;
const FAKE_PINE_ITEM: u32 = 14898;
const UPROOT_SKILL: u32 = 13789;

impl CrimeFakeTrialSubCommand {
    async fn run(
        &self,
        ctx: &CommandContext<'_>,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let self_character = ctx.player()?;
        let first_parameter = parameters.require("target")?.to_string();
        let Some(target) = resolve_character(
            &self.base,
            server,
            &self_character,
            &first_parameter,
            "player",
            out,
        ) else {
            return Ok(());
        };
        let second_parameter = parameters.require("victim")?.to_string();
        let Some(victim) = resolve_character(
            &self.base,
            server,
            &self_character,
            &second_parameter,
            "victim",
            out,
        ) else {
            return Ok(());
        };
        if !is_other(&target, &victim) {
            self.base.send_color_message(
                out,
                Color::RED,
                "The victim can not be the same as the perpetrator.",
            );
            return Ok(());
        }

        let evidence_count = parameters.require("evidence")?.as_u32()?;
        self.base.send_color_message(
            out,
            Color::WHITE,
            &format!(
                "Putting {} on trial by using {evidence_count} new evidence(s) to frame them with {} as the victim.",
                name_of(&target),
                name_of(&victim)
            ),
        );

        // Generate evidence
        let crimes = &server.systems.social.crimes;
        let hooks = server.systems.social.hooks.get();
        let target_obj = WorldObject::from(&target);
        for i in 0..evidence_count {
            let pos = world_position(server, &target_obj);
            let mut fake_pine = None;
            let mut skill_id = 0u32;
            let rng: u32 = rand::random::<u32>() % 3;
            // rng = 1; // for fixed murder
            let evidence = match rng {
                0 => crimes.generate_evidence_from_damage(
                    server,
                    &target_obj,
                    Some(&target),
                    Some(&victim),
                ),
                1 => crimes.generate_evidence_from_kill(
                    server,
                    &target_obj,
                    Some(&target),
                    Some(&victim),
                ),
                _ => {
                    // CreatePlayerDoodad spawns the doodad (the C# `Spawn()` again is skipped).
                    let pine = hooks
                        .create_player_doodad(
                            server,
                            &victim,
                            FAKE_PINE_DOODAD,
                            (pos[0], pos[1], pos[2]),
                            0.0,
                            FAKE_PINE_ITEM,
                            0,
                        )
                        .ok_or_else(|| {
                            anyhow::anyhow!("Object reference not set to an instance of an object.")
                        })?;
                    skill_id = UPROOT_SKILL; // uproot
                    let e = crimes.generate_evidence_from_theft(server, Some(&target), &pine);
                    fake_pine = Some(pine);
                    e
                }
            };
            if let Some(evidence) = evidence {
                // evidence.CurrentFuncs?.FirstOrDefault()
                let (next_phase, func_id) = {
                    let group = evidence.data().func_group_id;
                    server
                        .game_data
                        .systems
                        .npcs
                        .doodad
                        .funcs_for_group(group)
                        .first()
                        .map_or((0, 0), |f| (f.next_phase, f.func_id))
                };
                // C#: `_ = ReportCrime(...)` (not awaited); awaited here.
                let _ = crimes
                    .report_crime(
                        server,
                        &victim,
                        &evidence,
                        skill_id,
                        next_phase,
                        func_id,
                        &format!("Report #{}", i + 1),
                    )
                    .await;
                if let Some(pine) = &fake_pine {
                    if let Some(w) = server.world.world_of(&WorldObject::from(pine)) {
                        crate::systems::npcs::doodad::manager::delete(server, &w, pine);
                    }
                }
            }
        }
        server
            .systems
            .social
            .trials
            .arrest_criminal(server, &target, Some(&victim));
        Ok(())
    }
}

impl SubCommand for CrimeFakeTrialSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _trigger: &str,
        _parameters: &Parameters,
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("CrimeFakeTrialSubCommand runs through execute_async")
    }

    fn execute_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        _trigger: &'a str,
        parameters: &'a Parameters,
        out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(self.run(ctx, parameters, out))
    }
}

// ---------------------------------------------------------------------------------------------
// CrimeJuryInviteSubCommand
// ---------------------------------------------------------------------------------------------

/// `CrimeJuryInviteSubCommand`.
pub struct CrimeJuryInviteSubCommand {
    base: SubCommandBase,
}

impl Default for CrimeJuryInviteSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Crime Jury Invite]",
                "Does a jury invite request",
                "crime jury_invite",
                vec![
                    string_param("target", "player name||target||self"),
                    Box::new(StringSubCommandParameter::with_prefix(
                        "defendant",
                        Some("defendant=<name>"),
                        false,
                        Some("defendant"),
                    )),
                    num_param::<u32>("trial", "trial=<trialId>", false, Some("trial")),
                ],
            ),
        }
    }
}

impl SubCommand for CrimeJuryInviteSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let self_character = ctx.player()?;
        let first_parameter = parameters.require("target")?.to_string();
        let Some(target) = resolve_character(
            &self.base,
            server,
            &self_character,
            &first_parameter,
            "player",
            out,
        ) else {
            return Ok(());
        };
        let trial_id = optional_parameter(parameters, "trial", 0u32, ParameterValue::as_u32)?;
        let defendant_name =
            optional_parameter(parameters, "defendant", "WantedCriminal".to_string(), |v| {
                v.as_str().map(str::to_string)
            })?;
        self.base.send_color_message(
            out,
            Color::WHITE,
            &format!("Summoning {} to trial {trial_id}", name_of(&target)),
        );
        target.send(SCInviteJuryPacket {
            defendant_name,
            trial: trial_id,
        });
        Ok(())
    }
}

// ---------------------------------------------------------------------------------------------
// CrimeSetTrialStateSubCommand
// ---------------------------------------------------------------------------------------------

/// `CrimeSetTrialStateSubCommand`.
pub struct CrimeSetTrialStateSubCommand {
    base: SubCommandBase,
}

impl Default for CrimeSetTrialStateSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Crime Set Trial State]",
                "Changes a trial state.",
                "crime trial_state",
                vec![
                    string_param("target", "player name||target||self"),
                    num_param::<u32>("trial", "trial=<trialId>", true, Some("trial")),
                    num_param::<u8>("state", "state=<stateId>", true, Some("state")),
                    num_param::<i32>("jury", "jury=<count>", true, Some("jury")),
                    num_param::<u32>("time", "time=<minutes>", true, Some("time")),
                ],
            ),
        }
    }
}

impl SubCommand for CrimeSetTrialStateSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let self_character = ctx.player()?;
        let first_parameter = parameters.require("target")?.to_string();
        let Some(target) = resolve_character(
            &self.base,
            server,
            &self_character,
            &first_parameter,
            "player",
            out,
        ) else {
            return Ok(());
        };
        let trial_id = parameters.require("trial")?.as_u32()?;
        let state = parameters.require("state")?.as_u8()?;
        let jury = parameters.require("jury")?.as_i32()?;
        let time_minutes = parameters.require("time")?.as_u32()?;
        let time = time_minutes.wrapping_mul(60_000); // time is in ms
        self.base.send_color_message(
            out,
            Color::WHITE,
            &format!(
                "Setting Trial {trial_id} to {state} ({}) with Jury {jury} and Time {time_minutes} minutes.",
                enum_name(i64::from(state), &TRIAL_STEP_NAMES)
            ),
        );
        target.send(SCChangeTrialStatePacket {
            trial_id,
            state,
            cur_jury: jury,
            remain_time: time,
        });
        Ok(())
    }
}

/// `CrimeCmd` (registered as `crime`; `CommandNames` is `["doodad"]` in C#, so the call prefix and help use `/doodad`).
pub fn crime_cmd() -> RootCommand {
    RootCommand::new(
        &["doodad"],
        "[Crime]",
        "Root command to crime related tasks",
        "",
    )
    .register(Arc::new(CrimeCreateSubCommand::default()), &["create"])
    .register(Arc::new(CrimeAddPointSubCommand::default()), &["points"])
    .register(
        Arc::new(CrimeJuryInviteSubCommand::default()),
        &["jury_invite", "ji"],
    )
    .register(
        Arc::new(CrimeAskGuiltySubCommand::default()),
        &["ask_guilty", "ag"],
    )
    .register(Arc::new(CrimeCourtSubCommand::default()), &["court"])
    .register(
        Arc::new(CrimeSetTrialStateSubCommand::default()),
        &["trial_state", "ts"],
    )
    .register(Arc::new(CrimeFakeTrialSubCommand::default()), &["fake"])
}

#[cfg(test)]
mod tests {
    use std::sync::Arc;

    use aaemu_commons::PacketStream;

    use super::super::super::harness;
    use crate::data::skills::{BuffKind, BuffTemplate};
    use crate::data::world_data::SystemFaction;
    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::server::GameServer;
    use crate::systems::npcs::doodad::templates::DoodadTemplate;
    use crate::systems::social::config::{CourtRoomConfig, JusticeConfig, SpawnPosition};
    use crate::systems::social::crime::buff;

    const NUIA: u32 = 148;

    fn data() -> GameData {
        let mut data = GameData::empty();
        for id in [
            buff::PRISONER_NUIAN,
            buff::PRISONER_HARANYAN,
            buff::FORCIBLY_AWAITING_TRIAL,
            buff::JURY,
            buff::TRIAL_DEFENDANT,
            buff::WANTED,
            buff::COURT_HOUSE,
            buff::CANNOT_ESCAPE,
        ] {
            let mut t = BuffTemplate::simple(id, BuffKind::Good);
            t.duration = 1000;
            data.skills.buffs.insert(id, t);
        }
        for (id, mother) in [(101, NUIA), (NUIA, 0)] {
            data.factions.add(SystemFaction {
                id,
                mother_id: mother,
                ..SystemFaction::default()
            });
        }
        for id in [877, 878, 3313, 3314, 398, 1000] {
            data.systems.npcs.doodad.templates.insert(
                id,
                DoodadTemplate {
                    id,
                    ..DoodadTemplate::default()
                },
            );
        }
        data
    }

    /// A server with one Nuian court room (id 1) loaded.
    fn server() -> Arc<GameServer> {
        let server = harness::server_with(data());
        let pos = |x: f32| SpawnPosition {
            world_id: 0,
            zone_id: 100,
            x,
            y: 2.0,
            z: 3.0,
            yaw: 90.0,
        };
        server.config.update(|c| {
            c.justice = JusticeConfig {
                court_rooms: vec![CourtRoomConfig {
                    id: 1,
                    name: "Nuian Main Court".into(),
                    faction: NUIA,
                    defendant: pos(10.0),
                    holding_cell: pos(1234.5),
                }],
                ..JusticeConfig::default()
            }
        });
        server.systems.social.trials.load(&server);
        server
    }

    fn body(f: impl FnOnce(&mut PacketStream)) -> Vec<u8> {
        let mut s = PacketStream::new();
        f(&mut s);
        s.into_bytes()
    }

    fn of(frames: &[(u16, Vec<u8>)], op: u16) -> Vec<Vec<u8>> {
        frames
            .iter()
            .filter(|(o, _)| *o == op)
            .map(|(_, b)| b.clone())
            .collect()
    }

    #[tokio::test]
    async fn crime_root_help_and_points() {
        let server = server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        let (_, out) = harness::run(&server, &gm.p, "crime").await;
        assert_eq!(out.len(), 3);
        assert_eq!(
            out[0],
            "|cFF7CFC00[Crime] Root command to crime related tasks|r"
        );
        assert_eq!(
            out[1],
            "|cFF7CFC00[Crime] Supported subcommands: <create||points||jury_invite||ji||ask_guilty||ag||court||trial_state||ts||fake>|r"
        );

        // Change mode on another player.
        let (_, out) =
            harness::run(&server, &gm.p, "crime points bob crime=10 infamy=-3 jury=2").await;
        assert_eq!(
            out,
            [
                "[Crime Points] Adjusted Bob's justice points; crime +10, infamy -3, jury points +2",
                "Target: Bob - [Crime Points] [GM] Gm has changed your justice points; crime +10, infamy -3, jury points +2",
            ]
        );
        {
            let c = bob.p.data();
            assert_eq!((c.crime_point, c.infamy_point, c.jury_point), (10, 0, 2));
        }
        let frames = bob.frames();
        assert_eq!(
            of(&frames, sc::SCCrimeChangedPacket),
            vec![body(|s| {
                s.write_i32(10).write_i16(10).write_i32(0).write_i16(0);
            })]
        );
        assert_eq!(
            of(&frames, sc::SCJuryPointChangedPacket),
            vec![body(|s| {
                s.write_i32(2);
            })]
        );
        assert_eq!(
            harness::chat_text(&of(&frames, sc::SCChatMessagePacket)[0]),
            "[Crime Points] [GM] Gm has changed your justice points; crime +10, infamy -3, jury points +2"
        );

        // Stats mode.
        let (_, out) = harness::run(&server, &gm.p, "crime points Bob").await;
        assert_eq!(
            out,
            [
                "|cFFFFFFFF[Crime Points] [Bob] justice stats;|r",
                "[Crime Points] [Faction] 101 (MotherId: 148)",
                "[Crime Points] [Crime] 10",
                "[Crime Points] [Infamy] 0",
                "[Crime Points] [Jury] 2",
            ]
        );
        // Target lookups.
        let (_, out) = harness::run(&server, &gm.p, "crime points target").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Points] Please select a valid character player|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "crime points nobody").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Points] Character player: nobody was not found.|r"]
        );
        gm.p.data().unit.current_target = bob.p.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "crime points target crime=1").await;
        assert_eq!(out.len(), 2);
        // `self` gets no second message.
        let (_, out) = harness::run(&server, &gm.p, "crime points self jury=1").await;
        assert_eq!(
            out,
            ["[Crime Points] Adjusted Gm's justice points; crime +0, infamy +0, jury points +1"]
        );
        let (_, out) = harness::run(&server, &gm.p, "crime points self crime=x").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Points] Invalid numeric value for parameter: crime point amount|r"]
        );
    }

    #[tokio::test]
    async fn crime_ask_guilty_jury_invite_and_trial_state() {
        let server = server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);

        let (_, out) = harness::run(&server, &gm.p, "crime ag bob crime=60 time=15").await;
        assert_eq!(
            out,
            ["|cFFFFFFFF[Crime Ask Guilty] Asking Bob if they plead guilty for 15 minutes (60 crime points).|r"]
        );
        assert_eq!(
            bob.packets(),
            vec![(
                sc::SCAskImprisonOrTrialPacket,
                vec![60, 0, 0, 0, 15, 0, 0, 0]
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "crime ask_guilty bob crime=60").await;
        assert_eq!(
            out,
            // C#: a missing required prefix parameter is not validated (KeyNotFoundException).
            ["|cFFFF0000[Crime Ask Guilty] Unexpected error: The given key 'time' was not present in the dictionary.|r"]
        );

        let (_, out) = harness::run(&server, &gm.p, "crime ji bob").await;
        assert_eq!(
            out,
            ["|cFFFFFFFF[Crime Jury Invite] Summoning Bob to trial 0|r"]
        );
        assert_eq!(
            bob.packets(),
            vec![(
                sc::SCInviteJuryPacket,
                body(|s| {
                    s.write_str("WantedCriminal").write_u32(0);
                })
            )]
        );
        harness::run(
            &server,
            &gm.p,
            "crime jury_invite bob defendant=Crook trial=7",
        )
        .await;
        assert_eq!(
            bob.packets(),
            vec![(
                sc::SCInviteJuryPacket,
                body(|s| {
                    s.write_str("Crook").write_u32(7);
                })
            )]
        );

        let (_, out) =
            harness::run(&server, &gm.p, "crime ts bob trial=3 state=5 jury=4 time=2").await;
        assert_eq!(
            out,
            ["|cFFFFFFFF[Crime Set Trial State] Setting Trial 3 to 5 (JuryVerdict) with Jury 4 and Time 2 minutes.|r"]
        );
        assert_eq!(
            bob.packets(),
            vec![(
                sc::SCChangeTrialStatePacket,
                body(|s| {
                    s.write_u32(3).write_u8(5).write_i32(4).write_u32(120_000);
                })
            )]
        );
        let (_, out) = harness::run(
            &server,
            &gm.p,
            "crime trial_state bob trial=3 state=42 jury=4 time=2",
        )
        .await;
        assert!(out[0].contains("to 42 (42) with"));
    }

    #[tokio::test]
    async fn crime_court_list_and_jail() {
        let server = server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        let (_, out) = harness::run(&server, &gm.p, "crime court bob list court=5").await;
        assert_eq!(
            out,
            [
                "|cFFFFFFFF[Crime Court] [Crime Court list] Action for Bob|r",
                "|cFFFF0000[Crime Court] Invalid courtroom id: 5.|r",
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, "crime court bob List court=1").await;
        assert_eq!(
            out,
            [
                "|cFFFFFFFF[Crime Court] [Crime Court list] Action for Bob|r",
                "[Crime Court] Name: Nuian Main Court (1)",
                "[Crime Court] Judge: none from  (spawnerId: )",
                "[Crime Court] Defendant location: X:10 Y:2 Z:3  r:0° p:0° y:90°",
                "[Crime Court] Jail location: X:1,234.5 Y:2 Z:3  r:0° p:0° y:90°",
                "[Crime Court] No ongoing trial",
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, "crime court bob other").await;
        assert_eq!(
            out,
            ["|cFFFFFFFF[Crime Court] [Crime Court other] Action for Bob|r"]
        );
        bob.frames();
        let (_, out) = harness::run(&server, &gm.p, "crime court bob jail court=1").await;
        assert_eq!(
            out,
            [
                "|cFFFFFFFF[Crime Court] [Crime Court jail] Action for Bob|r",
                "[Crime Court] Moving Bob to jail of Nuian Main Court (1)",
            ]
        );
        {
            let c = bob.p.data();
            assert!(c.disabled_set_position);
            assert!(c.unit.buffs.check_buff(buff::FORCIBLY_AWAITING_TRIAL));
        }
        let frames = bob.packets();
        assert_eq!(
            of(&frames, sc::SCTeleportUnitPacket),
            vec![body(|s| {
                s.write_u8(0)
                    .write_i16(0)
                    .write_position(1234.5, 2.0, 3.0)
                    .write_f32(90f32.to_radians());
            })]
        );
    }

    #[tokio::test]
    async fn crime_create_spawns_evidence() {
        let server = server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "crime create 877 owner=2 victim=3").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Create] Unexpected error: The given key 'source' was not present in the dictionary.|r"]
        );
        let (_, out) =
            harness::run(&server, &gm.p, "crime create 5 owner=2 victim=3 source=9").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Create] Doodad templateId:5 don't exist|r"]
        );
        let (_, out) = harness::run(
            &server,
            &gm.p,
            "crime create 1000 owner=2 victim=3 source=9",
        )
        .await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Create] Doodad templateId:1000 is not related to evidence.|r"]
        );
        gm.chat();
        let (_, out) =
            harness::run(&server, &gm.p, "crime create 878 owner=2 victim=3 source=9").await;
        assert!(out.is_empty());
        let world = server.world.world(0).unwrap();
        let d = world
            .all_doodads()
            .into_iter()
            .find(|d| d.data().template_id == 878)
            .expect("evidence spawned");
        assert_eq!(
            gm.chat(),
            [format!(
                "Crime Evidence Doodad ObjId:{}, Template 878 spawned, Owner: 2, Victim: 3, Source: 9",
                d.obj_id
            )]
        );
        {
            let dd = d.data();
            assert_eq!(
                (dd.owner_id, dd.owner_type, dd.data, dd.item_template_id),
                (2, crate::models::doodad::doodad_owner_type::CHARACTER, 3, 9)
            );
            // 3 m in front of the player (yaw 0: +x), the angle back to the player in degrees.
            assert_eq!((dd.transform.x, dd.transform.y), (103.0, 100.0));
            assert_eq!(dd.transform.rot_z, 180.0);
        }
        let help = harness::run(&server, &gm.p, "crime create help").await.1;
        assert_eq!(
            help[0],
            "|cFF7CFC00[Crime Create] Add a new crime evidence doodad of a specific template at your current location.\nValid templates: Small Bloodstain (877), Large Bloodstain (878), Footprint (male 3313, female 3314)|r"
        );
        assert_eq!(
            help[1],
            "|cFF7CFC00[Crime Create] /crime create <template id> <owner=<playerId>> <victim=<playerId>> [source=<doodadTemplateId>]|r"
        );
    }

    #[tokio::test]
    async fn crime_fake_trial_reports_evidence_and_arrests() {
        let server = server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let _crook = harness::pc(&server, 2, "Crook", 105.0, 100.0);
        let _victim = harness::pc(&server, 3, "Victim", 110.0, 100.0);
        for (id, name) in [(1, "Gm"), (2, "Crook"), (3, "Victim")] {
            server.names.add_character(id, name, 1000 + id);
        }
        let (_, out) = harness::run(&server, &gm.p, "crime fake crook crook evidence=1").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Fake Trial] The victim can not be the same as the perpetrator.|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "crime fake crook target evidence=1").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Fake Trial] Please select a valid character victim|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "crime fake crook nobody evidence=1").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Crime Fake Trial] Character victim: nobody was not found.|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "crime fake crook victim evidence=3").await;
        assert_eq!(
            out,
            ["|cFFFFFFFF[Crime Fake Trial] Putting Crook on trial by using 3 new evidence(s) to frame them with Victim as the victim.|r"]
        );
        // Three reports against the crook, then the arrest (a trial in the Nuian court).
        let events = server.systems.social.crimes.crimes_of_player(2, true);
        assert_eq!(events.len(), 3);
        assert!(events.iter().all(|e| e.reporter == 3));
        let mut msgs: Vec<String> = events.iter().map(|e| e.msg.clone()).collect();
        msgs.sort();
        assert_eq!(msgs, ["Report #1", "Report #2", "Report #3"]);
        assert_eq!(server.systems.social.trials.trial_ids().len(), 1);
        // The fake pines are gone again.
        let world = server.world.world(0).unwrap();
        assert!(!world
            .all_doodads()
            .iter()
            .any(|d| d.data().template_id == 398));
    }
}
