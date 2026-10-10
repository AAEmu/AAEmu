//! The sub commands of `Utils/Scripts/SubCommands/{World, Time, FishFinder, Feature, AStar}`
//! (registered by the `Scripts/Commands` `WorldCmd`, `TimeCmd`, `FishFinderCmd`, `FeatureCmd`
//! and `AStarCmd` of the later scripts phase).

use std::sync::{Arc, RwLock};

use super::output::{Color, MessageOutput};
use super::params::{
    NumericSubCommandParameter, Parameters, StringSubCommandParameter, SubCommandParameter,
};
use super::subcommand::{optional_parameter, SubCommand, SubCommandBase};
use super::{CommandContext, COMMAND_PREFIX};
use crate::server::GameServer;
use crate::systems::services::webapi::json::dotnet_single_string;
use crate::systems::HookSlot;
use crate::world::{NpcRef, PlayerRef};

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

fn float_param(name: &str) -> Box<dyn SubCommandParameter> {
    Box::new(NumericSubCommandParameter::<f32>::new(
        name,
        Some(name),
        true,
    ))
}

fn string_param(name: &str) -> Box<dyn SubCommandParameter> {
    Box::new(StringSubCommandParameter::new(name, Some(name), true, &[]))
}

/// `Unit.Set*Rate(value)` etc.: the `AppConfiguration.Instance.World` members the World set
/// commands change (and the subsystem copies of them).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WorldSetting {
    GrowthRate,
    LootRate,
    VocationRate,
    HonorRate,
    ExpRate,
    AutoSaveInterval,
}

/// Applies a world rate to the running configuration (and the copies the subsystems keep).
pub fn apply_world_setting(server: &GameServer, setting: WorldSetting, value: f32) {
    let v = f64::from(value);
    server.config.update(|c| match setting {
        WorldSetting::GrowthRate => c.world.growth_rate = v,
        WorldSetting::LootRate => c.world.loot_rate = v,
        WorldSetting::VocationRate => c.world.vocation_rate = v,
        WorldSetting::HonorRate => c.world.honor_rate = v,
        WorldSetting::ExpRate => c.world.exp_rate = v,
        WorldSetting::AutoSaveInterval => c.world.auto_save_interval = v,
    });
    match setting {
        WorldSetting::GrowthRate => server.systems.npcs.doodads.set_growth_rate(v),
        WorldSetting::VocationRate | WorldSetting::HonorRate => {
            let mut s = server.systems.skills.config();
            s.vocation_rate = server.config.world.vocation_rate;
            s.honor_rate = server.config.world.honor_rate;
            server.systems.skills.set_config(s);
        }
        // `SaveManager.SetAutoSaveInterval()` only stores the delay: the running save task
        // keeps its interval (as in C#).
        _ => {}
    }
}

/// One of the numeric `WorldSet*rateSubCommand`s.
struct WorldSetRate {
    base: SubCommandBase,
    param: &'static str,
    label: &'static str,
    /// The (C#) text of the success message (`LootRate` reports "Set GrowthRate").
    done: &'static str,
    max: f32,
    unit: &'static str,
    setting: WorldSetting,
}

impl SubCommand for WorldSetRate {
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
        let value = parameters.require(self.param)?.as_f32()?;
        let shown = dotnet_single_string(value);
        if !(1.0..=self.max).contains(&value) {
            self.base.send_color_message(
                out,
                Color::CORAL,
                &format!(
                    "{} = {shown} must be at least 1.0 and no more than {}{}",
                    self.label,
                    format_max(self.max),
                    self.unit
                ),
            );
            return Ok(());
        }
        apply_world_setting(ctx.server()?, self.setting, value);
        self.base
            .send_message(out, &format!("{} {shown}", self.done));
        tracing::warn!("{}: {shown}", self.base.title);
        Ok(())
    }
}

fn format_max(max: f32) -> String {
    format!("{max:.1}")
}

/// `WorldSetLogoutmessageSubCommand` / `WorldSetMotdmessageSubCommand`.
struct WorldSetMessage {
    base: SubCommandBase,
    param: &'static str,
    empty_error: &'static str,
    done: &'static str,
    motd: bool,
}

