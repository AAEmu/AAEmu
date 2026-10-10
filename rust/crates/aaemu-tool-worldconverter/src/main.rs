//! AAEmu.WorldConverter (port of `Tools/WorldConverter`).
//!
//! Tool that was used to generate pre-generated heightmap data for use with AAEmu.
//! Original Authors: AANut, NLObP, AAGene, HexLulz, Nikes, Igor, ZeromusXYZ, Alex.
//!
//! Usage: `aaemu-worldconverter -in "C:\PathToUnpackedAA\game\worlds" -out "C:\PathToGenerateFiles"
//! [-export gimp|unity] [-split] [-ver 1|2|draft]`
//!
//! Output (byte-identical to the C# tool):
//! * `<out>\worlds.json` — Newtonsoft-style indented JSON of all worlds,
//! * `<out>\<world>\zones.json` — compact JSON of the zones/cells/sectors,
//! * `<out>\<world>\hmap.dat` — binary heightmap (see [`write_hmap_dat`]),
//! * with `-export`: `<out>\<world>[(xx-yy)].hmap.data|raw` raw 16-bit heightmaps.
//!
//! Output paths are built like the C# (`{output}\{name}`), i.e. with a literal backslash.

mod hmap;
mod world;

use std::fs::{File, OpenOptions};
use std::io::{self, BufWriter, Write};
use std::path::{Path, PathBuf};

use aaemu_commons::dotnet_fmt::{format_f32, format_f64};
use aaemu_commons::utils::json_helper::serialize_newtonsoft;

use crate::hmap::{Hmap, HmapError};
use crate::world::{Cell, JWorld, Sector, World, WorldXmlError, Zone};

const CELL_SIZE: usize = 512;
const SECTOR_SIZE: usize = 32;
const SECTORS_IN_CELL: usize = CELL_SIZE / SECTOR_SIZE;

/// `VersionCalc`: how heights are converted.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum VersionCalc {
    V1 = 1,
    V2 = 2,
    Draft = 3,
}

/// `Export`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Export {
    None,
    Gimp,
    Unity,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Options {
    pub input: String,
    pub output: String,
    pub version: VersionCalc,
    pub export: Export,
    pub split_size: usize,
    pub show_help: bool,
}

/// Parses the command line exactly like the C# loop (every argument is checked as an option
/// and the following argument is its value; `-split` takes no value).
pub fn parse_args(args: &[String]) -> Options {
    let mut o = Options {
        input: String::new(),
        output: String::new(),
        version: VersionCalc::Draft,
        export: Export::None,
        split_size: 0,
        show_help: false,
    };
    for c in 0..args.len() {
        let a = args[c].to_lowercase();
        let p = args.get(c + 1).cloned().unwrap_or_default();
        if a == "-in" || a == "-i" {
            o.input = p.clone();
        }
        if a == "-out" || a == "-o" {
            o.output = p.clone();
        }
        if a == "-export" || a == "-e" {
            o.export = match p.to_lowercase().as_str() {
                "gimp" => Export::Gimp,
                "unity" => Export::Unity,
                _ => Export::None,
            };
        }
        if a == "-ver" || a == "-version" || a == "-v" {
            o.version = match p.to_lowercase().as_str() {
                "1" | "v1" => VersionCalc::V1,
                "2" | "v2" => VersionCalc::V2,
                _ => VersionCalc::Draft,
            };
        }
        if a == "-split" {
            o.split_size = 512;
        }
        if a == "-help" || a == "-?" || a == "/?" {
            o.show_help = true;
        }
    }
    o
}

