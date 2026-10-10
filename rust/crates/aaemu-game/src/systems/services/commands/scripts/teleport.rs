//! `Scripts/Commands/Teleport.cs`.

use super::ping_position::ping_height;
use super::prelude::*;
use crate::models::character::DEFAULT_INSTANCE_ID;

/// `Teleport.TeleportCommandRegions`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TeleportCommandRegions {
    East = 0,
    West = 1,
    Auroria = 2,
    Dungeons = 3,
    Other = 4,
}

impl TeleportCommandRegions {
    /// `Enum.GetValues<TeleportCommandRegions>()`.
    const ALL: [Self; 5] = [
        Self::East,
        Self::West,
        Self::Auroria,
        Self::Dungeons,
        Self::Other,
    ];

    fn name(self) -> &'static str {
        match self {
            Self::East => "East",
            Self::West => "West",
            Self::Auroria => "Auroria",
            Self::Dungeons => "Dungeons",
            Self::Other => "Other",
        }
    }
}

/// `Teleport.TPloc`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TPloc {
    pub name: &'static str,
    pub info: &'static str,
    pub x: i32,
    pub y: i32,
    pub z: i32,
    /// `AltNames` (`null` = empty).
    pub alt_names: &'static [&'static str],
    pub region: TeleportCommandRegions,
}

/// `Teleport`.
pub struct Teleport {
    /// `Locations` (filled by `OnLoad` → `LoadLocations`).
    locations: Vec<TPloc>,
    /// `AllowPingPos` (enables `/teleport .`).
    allow_ping_pos: bool,
}

impl Teleport {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self {
            locations: load_locations(),
            allow_ping_pos: true,
        }
    }
}