impl SubCommand for WorldSetMessage {
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
        let text = parameters.require(self.param)?.as_str()?.to_string();
        if text.is_empty() {
            self.base
                .send_color_message(out, Color::CORAL, self.empty_error);
            return Ok(());
        }
        let server = ctx.server()?;
        server.config.update(|c| {
            if self.motd {
                c.world.motd = text.clone();
            } else {
                c.world.logout_message = text.clone();
            }
        });
        self.base
            .send_message(out, &format!("{} {text}", self.done));
        tracing::warn!("{}: {text}", self.base.title);
        Ok(())
    }
}

/// `WorldSetGeodatamodeSubCommand`.
struct WorldSetGeodatamode {
    base: SubCommandBase,
}

impl SubCommand for WorldSetGeodatamode {
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
        let mode = parameters.require("GeoDataMode")?.as_str()?.to_string();
        if mode.is_empty() {
            self.base.send_color_message(
                out,
                Color::CORAL,
                "GeoDataMode must be an 'true' or 'false'",
            );
            return Ok(());
        }
        let enabled = mode == "true";
        ctx.server()?
            .config
            .update(|c| c.world.geo_data_mode = enabled);
        self.base
            .send_message(out, &format!("Set GeoDataMode: {mode}"));
        tracing::warn!("{}: {mode}", self.base.title);
        Ok(())
    }
}

/// `WorldSetSubCommand`: the World set operations.
pub struct WorldSetSubCommand {
    base: SubCommandBase,
}

impl Default for WorldSetSubCommand {
    fn default() -> Self {
        let rate = |title: &str,
                    desc: &str,
                    prefix: &str,
                    param: &'static str,
                    label: &'static str,
                    done: &'static str,
                    max: f32,
                    unit: &'static str,
                    setting| {
            Arc::new(WorldSetRate {
                base: base_with(title, desc, prefix, vec![float_param(param)]),
                param,
                label,
                done,
                max,
                unit,
                setting,
            }) as Arc<dyn SubCommand>
        };
        let mut base = SubCommandBase::new(
            "[World Set]",
            "Possible operations with world configuration",
            &format!("{COMMAND_PREFIX}set"),
        );
        base.register(
            rate(
                "[World Set GrowthRate]",
                "Setting the growth rate",
                "growthrate",
                "GrowthRate",
                "Growth rate",
                "Set GrowthRate",
                1000.0,
                "",
                WorldSetting::GrowthRate,
            ),
            &["growthrate", "growth_rate", "gr"],
        );
        // C#: the loot rate command reports "Set GrowthRate".
        base.register(
            rate(
                "[World Set LootRate]",
                "Setting the loot rate",
                "lootrate",
                "LootRate",
                "Loot rate",
                "Set GrowthRate",
                1000.0,
                "",
                WorldSetting::LootRate,
            ),
            &["lootrate", "loot_rate", "lr"],
        );
        base.register(
            rate(
                "[World Set VocationRate]",
                "Setting the vocation rate",
                "vocationrate",
                "VocationRate",
                "Vocation rate",
                "Set VocationRate",
                1000.0,
                "",
                WorldSetting::VocationRate,
            ),
            &["vocationrate", "vocation_rate", "vr"],
        );
        base.register(
            rate(
                "[World Set HonorRate]",
                "Setting the honor rate",
                "honorrate",
                "HonorRate",
                "Honor rate",
                "Set HonorRate",
                1000.0,
                "",
                WorldSetting::HonorRate,
            ),
            &["honorrate", "honor_rate", "hr"],
        );
        base.register(
            rate(
                "[World Set ExpRate]",
                "Setting the exp rate",
                "exprate",
                "ExpRate",
                "Exp Rate",
                "Set ExpRate",
                1000.0,
                "",
                WorldSetting::ExpRate,
            ),
            &["exprate", "exp_rate", "er"],
        );
        base.register(
            rate(
                "[World Set AutoSaveInterval]",
                "Setting the Auto Save Interval",
                "autosaveinterval",
                "AutoSaveInterval",
                "AutoSaveInterval",
                "Set AutoSaveInterval",
                1440.0,
                " minutes",
                WorldSetting::AutoSaveInterval,
            ),
            &["autosaveinterval", "auto_save_interval", "asi"],
        );
        base.register(
            Arc::new(WorldSetMessage {
                base: base_with(
                    "[World Set LogoutMessage]",
                    "Setting the logout message",
                    "logoutmessage",
                    vec![string_param("LogoutMessage")],
                ),
                param: "LogoutMessage",
                empty_error: "Logout message must not be an empty string",
                done: "Set LogoutMessage",
                motd: false,
            }),
            &["logoutmessage", "logout_message", "lm"],
        );
        base.register(
            Arc::new(WorldSetGeodatamode {
                base: base_with(
                    "[World Set GeoDataMode]",
                    "Setting the GeoDataMode",
                    "geodatamode",
                    vec![string_param("GeoDataMode")],
                ),
            }),
            &[
                "geodatamode",
                "geo_data_mode",
                "gdm",
                "geodata",
                "geo_data",
                "gd",
            ],
        );
        base.register(
            Arc::new(WorldSetMessage {
                base: base_with(
                    "[World Set MOTD]",
                    "Setting the MOTD",
                    "motd",
                    vec![string_param("MOTD")],
                ),
                param: "MOTD",
                empty_error: "MOTD message must not be an empty string",
                done: "Set MOTD",
                motd: true,
            }),
            &["motd"],
        );
        Self { base }
    }
}

