//! UpdatesForTransform (port of `Tools/UpdatesForTransform`).
//!
//! Helper tool to convert spawn files (`npc_spawns.json`, `doodad_spawns.json`,
//! `transfer_spawns.json`) from the old sbyte X/Y/Z rotation to Roll/Pitch/Yaw (degrees).
//! Interactive like the original: asks for the source folder (default `./Data`), offers to
//! restore earlier backups, converts (keeping `*.json.old` backups) and offers to delete the
//! backups. The converted files are byte-identical to the C# tool's output.

mod spawns;

use std::io::{self, BufRead, Write};
use std::path::{Path, PathBuf};
use std::time::Duration;

use aaemu_commons::file_manager::{decode_text, get_matching_files_in_directory};
use aaemu_commons::utils::json_helper::{serialize_newtonsoft, strip_newtonsoft_extensions};

use crate::spawns::{convert_spawner, NpcSpawnerOld, SpawnJsonError};

#[derive(Debug, thiserror::Error)]
pub enum ToolError {
    #[error(transparent)]
    Io(#[from] io::Error),
    #[error("{file}: {source}")]
    Json {
        file: String,
        #[source]
        source: serde_json::Error,
    },
    #[error("{file}: {source}")]
    Spawn {
        file: String,
        #[source]
        source: SpawnJsonError,
    },
    #[error("Object reference not set to an instance of an object. ({0})")]
    NullReference(String),
    #[error("Not everything was converted for file {0}")]
    NotEverythingConverted(String),
}

/// Program state (`OldConvertedFiles`, `OldEntriesFound`) plus console I/O.
pub struct App<R, W> {
    input: R,
    out: W,
    pub old_converted_files: Vec<PathBuf>,
    pub old_entries_found: i32,
    /// Pause before the "already converted" warning (1.5 s in the C#).
    pub warning_pause: Duration,
}

impl<R: BufRead, W: Write> App<R, W> {
    pub fn new(input: R, out: W) -> Self {
        Self {
            input,
            out,
            old_converted_files: Vec::new(),
            old_entries_found: 0,
            warning_pause: Duration::from_millis(1500),
        }
    }

    /// `Console.ReadLine()`: `None` at end of input.
    fn read_line(&mut self) -> io::Result<Option<String>> {
        self.out.flush()?;
        let mut s = String::new();
        if self.input.read_line(&mut s)? == 0 {
            return Ok(None);
        }
        Ok(Some(s.trim_end_matches(['\r', '\n']).to_string()))
    }

    /// `ConvertNpcSpawnsFile`.
    pub fn convert_npc_spawns_file(&mut self, file_name: &Path) -> Result<(), ToolError> {
        let old_file_name = file_name.with_extension("json.old");
        if old_file_name.exists() {
            writeln!(self.out, "Already converted {}", file_name.display())?;
            return Ok(());
        }
        let is_quat_rotation = file_name
            .file_stem()
            .map(|s| s.to_string_lossy().to_lowercase().contains("doodad"))
            .unwrap_or(false);
        let fname = || file_name.display().to_string();
        let old_json_data = decode_text(&std::fs::read(file_name)?);
        let value: serde_json::Value = serde_json::from_str(&strip_newtonsoft_extensions(
            &old_json_data,
        ))
        .map_err(|source| ToolError::Json {
            file: fname(),
            source,
        })?;
        let old_spawners =
            NpcSpawnerOld::list_from_json(&value).map_err(|source| ToolError::Spawn {
                file: fname(),
                source,
            })?;
        let mut new_spawners = Vec::new();
        if let Some(old) = &old_spawners {
            for os in old {
                let pos = os.position.ok_or_else(|| {
                    ToolError::NullReference(format!("{}: Position is null", fname()))
                })?;
                new_spawners.push(convert_spawner(os, &pos, is_quat_rotation));
                if pos.rotation_x != 0 || pos.rotation_y != 0 || pos.rotation_z != 0 {
                    self.old_entries_found += 1;
                }
            }
        }
        let Some(old_spawners) = old_spawners else {
            // C#: oldSpawners.Count on a null list
            return Err(ToolError::NullReference(format!(
                "{}: file contains null",
                fname()
            )));
        };
        if new_spawners.len() != old_spawners.len() {
            return Err(ToolError::NotEverythingConverted(fname()));
        }
        std::fs::rename(file_name, &old_file_name)?;
        self.old_converted_files.push(old_file_name);
        let new_json_data =
            serialize_newtonsoft(&new_spawners, true).map_err(|source| ToolError::Json {
                file: fname(),
                source,
            })?;
        std::fs::write(file_name, new_json_data)?;
        Ok(())
    }