/// `LoadLocations()`.
fn load_locations() -> Vec<TPloc> {
    use TeleportCommandRegions::*;
    let l = |region, name, info, x, y, z, alt_names| TPloc {
        name,
        info,
        x,
        y,
        z,
        alt_names,
        region,
    };
    vec![
        // West
        // locations.Add(new TPloc { Region = TeleReg.West, Name = "aubrecradle", Info = "Aubre Cradle, Ironwrought", X = 7664, Y = 12957, Z = 400 });//228,71
        l(
            West,
            "cinderstone",
            "Cinderstone Moor, Seachild Wharf",
            15400,
            11543,
            107,
            &["cinder"],
        ),
        l(
            West,
            "dewstone",
            "Dewstone Plains, Sandcloud",
            12204,
            13305,
            178,
            &["dew"],
        ),
        l(
            West,
            "gweonid",
            "Gweonid Forest, Memoria",
            10604,
            14925,
            280,
            &["gwen"],
        ),
        l(
            West,
            "halcyona",
            "Halcyona, Suns End",
            9342,
            10321,
            187,
            &["halcy"],
        ),
        l(
            West,
            "hellswamp",
            "Hellswamp, Anarchi",
            7482,
            9853,
            189,
            &["hell"],
        ),
        l(
            West,
            "karkasse",
            "Karkasse Ridgelands, Lavis",
            10470,
            17346,
            186,
            &["kar", "karkase"],
        ),
        l(
            West,
            "lilyut",
            "Lilyut Hills, Windshade",
            12666,
            15520,
            165,
            &["lily", "lili"],
        ),
        l(
            West,
            "marianople",
            "Marianople, Marianople",
            11144,
            12066,
            145,
            &["maria", "west"],
        ),
        l(
            West,
            "sanddeep",
            "Sanddeep, Golden Fable Harbor",
            10720,
            9591,
            105,
            &["sand"],
        ),
        l(
            West,
            "solzreed",
            "Solzreed Peninsula, Cresent Throne",
            15369,
            13864,
            159,
            &["solz"],
        ),
        l(
            West,
            "twocrowns",
            "Two Crowns, Ezna",
            13500,
            10531,
            223,
            &["2c", "twoc"],
        ),
        l(
            West,
            "whitearden",
            "White Arden, Birchkeep",
            9682,
            12775,
            161,
            &["arden"],
        ),
        // East
        l(
            East,
            "arcumiris",
            "Arcum Iris, Parchsun Settlement",
            20509,
            7173,
            193,
            &["arcum"],
        ),
        l(
            East,
            "falcorth",
            "Falcorth Plains, Oxion Clan",
            23818,
            9102,
            589,
            &[],
        ),
        l(East, "hasla", "Hasla, Veroe", 30029, 8760, 539, &[]),
        l(
            East,
            "mahadevi",
            "Mahadevi, City of Towers",
            19284,
            8620,
            227,
            &["maha"],
        ),
        l(
            East,
            "perinoor",
            "Perinor Ruins, Stonehew",
            27787,
            6732,
            572,
            &["peri", "perinor"],
        ),
        l(
            East,
            "rookborne",
            "Rookborne Basin, Watermist",
            25388,
            9753,
            714,
            &["rook", "rookborn"],
        ),
        l(
            East,
            "silentforest",
            "Silent Forest, Count Sebastians Retreat",
            23306,
            12526,
            271,
            &["sf", "silent"],
        ),
        l(
            East,
            "solis",
            "Solis Headlands, Austera",
            16743,
            9018,
            120,
            &["austera", "east"],
        ),
        l(
            East,
            "tigerspine",
            "Tigerspine Mountains, Anvilton",
            21772,
            8089,
            404,
            &["tiger"],
        ),
        l(
            East,
            "villanelle",
            "Villanelle, Lutesong Harbor",
            21178,
            10604,
            119,
            &["villa"],
        ),
        l(
            East,
            "windscour",
            "Windscour Savanah, Skyfang",
            25926,
            6855,
            362,
            &["windscoure", "ws"],
        ),
        l(
            East,
            "ynyster",
            "Ynystere, Caernord",
            20931,
            13158,
            116,
            &["yny"],
        ),
        // Auroria
        l(Auroria, "calmlands", "Calmlands", 22349, 24941, 189, &[]),
        l(
            Auroria,
            "diamondshores",
            "Diamond Shores",
            19332,
            26883,
            130,
            &["ds", "auroria", "origin"],
        ),
        l(Auroria, "exeloch", "Exeloch", 22349, 24941, 189, &["exe"]),
        l(
            Auroria,
            "goldenruins",
            "Golden Ruins",
            17230,
            27501,
            141,
            &["golden"],
        ),
        l(Auroria, "heedmar", "Heedmar", 20110, 24568, 156, &[]),
        l(Auroria, "marcala", "Marcala", 20140, 24768, 189, &[]),
        l(Auroria, "nuimari", "Nuimari", 21731, 23751, 146, &[]),
        l(Auroria, "reedwind", "Reedwind", 19628, 28339, 295, &[]),
        l(Auroria, "sungold", "Sungold Fields", 22122, 26412, 163, &[]),
        // locations.Add(new TPloc { Region = TeleReg.Origin, Name = "whalesong", Info = "Whalesong Harbor", X = 14436, Y = 26696, Z = 135, altNames = ("whale"} });
        // Others
        // locations.Add(new TPloc { Region = TeleReg.Other, Name = "aegisisland", Info = "Aegis Island", X = 14436, Y = 26696, Z = 134, altNames = ("aegis"} });
        l(
            Other,
            "freedich",
            "Sunspeck Sea, Freedich Island",
            20944,
            18799,
            134,
            &["freeditch", "free"],
        ),
        l(
            Other,
            "growlgate",
            "Stormraw Sound, Growlgate Island",
            15138,
            22983,
            105,
            &["pirate"],
        ),
        l(
            Other,
            "ynys",
            "Arcadian Sea, Ynys Island, Docks",
            16570,
            14885,
            102,
            &[],
        ),
        l(
            Other,
            "nuiajail",
            "Marianople, Guard Barracks",
            10860,
            11950,
            132,
            &["nuianjail", "westjail"],
        ),
        l(
            Other,
            "haranyajail",
            "Solis Headlands, Isle of Penance",
            17000,
            9566,
            135,
            &["haranijail", "eastjail"],
        ),
        l(
            Other,
            "nuian",
            "Solzreed, Nuian Starter Area",
            15578,
            15382,
            128,
            &[],
        ),
        l(
            Other,
            "elf",
            "Gweonid Forest, Elven Starter Area",
            10388,
            15982,
            370,
            &[],
        ),
        l(
            Other,
            "harani",
            "Arcum Iris, Harani Starting Area",
            19304,
            7646,
            215,
            &["harihana"],
        ),
        l(
            Other,
            "firran",
            "Falcorth Plains, Firran Starting Area",
            23881,
            10495,
            592,
            &["ferre"],
        ),
        // Dungeons
        l(
            Dungeons,
            "sharpwind",
            "Dewstone Plains, Sharpwind Mines",
            11375,
            13135,
            146,
            &["sm", "gsm", "mines"],
        ),
        l(
            Dungeons,
            "burnt",
            "Cinderstone, Burnt Castle Armory",
            15683,
            12590,
            170,
            &["bc", "bca", "gbc", "castle", "orchidna"],
        ),
        l(
            Dungeons,
            "cellar",
            "Mahadevi, City of Towers, Palace Cellar",
            19317,
            8512,
            198,
            &["pc", "gpc", "sewer"],
        ),
        l(
            Dungeons,
            "hadir",
            "Ynystere, Hadir Farm",
            20022,
            12777,
            140,
            &["hf", "ghf", "farm"],
        ),
        l(
            Dungeons,
            "cradle",
            "Rookborne Basin, Kroloal Cradle",
            26862,
            9042,
            777,
            &["kc", "gkc", "kroloal", "kroloa", "koala"],
        ),
        l(
            Dungeons,
            "abyss",
            "Hellswamp, Howling Abyss",
            7800,
            10315,
            251,
            &["ha", "gha", "howling"],
        ),
        l(
            Dungeons,
            "serpentis",
            "Exeloch, Serpentis",
            23041,
            25848,
            142,
            &["snake", "snek"],
        ),
        // debug_regions
        l(Other, "debug", "Out of Bounds", 8192, 6144, 130, &[]),
    ]
}