impl SubCommand for WorldSetSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }
}

/// `TimeSetSubCommand`.
pub struct TimeSetSubCommand {
    base: SubCommandBase,
}

impl Default for TimeSetSubCommand {
    fn default() -> Self {
        let mut base = SubCommandBase::new(
            "[Time Information]",
            "Find out what time it is",
            &format!("{COMMAND_PREFIX}time set"),
        );
        base.add_parameter(Box::new(NumericSubCommandParameter::<i32>::new(
            "hour",
            Some("hour"),
            true,
        )))
        .expect("valid");
        base.add_parameter(Box::new(NumericSubCommandParameter::<i32>::new(
            "minute",
            Some("minute"),
            false,
        )))
        .expect("valid");
        Self { base }
    }
}

impl SubCommand for TimeSetSubCommand {
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
        let old_time = server.time.get_time();
        let mut hour = old_time as i32;
        if let Some(h) = parameters.get("hour") {
            hour = h.as_i32()?;
        }
        let mut minute = ((old_time - old_time.trunc()) * 60.0).floor() as i32;
        if let Some(m) = parameters.get("minute") {
            minute = m.as_i32()?;
        }
        let new_time = hour as f32 + minute as f32 / 60.0;
        server.time.set(new_time);
        self.base.send_message(
            out,
            &format!("Changed game time {old_time:.2} -> {new_time:.2} ({hour}h{minute}m)"),
        );
        Ok(())
    }
}

/// `FishFinderSetSubCommand`.
pub struct FishFinderSetSubCommand {
    base: SubCommandBase,
}

impl Default for FishFinderSetSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[FishFinder setup]",
                "FishFinder setup",
                "set",
                vec![string_param("start")],
            ),
        }
    }
}

impl SubCommand for FishFinderSetSubCommand {
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
        let start = parameters.require("start")?.as_str()?;
        let player = ctx.player()?;
        let radar = &ctx.server()?.systems.services.radar;
        if start == "true" {
            radar.register_for_fish_school(&player, 1000.0);
            self.base.send_message(out, "FishFinder set start: true");
        } else {
            radar.register_for_fish_school(&player, 0.0);
            self.base.send_message(out, "FishFinder set start: false");
        }
        Ok(())
    }
}

