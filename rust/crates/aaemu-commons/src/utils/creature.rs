//! Port of `AAEmu.Commons.Utils.Creatures.Creature`: id/name pairs loaded from
//! `Data/Creatures.xml` and `Data/Doodads.xml` (`<Creatures><Creature Id=".." Name=".."/>...`).

use std::cmp::Ordering;
use std::collections::HashMap;
use std::fmt;
use std::path::Path;

use crate::error::GameError;
use crate::file_manager;

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Creature {
    /// XML attribute `Id`.
    pub id: u32,
    /// XML attribute `Name` (C# `Title`).
    pub title: String,
}

impl Creature {
    pub fn new(id: u32, title: impl Into<String>) -> Self {
        Self {
            id,
            title: title.into(),
        }
    }

    /// Parses an `XmlSerializer` list document with root element `root_name`.
    pub fn parse_list(xml: &str, root_name: &str) -> Result<Vec<Creature>, GameError> {
        let doc = roxmltree::Document::parse(xml)
            .map_err(|e| GameError::with_source("Invalid XML", e))?;
        let root = doc.root_element();
        if root.tag_name().name() != root_name {
            return Err(GameError::new(format!(
                "<{root_name} xmlns=''> was not expected (found <{}>).",
                root.tag_name().name()
            )));
        }
        root.children()
            .filter(|n| n.is_element() && n.tag_name().name() == "Creature")
            .map(|n| {
                let id = match n.attribute("Id") {
                    Some(v) => v
                        .trim()
                        .parse()
                        .map_err(|e| GameError::with_source(format!("Invalid Id {v:?}"), e))?,
                    None => 0,
                };
                Ok(Creature::new(id, n.attribute("Name").unwrap_or_default()))
            })
            .collect()
    }

    /// Loads `<file>` (root `Creatures`) into a map by id; logs and returns an empty map on
    /// failure. Duplicate ids: the C# `ToDictionary` threw; here the last one wins.
    pub fn load_file(path: impl AsRef<Path>) -> HashMap<u32, Creature> {
        let path = path.as_ref();
        let res = file_manager::get_file_contents(path)
            .map_err(|e| GameError::with_source("read failed", e))
            .and_then(|s| {
                if s.is_empty() {
                    Err(GameError::new("File does not exist"))
                } else {
                    Self::parse_list(&s, "Creatures")
                }
            });
        match res {
            Ok(list) => list.into_iter().map(|c| (c.id, c)).collect(),
            Err(e) => {
                tracing::error!(
                    "Failed to load {} /n{e} {}",
                    path.file_name().unwrap_or_default().to_string_lossy(),
                    file_manager::app_path()
                );
                HashMap::new()
            }
        }
    }

    /// `GetAllDoodads`: `<AppPath>/Data/Doodads.xml`.
    pub fn get_all_doodads() -> HashMap<u32, Creature> {
        Self::load_file(format!("{}/Data/Doodads.xml", file_manager::app_path()))
    }

    /// `GetAllCreatures`: `<AppPath>/Data/Creatures.xml`.
    pub fn get_all_creatures() -> HashMap<u32, Creature> {
        Self::load_file(format!("{}/Data/Creatures.xml", file_manager::app_path()))
    }
}

impl fmt::Display for Creature {
    /// C#: `Id + " ­- " + Title` (the source contains a soft hyphen).
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{} \u{AD}- {}", self.id, self.title)
    }
}

impl PartialOrd for Creature {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for Creature {
    /// `CompareTo`: by id (ties broken by title to stay consistent with `Eq`).
    fn cmp(&self, other: &Self) -> Ordering {
        self.id
            .cmp(&other.id)
            .then_with(|| self.title.cmp(&other.title))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_and_display() {
        let xml = "<?xml version=\"1.0\" ?> \r\n<Creatures xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n\t<Creature Id=\"2\" Name=\"Castle Guard\" />\r\n\t<Creature Id=\"3\" Name=\"Siege &amp; Co\" /></Creatures>";
        let list = Creature::parse_list(xml, "Creatures").unwrap();
        assert_eq!(
            list,
            vec![
                Creature::new(2, "Castle Guard"),
                Creature::new(3, "Siege & Co")
            ]
        );
        assert_eq!(list[0].to_string(), "2 \u{AD}- Castle Guard");
        assert!(list[0] < list[1]);
        assert!(Creature::parse_list(xml, "Other").is_err());
        assert!(
            Creature::parse_list("<Creatures><Creature Id=\"x\"/></Creatures>", "Creatures")
                .is_err()
        );
    }

    /// Loads the real data files shipped with AAEmu.Game when present.
    #[test]
    fn load_repo_data_files() {
        let dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../AAEmu.Game/Data");
        if !dir.join("Creatures.xml").is_file() {
            return;
        }
        let creatures = Creature::load_file(dir.join("Creatures.xml"));
        let doodads = Creature::load_file(dir.join("Doodads.xml"));
        assert!(creatures.len() > 10_000, "{}", creatures.len());
        assert!(doodads.len() > 5_000, "{}", doodads.len());
        assert_eq!(creatures[&2].title, "Castle Guard");
        assert!(Creature::load_file(dir.join("missing.xml")).is_empty());
    }
}
