//! `Tools/WorldConverter/Models/World.cs` (the client `world.xml`, read with `XmlSerializer`)
//! and `JWorld.cs` (the JSON output models).

use aaemu_commons::utils::json_helper::newtonsoft_float;
use serde::Serialize;

#[derive(Debug, thiserror::Error)]
pub enum WorldXmlError {
    #[error("There is an error in XML document: {0}")]
    Xml(#[from] roxmltree::Error),
    #[error("There is an error in XML document: <{0} xmlns=''> was not expected.")]
    UnexpectedRoot(String),
    #[error("There is an error in XML document: invalid value {value:?} for attribute {attr}")]
    InvalidValue { attr: String, value: String },
}

/// `World` (root of `world.xml`).
#[allow(dead_code)] // all attributes are read, not all are used
#[derive(Debug, Clone, Default, PartialEq)]
pub struct World {
    pub zone_list: Vec<WorldZone>,
    pub name: Option<String>,
    pub resolution: u16,
    pub unit_size: u8,
    pub cell_x_count: u8,
    pub cell_y_count: u8,
    pub is_instance: u8,
    pub next_layer_id: u8,
    pub next_surface_id: u8,
    pub ocean_level: u8,
    pub max_terrain_height: u16,
    pub is_release_branch: u8,
}

#[derive(Debug, Clone, Default, PartialEq)]
#[allow(dead_code)]
pub struct WorldZone {
    pub cell_list: Vec<WorldZoneCell>,
    pub name: Option<String>,
    pub id: u16,
    pub origin_x: u8,
    pub origin_y: u8,
}

#[derive(Debug, Clone, Default, PartialEq)]
pub struct WorldZoneCell {
    pub sector_list: Vec<WorldZoneCellSector>,
    pub x: u8,
    pub y: u8,
}

#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct WorldZoneCellSector {
    pub x: u8,
    pub y: u8,
}

fn attr<T: std::str::FromStr + Default>(
    node: roxmltree::Node<'_, '_>,
    name: &str,
) -> Result<T, WorldXmlError> {
    match node.attribute(name) {
        None => Ok(T::default()),
        Some(v) => {
            let t = v.trim();
            t.strip_prefix('+')
                .unwrap_or(t)
                .parse()
                .map_err(|_| WorldXmlError::InvalidValue {
                    attr: name.to_string(),
                    value: v.to_string(),
                })
        }
    }
}

fn children<'a, 'input>(
    node: roxmltree::Node<'a, 'input>,
    name: &'static str,
) -> impl Iterator<Item = roxmltree::Node<'a, 'input>> {
    node.children().filter(move |n| {
        n.is_element() && n.tag_name().name() == name && n.tag_name().namespace().is_none()
    })
}

/// Items of an `[XmlArrayItem(item)]` array property `array` (missing array → empty; the C#
/// would get `null` and crash).
fn array_items<'a, 'input>(
    node: roxmltree::Node<'a, 'input>,
    array: &'static str,
    item: &'static str,
) -> Vec<roxmltree::Node<'a, 'input>> {
    children(node, array)
        .flat_map(|a| children(a, item))
        .collect()
}

impl World {
    /// Deserializes `world.xml`.
    pub fn parse(xml: &str) -> Result<Self, WorldXmlError> {
        let doc = roxmltree::Document::parse(xml)?;
        let root = doc.root_element();
        if root.tag_name().name() != "World" || root.tag_name().namespace().is_some() {
            return Err(WorldXmlError::UnexpectedRoot(
                root.tag_name().name().to_string(),
            ));
        }
        let zone_list = array_items(root, "ZoneList", "Zone")
            .into_iter()
            .map(|z| {
                let cell_list = array_items(z, "cellList", "cell")
                    .into_iter()
                    .map(|c| {
                        let sector_list = array_items(c, "sectorList", "sector")
                            .into_iter()
                            .map(|s| {
                                Ok(WorldZoneCellSector {
                                    x: attr(s, "x")?,
                                    y: attr(s, "y")?,
                                })
                            })
                            .collect::<Result<_, WorldXmlError>>()?;
                        Ok(WorldZoneCell {
                            sector_list,
                            x: attr(c, "x")?,
                            y: attr(c, "y")?,
                        })
                    })
                    .collect::<Result<_, WorldXmlError>>()?;
                Ok(WorldZone {
                    cell_list,
                    name: z.attribute("name").map(str::to_string),
                    id: attr(z, "id")?,
                    origin_x: attr(z, "originX")?,
                    origin_y: attr(z, "originY")?,
                })
            })
            .collect::<Result<_, WorldXmlError>>()?;
        Ok(World {
            zone_list,
            name: root.attribute("name").map(str::to_string),
            resolution: attr(root, "resolution")?,
            unit_size: attr(root, "unitSize")?,
            cell_x_count: attr(root, "cellXCount")?,
            cell_y_count: attr(root, "cellYCount")?,
            is_instance: attr(root, "isInstance")?,
            next_layer_id: attr(root, "nextLayerId")?,
            next_surface_id: attr(root, "nextSurfaceId")?,
            ocean_level: attr(root, "oceanLevel")?,
            max_terrain_height: attr(root, "maxTerrainHeight")?,
            is_release_branch: attr(root, "isReleaseBranch")?,
        })
    }
}

/// `JWorld` (serialized into `worlds.json`; the `[JsonIgnore]` members live in
/// [`crate::JWorldData`]).
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct JWorld {
    pub id: u8,
    pub name: Option<String>,
    #[serde(serialize_with = "newtonsoft_float::f32")]
    pub max_height: f32,
    #[serde(serialize_with = "newtonsoft_float::f64")]
    pub height_max_coeff: f64,
    #[serde(serialize_with = "newtonsoft_float::f32")]
    pub ocean_level: f32,
    pub cell_x: i32,
    pub cell_y: i32,
}

/// `Zone` (serialized into `<world>/zones.json`).
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct Zone {
    pub id: u16,
    pub name: Option<String>,
    pub cells: Vec<Cell>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct Cell {
    pub x: i32,
    pub y: i32,
    pub sectors: Vec<Sector>,
}

#[derive(Debug, Clone, Copy, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct Sector {
    pub x: i32,
    pub y: i32,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_world_xml() {
        let xml = r#"<World name="w" resolution="1024" unitSize="2" cellXCount="2" cellYCount="3" isInstance="1" oceanLevel="100" maxTerrainHeight="4096">
 <ZoneList>
  <Zone name="z" id="198" originX="0" originY="0">
   <cellList>
    <cell x="0" y="0"><sectorList><sector x="14" y="5"/><sector x="1" y="2"/></sectorList></cell>
    <cell x="1" y="1"><sectorList /></cell>
   </cellList>
  </Zone>
 </ZoneList>
</World>"#;
        let w = World::parse(xml).unwrap();
        assert_eq!(w.name.as_deref(), Some("w"));
        assert_eq!(
            (
                w.cell_x_count,
                w.cell_y_count,
                w.max_terrain_height,
                w.ocean_level
            ),
            (2, 3, 4096, 100)
        );
        assert_eq!(w.zone_list.len(), 1);
        assert_eq!(w.zone_list[0].id, 198);
        assert_eq!(w.zone_list[0].cell_list.len(), 2);
        assert_eq!(
            w.zone_list[0].cell_list[0].sector_list[1],
            WorldZoneCellSector { x: 1, y: 2 }
        );
        assert!(World::parse("<Other/>").is_err());
        assert!(World::parse(r#"<World cellXCount="300"/>"#).is_err());
    }
}