/// Every `Feature` enum member (`Enum.GetValues<Feature>()` order).
pub const FEATURE_NAMES: &[(u8, &str)] = &[
    (0, "siege"),
    (1, "allowFamilyChanges"),
    (2, "flag_0_2"),
    (3, "houseSale"),
    (4, "premium"),
    (5, "flag_0_5"),
    (6, "nations"),
    (7, "flag_0_7"),
    (16, "flag_2_0"),
    (17, "flag_2_1"),
    (18, "flag_2_2"),
    (19, "flag_2_3"),
    (20, "flag_2_4"),
    (21, "flag_2_5"),
    (22, "flag_2_6"),
    (23, "flag_2_7"),
    (24, "flag_3_0"),
    (25, "flag_3_1"),
    (26, "flag_3_2"),
    (27, "flag_3_3"),
    (28, "flag_3_4"),
    (29, "flag_3_5"),
    (30, "flag_3_6"),
    (31, "flag_3_7"),
    (32, "flag_4_0"),
    (33, "flag_4_1"),
    (34, "flag_4_2"),
    (35, "flag_4_3"),
    (36, "ranking"),
    (37, "flag_4_5"),
    (38, "ingamecashshop"),
    (39, "customUiButton"),
    (40, "customsaveload"),
    (41, "flag_5_1"),
    (42, "flag_5_2"),
    (43, "bm_mileage"),
    (44, "aaPoint"),
    (45, "itemSecure"),
    (46, "secondpass"),
    (47, "flag_5_7"),
    (48, "slave_customize"),
    (49, "flag_6_1"),
    (50, "flag_6_2"),
    (51, "flag_6_3"),
    (52, "beautyshopBypass"),
    (53, "freeLpRaise"),
    (54, "flag_6_6"),
    (55, "ingameshopSecondpass"),
    (56, "backpackProfitShare"),
    (57, "flag_7_1"),
    (58, "sensitiveOperation"),
    (59, "taxItem"),
    (60, "flag_7_4"),
    (61, "flag_7_5"),
    (62, "flag_7_6"),
    (63, "achievement"),
    (72, "flag_9_0"),
    (73, "flag_9_1"),
    (74, "dwarfWarborn"),
    (75, "mailCoolTime"),
    (76, "flag_9_4"),
    (77, "flag_9_5"),
    (78, "flag_9_6"),
    (79, "flag_9_7"),
    (80, "flag_10_0_DO_NOT_USE"),
    (81, "hudAuctionButton"),
    (82, "flag_10_2"),
    (83, "auctionPostBuff"),
    (84, "houseTaxPrepay"),
    (85, "flag_10_5"),
    (86, "flag_10_6"),
    (87, "flag_10_7"),
];

/// `FeatureCheckSubCommand`.
pub struct FeatureCheckSubCommand {
    base: SubCommandBase,
}

impl Default for FeatureCheckSubCommand {
    fn default() -> Self {
        Self {
            base: SubCommandBase::new(
                "[Feature]",
                "Checking the characteristic features of the account using a bitwise installation",
                &format!("{COMMAND_PREFIX}feature check"),
            ),
        }
    }
}

impl SubCommand for FeatureCheckSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute_args(
        &self,
        ctx: &CommandContext<'_>,
        trigger: &str,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        self.execute(ctx, trigger, &Parameters::default(), out)
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        _parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let fsets = ctx.server()?.systems.services.features(ctx.server()?);
        for (bit, name) in FEATURE_NAMES {
            if fsets.check_bit(*bit) {
                out.send_message(&format!("[Feature] |cFF00FF00ON  |cFF80FF80{name}|r"));
            } else {
                out.send_message(&format!("[Feature] |cFFFF0000OFF |cFF802020{name}|r"));
            }
        }
        Ok(())
    }
}

/// `FeatureSetSubCommand`.
pub struct FeatureSetSubCommand {
    base: SubCommandBase,
}

impl Default for FeatureSetSubCommand {
    fn default() -> Self {
        let mut base = SubCommandBase::new(
            "[Feature]",
            "Change the characteristic features of the account using a bitwise installation",
            &format!("{COMMAND_PREFIX}feature set"),
        );
        base.add_parameter(Box::new(
            NumericSubCommandParameter::<i32>::with_range(
                "feature",
                Some("feature id"),
                true,
                None,
                0,
                87,
            )
            .expect("valid range"),
        ))
        .expect("valid");
        base.add_parameter(string_param("enable")).expect("valid");
        Self { base }
    }
}

impl SubCommand for FeatureSetSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute_args(
        &self,
        ctx: &CommandContext<'_>,
        trigger: &str,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        self.execute(ctx, trigger, &Parameters::default(), out)
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let feature = parameters.require("feature")?.as_i32()?;
        if feature == -1 {
            self.base
                .send_color_message(out, Color::RED, "Error Feature set!");
            return Ok(());
        }
        let enable = parameters.require("enable")?.as_str()? == "true";
        let server = ctx.server()?;
        if server
            .systems
            .services
            .set_feature(server, feature as u8, enable)
        {
            self.base.send_message(
                out,
                &format!(
                    "Feature set {feature}, {}. Need reload character",
                    if enable { "True" } else { "False" }
                ),
            );
        } else {
            self.base
                .send_color_message(out, Color::RED, "Error Feature set!");
        }
        Ok(())
    }
}