#[derive(Debug, thiserror::Error)]
pub enum ConvertError {
    #[error(transparent)]
    Io(#[from] io::Error),
    #[error("{file}: {source}")]
    WorldXml {
        file: String,
        #[source]
        source: WorldXmlError,
    },
    #[error("{file}: {source}")]
    Heightmap {
        file: String,
        #[source]
        source: HmapError,
    },
    #[error(transparent)]
    Json(#[from] serde_json::Error),
}

/// A converted world: the JSON part plus the `[JsonIgnore]` members of the C# `JWorld`.
pub struct JWorldData {
    pub json: JWorld,
    pub heighest_point: u32,
    pub zones: Vec<Zone>,
    /// `HeightMap.Length` (number of cells).
    pub height_map_length: i32,
    /// `HeightMapRaw[x, y]`, stored as `x * (cell_y * 512) + y`.
    pub height_map_raw: Vec<u16>,
}

impl JWorldData {
    fn height(&self) -> usize {
        self.json.cell_y.max(0) as usize * CELL_SIZE
    }

    fn raw(&self, x: usize, y: usize) -> u16 {
        self.height_map_raw[x * self.height() + y]
    }
}

fn help_text() -> String {
    let exe = std::env::current_exe()
        .ok()
        .and_then(|p| p.file_stem().map(|s| s.to_string_lossy().into_owned()))
        .unwrap_or_else(|| "AAEmu.WorldConverter".into());
    [
        "WorldConverter tool to generate heightmaps for use in AAEmu".to_string(),
        String::new(),
        format!(r#"{exe} -in "C:\PathToUnpackedAA\game\worlds" -out "C:\PathToGenerateFiles""#),
        String::new(),
        "\t-in\t\tSpecifies the source directory where to start searching for world files.".into(),
        "\t\t\tThis needs to be from the unpacked game_pak client files and point to the worlds folder within.".into(),
        String::new(),
        "\t-out\t\tSpecifies the directory where to create the generate files.".into(),
        String::new(),
        "\t-export\t\tCreate additional heightmap files. Possible options are:".into(),
        "\t\t\tgimp\tcreates raw .data that can be opened as a 16-bit grayscale LittleEndian file".into(),
        "\t\t\tunity\tcreates .raw data that can be used as a heightmap in Unity".into(),
        "\t\t\t".into(),
        "\t-split\t\tSplits result of -export in blocks of cells instead of a single map (512x512 pixels).".into(),
        "\t\t\tif you use import in unity, you can import them with settings of 1024x1024 size,".into(),
        "\t\t\tand 1024 max height for import of each cell (space them by 1024 on X/Z axis).".into(),
        "\t\t\t".into(),
        "\t-ver\t\tChanges which technique version is used to generate the heightmap data.".into(),
        "\t\t\tCheck source code for how they affect the outcome.".into(),
        "\t\t\t".into(),
    ]
    .join("\n")
}

/// `SearchFiles`: `world.xml` directly inside each sub-directory of `path`.
fn search_files(path: &Path) -> io::Result<Vec<PathBuf>> {
    let mut dirs: Vec<PathBuf> = std::fs::read_dir(path)?
        .filter_map(Result::ok)
        .filter(|e| e.file_type().map(|t| t.is_dir()).unwrap_or(false))
        .map(|e| e.path())
        .collect();
    // NTFS returns directory entries ordered by their upper-cased names.
    dirs.sort_by_key(|d| d.file_name().map(|n| n.to_string_lossy().to_uppercase()));
    let mut files = Vec::new();
    for dir in dirs {
        let found =
            aaemu_commons::file_manager::get_matching_files_in_directory(&dir, "world.xml", false);
        files.extend(found);
    }
    Ok(files)
}

/// Converts one `world.xml` (+ its cells' heightmaps), printing progress like the C#.
pub fn convert_world(
    input: &str,
    file: &Path,
    version: VersionCalc,
) -> Result<JWorldData, ConvertError> {
    let xml = aaemu_commons::file_manager::decode_text(&std::fs::read(file)?);
    let world = World::parse(&xml).map_err(|source| ConvertError::WorldXml {
        file: file.display().to_string(),
        source,
    })?;
    let zones = world
        .zone_list
        .iter()
        .map(|z| Zone {
            id: z.id,
            name: z.name.clone(),
            cells: z
                .cell_list
                .iter()
                .map(|c| Cell {
                    sectors: c
                        .sector_list
                        .iter()
                        .map(|s| Sector {
                            x: i32::from(s.x),
                            y: i32::from(s.y),
                        })
                        .collect(),
                    x: i32::from(c.x),
                    y: i32::from(c.y),
                })
                .collect(),
        })
        .collect();
    let max_height = f32::from(world.max_terrain_height); // usually 4096
    let height_max_coeff = f64::from(u16::MAX) / (f64::from(max_height) / 2.0); // was 4.0
    let cell_x = i32::from(world.cell_x_count);
    let cell_y = i32::from(world.cell_y_count);
    let mut jw = JWorldData {
        json: JWorld {
            id: 0,
            name: world.name.clone(),
            max_height,
            height_max_coeff,
            ocean_level: f32::from(world.ocean_level),
            cell_x,
            cell_y,
        },
        heighest_point: 0,
        zones,
        height_map_length: cell_x * cell_y,
        height_map_raw: vec![0u16; cell_x as usize * CELL_SIZE * cell_y as usize * CELL_SIZE],
    };
    let name = jw.json.name.clone().unwrap_or_default();
    println!("[{name}] Parse xml done.");

    let height = jw.height();
    for cell_x in 0..jw.json.cell_x as usize {
        for cell_y in 0..jw.json.cell_y as usize {
            let height_map = format!(
                "{input}/{name}/cells/{cell_x:03}_{cell_y:03}/client/terrain/heightmap.dat"
            );
            if !Path::new(&height_map).is_file() {
                continue;
            }
            let data = std::fs::read(&height_map)?;
            let hm_err = |source| ConvertError::Heightmap {
                file: height_map.clone(),
                source,
            };
            let hmap = Hmap::read(&data, version == VersionCalc::V1).map_err(hm_err)?;
            let nodes = hmap.sorted_nodes();
            for sector_x in 0..SECTORS_IN_CELL {
                for sector_y in 0..SECTORS_IN_CELL {
                    // C#: nodes[...] throws IndexOutOfRangeException when there are fewer nodes
                    let node = nodes
                        .get(sector_x * SECTORS_IN_CELL + sector_y)
                        .ok_or(HmapError::IndexOutOfRange)
                        .map_err(hm_err)?;
                    for unit_x in 0..SECTOR_SIZE as u16 {
                        for unit_y in 0..SECTOR_SIZE as u16 {
                            let o_x = cell_x * CELL_SIZE + sector_x * SECTOR_SIZE + unit_x as usize;
                            let o_y = cell_y * CELL_SIZE + sector_y * SECTOR_SIZE + unit_y as usize;
                            let mut value: u32 = match version {
                                VersionCalc::V1 => {
                                    let double_value = f64::from(node.f_range) * 100000.0;
                                    let raw_value = node.raw_data_by_index(unit_x, unit_y);
                                    let coeff = jw.json.height_max_coeff;
                                    #[allow(clippy::excessive_precision)]
                                    const DIVIDER: f32 = 1.526_043_356_207_11;
                                    (double_value / f64::from(DIVIDER) * coeff
                                        / f64::from(u16::MAX)
                                        * f64::from(
                                            f32::from(raw_value) + node.box_heightmap.min[2],
                                        )
                                        * coeff) as u32
                                }
                                VersionCalc::V2 => {
                                    u32::from(node.raw_data_by_index(unit_x, unit_y))
                                }
                                VersionCalc::Draft => {
                                    let h = node.get_height(unit_x, unit_y).map_err(hm_err)?;
                                    (f64::from(h) * jw.json.height_max_coeff) as u32
                                }
                            };
                            if value > 65535 {
                                value = 65535; // cap it at max
                            }
                            if value > jw.heighest_point {
                                jw.heighest_point = value;
                            }
                            jw.height_map_raw[o_x * height + o_y] = value as u16;
                        }
                    }
                }
            }
            println!(
                "[{name}] Done parse cell, X: {cell_x:03}, Y: {cell_y:03}, Ver: {}, Flag: {:02X}-{:02X}, ZRatio:{}",
                hmap.version,
                hmap.flags,
                hmap.flags2,
                format_f32(hmap.heightmap_z_ratio)
            );
        }
    }
    let real_height = f64::from(jw.heighest_point) / jw.json.height_max_coeff;
    println!(
        "[{name}] parse completed. HeighestPoint value: {} => {}",
        jw.heighest_point,
        format_f64(real_height)
    );
    Ok(jw)
}

/// Writes the `-export` raw heightmap file(s) for one world.
pub fn export_world(
    output: &str,
    jw: &JWorldData,
    export: Export,
    split_size: usize,
) -> io::Result<()> {
    let format = match export {
        Export::Gimp => "data",
        Export::Unity => "raw",
        Export::None => return Ok(()),
    };
    let name = jw.json.name.clone().unwrap_or_default();
    let (cx, cy) = (
        jw.json.cell_x.max(0) as usize,
        jw.json.cell_y.max(0) as usize,
    );
    let mut y_pos = 0;
    while y_pos < cy {
        let mut x_pos = 0;
        while x_pos < cx {
            let (split_name, start_x, size_x, start_y, size_y);
            if split_size > 0 {
                split_name = format!("({x_pos:02}-{y_pos:02})");
                (start_x, size_x, start_y, size_y) =
                    (CELL_SIZE * x_pos, CELL_SIZE, CELL_SIZE * y_pos, CELL_SIZE);
            } else {
                split_name = String::new();
                (start_x, size_x, start_y, size_y) = (0, CELL_SIZE * cx, 0, CELL_SIZE * cy);
                // mark pos as being "the last cell" for the loop
                x_pos = cx;
                y_pos = cy;
            }
            let path = format!("{output}\\{name}{split_name}.hmap.{format}");
            let mut bw = BufWriter::new(File::create(&path)?);
            print!("[{name}] exporting heightmap data {name}{split_name} ... ");
            let _ = io::stdout().flush();
            let rows: Box<dyn Iterator<Item = usize>> = if export == Export::Gimp {
                Box::new((start_y..start_y + size_y).rev())
            } else {
                Box::new(start_y..start_y + size_y)
            };
            for y in rows {
                for x in start_x..start_x + size_x {
                    bw.write_all(&jw.raw(x, y).to_le_bytes())?;
                }
            }
            bw.flush()?;
            println!("Done.");
            x_pos += 1;
        }
        y_pos += 1;
    }
    Ok(())
}

/// Writes `hmap.dat`:
/// `i32 1 (version), i32 cellX, i32 cellY, f64 heightMaxCoeff, i32 cellX*cellY`, then per cell
/// (x outer, y inner) `bool empty` and, if not empty, 512*512 `u16` ordered by sector x,
/// sector y, unit x, unit y.
///
/// Like the C# (`File.OpenWrite`), an existing file is overwritten without being truncated.
pub fn write_hmap_dat(path: &str, jw: &JWorldData) -> io::Result<()> {
    let f = OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(false)
        .open(path)?;
    let mut bw = BufWriter::new(f);
    bw.write_all(&1i32.to_le_bytes())?; // version
    bw.write_all(&jw.json.cell_x.to_le_bytes())?;
    bw.write_all(&jw.json.cell_y.to_le_bytes())?;
    bw.write_all(&jw.json.height_max_coeff.to_le_bytes())?;
    bw.write_all(&jw.height_map_length.to_le_bytes())?;
    let mut data = vec![0u16; CELL_SIZE * CELL_SIZE];
    for cell_x in 0..jw.json.cell_x.max(0) as usize {
        for cell_y in 0..jw.json.cell_y.max(0) as usize {
            for sector_x in 0..SECTORS_IN_CELL {
                for sector_y in 0..SECTORS_IN_CELL {
                    let sector_pos_x = cell_x * CELL_SIZE + sector_x * SECTOR_SIZE;
                    let sector_pos_y = cell_y * CELL_SIZE + sector_y * SECTOR_SIZE;
                    for x in 0..SECTOR_SIZE {
                        for y in 0..SECTOR_SIZE {
                            let value = jw.raw(sector_pos_x + x, sector_pos_y + y);
                            data[(sector_x * SECTOR_SIZE + x) * CELL_SIZE
                                + sector_y * SECTOR_SIZE
                                + y] = value;
                        }
                    }
                }
            }
            if data.iter().all(|&v| v == 0) {
                bw.write_all(&[1])?;
            } else {
                bw.write_all(&[0])?;
                for sector_x in 0..SECTORS_IN_CELL {
                    for sector_y in 0..SECTORS_IN_CELL {
                        for x in 0..SECTOR_SIZE {
                            for y in 0..SECTOR_SIZE {
                                let v = data[(sector_x * SECTOR_SIZE + x) * CELL_SIZE
                                    + sector_y * SECTOR_SIZE
                                    + y];
                                bw.write_all(&v.to_le_bytes())?;
                            }
                        }
                    }
                }
            }
        }
    }
    bw.flush()
}

/// The whole program (`Main`).
pub fn run(args: &[String]) -> Result<(), ConvertError> {
    let o = parse_args(args);
    if o.input.is_empty() || o.output.is_empty() || o.show_help {
        println!("{}", help_text());
        return Ok(());
    }
    if !Path::new(&o.input).is_dir() {
        println!("Input folder not found {}", o.input);
        return Ok(());
    }
    let mut files = search_files(Path::new(&o.input))?;
    if files.is_empty() {
        println!("Nothing to do for source {}", o.input);
        return Ok(());
    }
    files.reverse();

    let mut worlds = Vec::new();
    for file in &files {
        let jw = convert_world(&o.input, file, o.version)?;
        export_world(&o.output, &jw, o.export, o.split_size)?;
        println!(
            "[{}][{}][{}] Done.",
            jw.json.name.as_deref().unwrap_or_default(),
            jw.json.cell_x * CELL_SIZE as i32,
            jw.json.cell_y * CELL_SIZE as i32
        );
        worlds.push(jw);
    }

    let output = &o.output;
    let all: Vec<&JWorld> = worlds.iter().map(|w| &w.json).collect();
    std::fs::write(
        format!("{output}\\worlds.json"),
        serialize_newtonsoft(&all, true)?,
    )?;
    for world in &worlds {
        let name = world.json.name.as_deref().unwrap_or_default();
        // C# checks `{output}\${world.Name}` (stray `$`), so this practically never matches.
        if Path::new(&format!("{output}\\${name}")).is_dir() {
            println!("World {name} is exist...");
        } else {
            std::fs::create_dir_all(format!("{output}\\{name}"))?;
            std::fs::write(
                format!("{output}\\{name}\\zones.json"),
                serialize_newtonsoft(&world.zones, false)?,
            )?;
            write_hmap_dat(&format!("{output}\\{name}\\hmap.dat"), world)?;
            println!("[{name}] Done Write to output");
        }
    }
    Ok(())
}

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if let Err(e) = run(&args) {
        eprintln!("Unhandled exception. {e}");
        std::process::exit(1);
    }
}

#[cfg(test)]
mod tests;