    /// `Main`, with `default_search_path` instead of `./Data`.
    pub fn run(&mut self, default_search_path: &Path) -> Result<(), ToolError> {
        writeln!(
            self.out,
            "This is a helper tool to convert spawn files from the old sbyte X/Y/Z rotation to radians in Roll/Pitch/Yaw"
        )?;
        writeln!(
            self.out,
            "-----------------------------------------------------------------------------------------------------------"
        )?;
        writeln!(self.out)?;

        let mut search_path = std::path::absolute(default_search_path)?;
        writeln!(
            self.out,
            "Change Source Path ? (leave blank to keep default folder)"
        )?;
        write!(self.out, "{} : ", search_path.display())?;
        if let Some(custom) = self.read_line()? {
            if !custom.is_empty() {
                search_path = std::path::absolute(&custom)?;
            }
        }
        writeln!(self.out, "Searching for files in {}", search_path.display())?;
        let npc = get_matching_files_in_directory(&search_path, "npc_spawns.json", true);
        let doodad = get_matching_files_in_directory(&search_path, "doodad_spawns.json", true);
        let transfer = get_matching_files_in_directory(&search_path, "transfer_spawns.json", true);
        let mut all_files = Vec::new();
        all_files.extend(npc.iter().cloned());
        all_files.extend(doodad.iter().cloned());
        all_files.extend(transfer.iter().cloned());
        writeln!(self.out)?;
        writeln!(
            self.out,
            "Found {} files found ({} NPC, {} Doodad and {} transfers).",
            all_files.len(),
            npc.len(),
            doodad.len(),
            transfer.len()
        )?;

        for f in &all_files {
            if with_suffix(f, ".old").exists() {
                self.old_converted_files.push(f.clone());
            }
        }
        if !self.old_converted_files.is_empty() {
            writeln!(
                self.out,
                "Also found {} backup files from previous conversions",
                self.old_converted_files.len()
            )?;
            writeln!(self.out)?;
            write!(
                self.out,
                "Do you want to revert the backup files first ? (Y/n): "
            )?;
            let confirm_restore = self.read_line()?.map(|s| s.to_lowercase());
            if matches!(confirm_restore.as_deref(), Some("y") | Some("")) {
                writeln!(self.out, "Restoring previous backup files ...")?;
                let mut restored = 0;
                for new_f_name in self.old_converted_files.clone() {
                    let backup_name = with_suffix(&new_f_name, ".old");
                    let res = std::fs::remove_file(&new_f_name)
                        .or_else(|e| {
                            if e.kind() == io::ErrorKind::NotFound {
                                Ok(())
                            } else {
                                Err(e)
                            }
                        })
                        .and_then(|_| std::fs::rename(&backup_name, &new_f_name));
                    match res {
                        Ok(()) => restored += 1,
                        Err(_) => {
                            writeln!(self.out, "Failed to restore: {}", new_f_name.display())?
                        }
                    }
                }
                writeln!(self.out, "Restored {restored} files !")?;
                writeln!(self.out)?;
            }
        }

        writeln!(self.out)?;
        write!(self.out, "Continue with converting ? (y/N): ")?;
        let confirm = self.read_line()?.map(|s| s.to_lowercase());
        if confirm.as_deref() != Some("y") {
            writeln!(self.out, "Aborted.")?;
            return Ok(());
        }

        writeln!(self.out)?;
        writeln!(self.out, "Converting files ...")?;
        self.old_entries_found = 0;
        self.old_converted_files.clear();
        for file in &all_files {
            self.convert_npc_spawns_file(file)?;
        }
        writeln!(self.out, "Done ... ")?;
        writeln!(self.out)?;
        if self.old_entries_found <= 0 {
            writeln!(
                self.out,
                "+----------------------------------------------------------------+"
            )?;
            writeln!(
                self.out,
                "| It looks like the json files were already converted previously |"
            )?;
            writeln!(
                self.out,
                "| as no old rotation data was found.                             |"
            )?;
            writeln!(
                self.out,
                "| It is HIGHLY ADVISED to RESTORE the created .json.old files !  |"
            )?;
            writeln!(
                self.out,
                "| You will need to do this manually.                             |"
            )?;
            writeln!(
                self.out,
                "+----------------------------------------------------------------+"
            )?;
            writeln!(self.out)?;
            self.out.flush()?;
            std::thread::sleep(self.warning_pause);
        }
        write!(
            self.out,
            "Do you want to delete the {} old files ? (y/N) : ",
            self.old_converted_files.len()
        )?;
        // C# crashed on end of input here (null.ToLower()); treated as "no".
        let do_delete = self.read_line()?.unwrap_or_default();
        let mut deleted = 0;
        if do_delete.to_lowercase().starts_with('y') {
            for old_f_name in self.old_converted_files.clone() {
                match std::fs::remove_file(&old_f_name) {
                    Ok(()) => deleted += 1,
                    Err(_) => writeln!(self.out, "Failed to delete: {}", old_f_name.display())?,
                }
            }
        }
        writeln!(self.out, "Deleted {deleted} old files.")?;
        Ok(())
    }
}

/// `path + suffix` (C#: `f.FullName + ".old"`).
fn with_suffix(path: &Path, suffix: &str) -> PathBuf {
    let mut s = path.as_os_str().to_owned();
    s.push(suffix);
    PathBuf::from(s)
}

fn main() {
    let stdin = io::stdin();
    let stdout = io::stdout();
    let mut app = App::new(stdin.lock(), stdout.lock());
    if let Err(e) = app.run(&Path::new(".").join("Data")) {
        eprintln!("Unhandled exception. {e}");
        std::process::exit(1);
    }
}

#[cfg(test)]
mod tests;