/// The NPC path finding the AStar sub commands drive (`Npc.Ai.PathNode`, `GeoData.ReducePath`,
/// `DoodadSpawner.Spawn`); installed by the AI owner.
pub trait AStarHooks: Send + Sync {
    /// `PathNode.StartPointPos` / `EndPointPos` (`None` = `Vector3.Zero`).
    fn set_start(&self, server: &GameServer, npc: &NpcRef, pos: [f32; 3]);
    fn set_end(&self, server: &GameServer, npc: &NpcRef, pos: [f32; 3]);
    fn start_end(&self, server: &GameServer, npc: &NpcRef) -> ([f32; 3], [f32; 3]);
    /// `ZoneKey = zone; FindPath(...)` minus the first node when not on it, then
    /// `FoundPath = ReducePath(path, 10)`; returns the full path.
    fn find_path(&self, server: &GameServer, npc: &NpcRef, zone_key: u32) -> Vec<[f32; 3]>;
    /// `PathNode.FoundPath`.
    fn found_path(&self, server: &GameServer, npc: &NpcRef) -> Vec<[f32; 3]>;
    /// `DoodadSpawner { UnitId = templateId }.Spawn(0, 0, ownerObjId)` at `pos`.
    fn spawn_marker(&self, server: &GameServer, owner: &PlayerRef, template_id: u32, pos: [f32; 3]);
}

/// The installed [`AStarHooks`] (`None` until the AI subsystem provides path finding).
pub static ASTAR_HOOKS: std::sync::LazyLock<HookSlot<dyn AStarHooks>> =
    std::sync::LazyLock::new(HookSlot::default);

fn astar(ctx: &CommandContext<'_>) -> anyhow::Result<Arc<dyn AStarHooks>> {
    let _ = ctx;
    ASTAR_HOOKS
        .get()
        .ok_or_else(|| anyhow::anyhow!("Npc.Ai.PathNode is not available (AI not ported)"))
}

/// The targeted NPC (`CurrentTarget as Npc`).
fn targeted_npc(ctx: &CommandContext<'_>) -> anyhow::Result<Option<NpcRef>> {
    let player = ctx.player()?;
    let target = player.data().unit.current_target;
    Ok(ctx
        .server()?
        .world
        .object(target)
        .and_then(|o| o.as_npc().cloned()))
}

/// `AStarStartPositionSubCommand` / `AStarEndPositionSubCommand`.
pub struct AStarPointSubCommand {
    base: SubCommandBase,
    end: bool,
}

impl AStarPointSubCommand {
    fn new(end: bool) -> Self {
        let (title, desc, prefix) = if end {
            (
                "[AStar Goal Position]",
                "Let's assign an endpoint to the path.",
                "goal||end",
            )
        } else {
            (
                "[AStar Start Position]",
                "Let's set the starting point of the path.",
                "start||begin",
            )
        };
        let mut base = SubCommandBase::new(title, desc, &format!("{COMMAND_PREFIX}{prefix}"));
        for axis in ["x", "y", "z"] {
            base.add_parameter(Box::new(NumericSubCommandParameter::<f32>::with_prefix(
                axis,
                Some(&format!("{axis}=<new {axis}>")),
                false,
                Some(axis),
            )))
            .expect("valid");
        }
        Self { base, end }
    }

    pub fn start() -> Self {
        Self::new(false)
    }

    pub fn goal() -> Self {
        Self::new(true)
    }
}

impl SubCommand for AStarPointSubCommand {
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
        // No parameter named "ObjId" is ever declared: the C# always uses the target.
        let Some(npc) = targeted_npc(ctx)? else {
            self.base.send_color_message(
                out,
                Color::CORAL,
                "AStar: You need to target a Npc first",
            );
            return Ok(());
        };
        let player = ctx.player()?;
        let (px, py, pz) = {
            let c = player.data();
            let world = server.world.world_of(&player.clone().into());
            match world {
                Some(w) => {
                    let p = w.world_pos_rot(&player.clone().into());
                    (p.x, p.y, p.z)
                }
                None => (c.unit.transform.x, c.unit.transform.y, c.unit.transform.z),
            }
        };
        let x = optional_parameter(parameters, "x", px, |v| v.as_f32())?;
        let y = optional_parameter(parameters, "y", py, |v| v.as_f32())?;
        let z = optional_parameter(parameters, "z", pz, |v| v.as_f32())?;
        let hooks = astar(ctx)?;
        let f = dotnet_single_string;
        if self.end {
            hooks.set_end(server, &npc, [x, y, z]);
            out.send_message(&format!(
                "AStar: the endpoint is set X:{}, Y:{}, Z:{}",
                f(x),
                f(y),
                f(z)
            ));
        } else {
            hooks.set_start(server, &npc, [x, y, z]);
            out.send_message(&format!(
                "AStar: the starting point is set X:{}, Y:{}, Z:{}",
                f(x),
                f(y),
                f(z)
            ));
        }
        Ok(())
    }
}