/// `Character.ForceDismount()` (`AttachUnitReason.PrefabChanged`).
fn force_dismount(server: &GameServer, character: &PlayerRef) {
    crate::systems::vehicles::force_dismount(
        server,
        character,
        crate::systems::vehicles::slaves::attach_reason::PREFAB_CHANGED,
    );
}

impl Command for Teleport {
    fn command_names(&self) -> Vec<String> {
        names(&["teleport"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.len() == 1 {
            let server = ctx.server()?;
            let character = ctx.player()?;
            let n = args[0].to_lowercase();

            if self.allow_ping_pos && n == "." {
                let (px, py, _) = character.data().local_ping_position;
                if px == 0.0 && py == 0.0 {
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "|cFFFFFF00Make sure you marked a location on the map WHILE IN A PARTY OR RAID, before using this teleport function.\n\
                             If required, you can use the {COMMAND_PREFIX}soloparty command to make a party of just yourself.|r"
                        ),
                    );
                } else {
                    let mut height = ping_height(server, &character, px, py)?;
                    if height == 0.0 {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "|cFFFF0000Target height was |cFFFFFFFFzero|cFFFF0000. \
                                 You likely tried to teleport out of bounds, or no heightmap data was loaded on the server.\n\
                                 If you still want to move to the target location, you can use |cFFFFFFFF{COMMAND_PREFIX}move|cFFFF0000 to go to the following location|cFF40FF40\n\
                                 X:{} Y:{}|r",
                                fixed(f64::from(px), 1),
                                fixed(f64::from(py), 1)
                            ),
                        );
                    } else {
                        height += 2.5; // compensate a bit for terrain irregularities
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Teleporting to |cFFFFFFFFX:{} Y:{} Z:{}|r",
                                f32s(px),
                                f32s(py),
                                f32s(height)
                            ),
                        );
                        force_dismount(server, &character);
                        teleport_player(&character, 0, px, py, height, 0.0);
                    }
                }
            } else if character.data().unit.transform.instance_id != DEFAULT_INSTANCE_ID {
                CommandManager::send_error_text(
                    self,
                    out,
                    "Named teleports are not allowed inside a instance",
                );
            } else {
                let mut found_it = false;
                for item in &self.locations {
                    if item.name == n || item.alt_names.iter().any(|alt| *alt == n) {
                        found_it = true;
                    }

                    if found_it {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!("Teleporting to |cFFFFFFFF{}|r", item.info),
                        );
                        force_dismount(server, &character);
                        teleport_player(
                            &character,
                            0,
                            item.x as f32,
                            item.y as f32,
                            item.z as f32,
                            0.0,
                        );
                        break;
                    }
                }

                if !found_it {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Unavailable Location [{}]", args[0]),
                    );
                }
            }
        } else {
            if self.allow_ping_pos {
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "Usage : {COMMAND_PREFIX}{} <Location>\n\
                         Use a period (.) to teleport to YOUR marked location on the map, \
                         or use one of the following locations :",
                        self.command_names()[0]
                    ),
                );
            }

            let mut sb: Vec<String> = TeleportCommandRegions::ALL
                .iter()
                .map(|r| format!("|cFFFFFFFF{}|r: ", r.name()))
                .collect();
            for item in &self.locations {
                sb[item.region as usize] += &format!("{}  ", item.name);
            }
            for s in &sb {
                CommandManager::send_normal_text(self, out, s);
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[location]".into()
    }

    fn get_command_help_text(&self) -> String {
        if self.allow_ping_pos {
            "Teleports you to target location. if no [location] is provided, you will get a list of available names. \
             You can also use a period (.) as a location name to teleport to a location you marked on the map."
                .into()
        } else {
            "Teleports you to target location. if no [location] is provided, you will get a list of available names."
                .into()
        }
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::move_cmd::tests::teleport_body;
    use super::super::ping_position::tests::install_terrain;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn teleport_named_locations_and_list() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let (handled, out) = harness::run(&server, &gm.p, "teleport Mines").await;
        assert!(handled);
        assert_eq!(
            out,
            [harness::normal(
                "teleport",
                "Teleporting to |cFFFFFFFFDewstone Plains, Sharpwind Mines|r"
            )]
        );
        assert!(gm.p.data().disabled_set_position);
        assert_eq!(
            gm.packets(),
            vec![(
                sc::SCTeleportUnitPacket,
                teleport_body(0, 11375.0, 13135.0, 146.0, 0.0)
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "teleport solis").await;
        assert_eq!(
            out,
            [harness::normal(
                "teleport",
                "Teleporting to |cFFFFFFFFSolis Headlands, Austera|r"
            )]
        );
        gm.packets();
        let (_, out) = harness::run(&server, &gm.p, "teleport Nowhere").await;
        assert_eq!(
            out,
            [harness::error("teleport", "Unavailable Location [Nowhere]")]
        );
        assert!(gm.packets().is_empty());

        let (_, out) = harness::run(&server, &gm.p, "teleport").await;
        assert_eq!(out.len(), 6);
        assert_eq!(
            out[0],
            harness::normal(
                "teleport",
                "Usage : /teleport <Location>\nUse a period (.) to teleport to YOUR marked location on the map, or use one of the following locations :"
            )
        );
        assert_eq!(
            out[1],
            harness::normal(
                "teleport",
                "|cFFFFFFFFEast|r: arcumiris  falcorth  hasla  mahadevi  perinoor  rookborne  silentforest  solis  tigerspine  villanelle  windscour  ynyster  "
            )
        );
        assert_eq!(
            out[5],
            harness::normal(
                "teleport",
                "|cFFFFFFFFOther|r: freedich  growlgate  ynys  nuiajail  haranyajail  nuian  elf  harani  firran  debug  "
            )
        );

        // Not in the main instance
        gm.p.data().unit.transform.instance_id = 5;
        let (_, out) = harness::run(&server, &gm.p, "teleport solis").await;
        assert_eq!(
            out,
            [harness::error(
                "teleport",
                "Named teleports are not allowed inside a instance"
            )]
        );
    }

    #[tokio::test]
    async fn teleport_to_ping_position() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let (_, out) = harness::run(&server, &gm.p, "teleport .").await;
        assert_eq!(
            out,
            [harness::normal(
                "teleport",
                "|cFFFFFF00Make sure you marked a location on the map WHILE IN A PARTY OR RAID, before using this teleport function.\nIf required, you can use the /soloparty command to make a party of just yourself.|r"
            )]
        );
        gm.p.data().local_ping_position = (10.25, 20.0, 0.0);
        let (_, out) = harness::run(&server, &gm.p, "teleport .").await;
        assert_eq!(
            out,
            [harness::normal(
                "teleport",
                "|cFFFF0000Target height was |cFFFFFFFFzero|cFFFF0000. You likely tried to teleport out of bounds, or no heightmap data was loaded on the server.\nIf you still want to move to the target location, you can use |cFFFFFFFF/move|cFFFF0000 to go to the following location|cFF40FF40\nX:10.3 Y:20.0|r"
            )]
        );
        assert!(gm.packets().is_empty());
        install_terrain(&server);
        let (_, out) = harness::run(&server, &gm.p, "teleport .").await;
        let h = out[0]
            .strip_prefix("[teleport] Teleporting to |cFFFFFFFFX:10.25 Y:20 Z:")
            .and_then(|s| s.strip_suffix("|r"))
            .and_then(|s| s.parse::<f32>().ok())
            .expect("teleport text");
        assert!((h - 27.5).abs() < 0.01, "{h}");
        assert_eq!(
            gm.packets(),
            vec![(
                sc::SCTeleportUnitPacket,
                teleport_body(0, 10.25, 20.0, h, 0.0)
            )]
        );
    }
}