/// `AStarPathFindingSubCommand`.
pub struct AStarPathFindingSubCommand {
    base: SubCommandBase,
}

impl Default for AStarPathFindingSubCommand {
    fn default() -> Self {
        let mut base = SubCommandBase::new(
            "[AStar Path Finding]",
            "Start looking for a path. Returns a list of points.",
            &format!("{COMMAND_PREFIX}go||find"),
        );
        base.add_parameter(Box::new(StringSubCommandParameter::new(
            "templateId",
            Some("templateId"),
            false,
            &[],
        )))
        .expect("valid");
        Self { base }
    }
}

impl SubCommand for AStarPathFindingSubCommand {
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
        // `parameters["templateId"]`: required by the C# body although declared optional.
        let _template = parameters.require("templateId")?.as_str()?;
        let server = ctx.server()?;
        let Some(npc) = targeted_npc(ctx)? else {
            self.base.send_color_message(
                out,
                Color::CORAL,
                "AStar: You need to target a Npc first",
            );
            return Ok(());
        };
        let hooks = astar(ctx)?;
        let (start, end) = hooks.start_end(server, &npc);
        if start != [0.0; 3] && end != [0.0; 3] {
            let zone = ctx.player()?.data().unit.transform.zone_id;
            let path = hooks.find_path(server, &npc, zone);
            let character = &ctx.character;
            character.send_message(&format!("AStar: points found Total: {}", path.len()));
            for (i, p) in path.iter().enumerate() {
                character.send_message(&format!(
                    "AStar: point {i} coordinates X:{}, Y:{}, Z:{}",
                    dotnet_single_string(p[0]),
                    dotnet_single_string(p[1]),
                    dotnet_single_string(p[2])
                ));
            }
        } else {
            self.base.send_color_message(
                out,
                Color::CORAL,
                "AStar: to find the path, you need to set the start and end points of the route!",
            );
        }
        Ok(())
    }
}

/// `AStarViewSubCommand`.
pub struct AStarViewSubCommand {
    base: SubCommandBase,
}

impl Default for AStarViewSubCommand {
    fn default() -> Self {
        let mut base = SubCommandBase::new(
            "[AStar View]",
            "Let's map the found waypoints on the terrain.",
            &format!("{COMMAND_PREFIX}view"),
        );
        base.add_parameter(Box::new(NumericSubCommandParameter::<u32>::new(
            "templateId",
            Some("template id"),
            false,
        )))
        .expect("valid");
        Self { base }
    }
}

impl SubCommand for AStarViewSubCommand {
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
        let Some(npc) = targeted_npc(ctx)? else {
            self.base.send_color_message(
                out,
                Color::CORAL,
                "AStar: You need to target a Npc first",
            );
            return Ok(());
        };
        let mut template_id = parameters.require("templateId")?.as_u32()?;
        if template_id == 0 {
            template_id = 5014;
        }
        if !server.game_data.systems.npcs.doodad.exist(template_id) {
            self.base.send_color_message(
                out,
                Color::CORAL,
                &format!("AStar: Doodad templateId:{template_id} don't exist"),
            );
            return Ok(());
        }
        let hooks = astar(ctx)?;
        let player = ctx.player()?;
        for p in hooks.found_path(server, &npc) {
            hooks.spawn_marker(server, &player, template_id, p);
        }
        Ok(())
    }
}

/// The runtime `FeaturesManager.Fsets` (changed by `FeatureSetSubCommand`).
#[derive(Debug, Default)]
pub struct RuntimeFeatures(pub RwLock<Option<crate::data::FeatureSet>>);
