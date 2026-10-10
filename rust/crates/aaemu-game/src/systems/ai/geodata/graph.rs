//! The merged navigation graph: every loaded `.bai` folder (path sector or zone folder) of a
//! world template merged into one graph like the engine's `CGraph` (10.0.2.13
//! `cryaisystem_dedicate.dll`, `D:\aa-re\zone\agent_geom\sub_navmesh.md` §1.4):
//!
//! * **vertices** (the verts file's triangle corners) are de-duplicated by XY position
//!   (squared XY distance < 1e-8, i.e. < 1e-4 m; 0x311A1D14) and the file indices are remapped
//!   to graph indices (verts reader 0x311A2AC4);
//! * **nodes** are de-duplicated by XY (< 1e-4 m) and equal navType (0x31101C68);
//! * **links** are re-attached to the merged nodes (0x310FB7E8): a link to a node that is
//!   already connected overwrites the existing link's pass radius / edge / exposure / flags
//!   (last read wins, as `CGraph::Connect` + `SetRadius` 0x31083160), the pass radius is stored
//!   as signed centimetres in an i16 like the engine, self links are skipped, and links whose
//!   source or target id is not in the file are dropped ("Read a link to a node which could not
//!   be found").
//!
//! So A* can cross sector and cell borders. The engine keeps a 3x3-sector window around one
//! focus; the Rust server keeps its lazy per-cell loading (the 4x4 sectors of a cell on first
//! use) and merges each folder into the graph as it loads (incremental, never unloaded).
//!
//! Positions are compared in f64 world coordinates (file position + sector offset), which is
//! exact; the engine compares f32 origin-relative positions.
//!
//! [`NavGraph::enclosing`] is the engine's `GetEnclosing`: the triangle node whose XY triangle
//! (its 3 corner vertices) contains the point, with the height interpolated on the triangle's
//! plane.

use std::collections::{HashMap, HashSet};

use crate::systems::skills::controllers::Vec3;

use super::bai::{BaseBaiLoader, NodeRef};

/// The squared XY distance below which two vertices / nodes are the same (engine `< 1e-8`).
pub const MERGE_DISTANCE_SQ: f64 = 1e-8;
/// Engine link flag set from link byte +42 bit 0.
pub const LINK_FLAG_PURE_TRIANGULAR: u32 = 0x80_0000;
/// The default agent radius for NPC path finding: the X2 walkability probe radius
/// (`ai_DefaultWalkability` = 1, constants at 0x311FF070: 0.25, 0.06, 1.8, 0.6, 0.6).
pub const DEFAULT_AGENT_RADIUS: f32 = 0.25;

/// Bucket size of the de-duplication hash (much larger than the 1e-4 tolerance).
const MERGE_BUCKET: f64 = 0.01;
/// Bucket size of the triangle lookup grid.
const TRIANGLE_BUCKET: f64 = 8.0;
/// The most buckets one triangle is put into per axis (huge coastal triangles).
const MAX_TRIANGLE_BUCKETS: i64 = 128;

/// A link of the merged graph.
#[derive(Debug, Clone, PartialEq)]
pub struct GraphLink {
    /// The target node (index into [`NavGraph::nodes`]).
    pub target: u32,
    /// The widest agent radius that may pass (engine i16 centimetres; negative = blocked).
    pub max_pass_radius: f32,
    pub exposure: f32,
    pub edge_center: Vec3,
    /// [`LINK_FLAG_PURE_TRIANGULAR`] from byte +42 bit 0.
    pub flags: u32,
    /// The edge's corners (link bytes +40 / +41).
    pub edge: (u8, u8),
}

impl GraphLink {
    /// Whether an agent of `agent_radius` may use the link (blocked links never).
    pub fn passable(&self, agent_radius: f32) -> bool {
        self.max_pass_radius >= 0.0 && self.max_pass_radius >= agent_radius
    }
}

/// A node of the merged graph.
#[derive(Debug, Clone, PartialEq)]
pub struct GraphNode {
    /// World position (of the first copy read).
    pub pos: Vec3,
    pub nav_type: u16,
    /// Node descriptor byte +58.
    pub flags: u8,
    /// The triangle corners (indices into [`NavGraph::vertices`]) of a triangle node.
    pub triangle: Option<[u32; 3]>,
    pub links: Vec<GraphLink>,
    xy: (f64, f64),
}

/// Merge statistics (diagnostics / tests).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct MergeStats {
    pub folders: usize,
    pub vertices_read: usize,
    pub vertices_merged: usize,
    pub nodes_read: usize,
    pub nodes_merged: usize,
    pub links_read: usize,
    pub links_merged: usize,
    pub links_dropped: usize,
}

/// The merged navigation graph of one world template.
#[derive(Debug, Default)]
pub struct NavGraph {
    pub vertices: Vec<Vec3>,
    vertex_xy: Vec<(f64, f64)>,
    vertex_hash: HashMap<(i64, i64), Vec<u32>>,
    pub nodes: Vec<GraphNode>,
    node_hash: HashMap<(i64, i64), Vec<u32>>,
    triangle_grid: HashMap<(i64, i64), Vec<u32>>,
    /// Folder -> (net reader, node) -> graph node.
    node_maps: HashMap<String, Vec<Vec<u32>>>,
    merged: HashSet<String>,
    pub stats: MergeStats,
}

fn merge_key(x: f64, y: f64) -> (i64, i64) {
    (
        (x / MERGE_BUCKET).floor() as i64,
        (y / MERGE_BUCKET).floor() as i64,
    )
}

fn triangle_key(x: f64, y: f64) -> (i64, i64) {
    (
        (x / TRIANGLE_BUCKET).floor() as i64,
        (y / TRIANGLE_BUCKET).floor() as i64,
    )
}

fn world_xy(local: Vec3, offset: Vec3) -> (f64, f64) {
    (
        f64::from(local.x) + f64::from(offset.x),
        f64::from(local.y) + f64::from(offset.y),
    )
}

fn close(a: (f64, f64), b: (f64, f64)) -> bool {
    let (dx, dy) = (a.0 - b.0, a.1 - b.1);
    dx * dx + dy * dy < MERGE_DISTANCE_SQ
}

/// The engine's i16 centimetre pass radius (0x31083160).
pub fn quantize_radius(r: f32) -> f32 {
    let v = ((r * 100.0) as i32).clamp(-i32::from(i16::MAX), i32::from(i16::MAX));
    let v = if v == 0 && r != 0.0 {
        i32::from(r > 0.0)
    } else {
        v
    };
    v as f32 / 100.0
}

impl NavGraph {
    /// Whether a folder was merged.
    pub fn contains_folder(&self, folder: &str) -> bool {
        self.merged.contains(folder)
    }

    /// The graph node of a node of a merged folder's net reader.
    pub fn node_for(&self, folder: &str, r: NodeRef) -> Option<u32> {
        self.node_maps
            .get(folder)?
            .get(r.reader)?
            .get(r.node)
            .copied()
    }

    fn find_close(
        hash: &HashMap<(i64, i64), Vec<u32>>,
        xy: (f64, f64),
        mut accept: impl FnMut(u32) -> bool,
    ) -> Option<u32> {
        let (kx, ky) = merge_key(xy.0, xy.1);
        for dx in -1..=1 {
            for dy in -1..=1 {
                if let Some(v) = hash.get(&(kx + dx, ky + dy)) {
                    if let Some(i) = v.iter().copied().find(|i| accept(*i)) {
                        return Some(i);
                    }
                }
            }
        }
        None
    }

    fn add_vertex(&mut self, pos: Vec3, xy: (f64, f64)) -> u32 {
        let vxy = &self.vertex_xy;
        if let Some(i) = Self::find_close(&self.vertex_hash, xy, |i| close(vxy[i as usize], xy)) {
            self.stats.vertices_merged += 1;
            return i;
        }
        let i = self.vertices.len() as u32;
        self.vertices.push(pos);
        self.vertex_xy.push(xy);
        self.vertex_hash
            .entry(merge_key(xy.0, xy.1))
            .or_default()
            .push(i);
        i
    }

    fn index_triangle(&mut self, node: u32) {
        let Some(t) = self.nodes[node as usize].triangle else {
            return;
        };
        let xs = t.map(|v| self.vertex_xy[v as usize]);
        let (min_x, max_x) = (
            xs.iter().map(|p| p.0).fold(f64::MAX, f64::min),
            xs.iter().map(|p| p.0).fold(f64::MIN, f64::max),
        );
        let (min_y, max_y) = (
            xs.iter().map(|p| p.1).fold(f64::MAX, f64::min),
            xs.iter().map(|p| p.1).fold(f64::MIN, f64::max),
        );
        let (k0, k1) = (triangle_key(min_x, min_y), triangle_key(max_x, max_y));
        if k1.0 - k0.0 > MAX_TRIANGLE_BUCKETS || k1.1 - k0.1 > MAX_TRIANGLE_BUCKETS {
            tracing::debug!("navmesh triangle {node} too large for the lookup grid");
            return;
        }
        for kx in k0.0..=k1.0 {
            for ky in k0.1..=k1.1 {
                self.triangle_grid.entry((kx, ky)).or_default().push(node);
            }
        }
    }

    fn add_node(
        &mut self,
        pos: Vec3,
        xy: (f64, f64),
        nav_type: u16,
        flags: u8,
        triangle: Option<[u32; 3]>,
    ) -> u32 {
        let nodes = &self.nodes;
        if let Some(i) = Self::find_close(&self.node_hash, xy, |i| {
            let n = &nodes[i as usize];
            n.nav_type == nav_type && close(n.xy, xy)
        }) {
            self.stats.nodes_merged += 1;
            if self.nodes[i as usize].triangle.is_none() && triangle.is_some() {
                self.nodes[i as usize].triangle = triangle;
                self.index_triangle(i);
            }
            return i;
        }
        let i = self.nodes.len() as u32;
        self.nodes.push(GraphNode {
            pos,
            nav_type,
            flags,
            triangle,
            links: Vec::new(),
            xy,
        });
        self.node_hash
            .entry(merge_key(xy.0, xy.1))
            .or_default()
            .push(i);
        self.index_triangle(i);
        i
    }

    /// `CGraph::Connect` + the link attributes: a second link between the same nodes
    /// overwrites the first one's attributes.
    fn connect(&mut self, src: u32, link: GraphLink) {
        let links = &mut self.nodes[src as usize].links;
        match links.iter_mut().find(|l| l.target == link.target) {
            Some(l) => {
                self.stats.links_merged += 1;
                *l = link;
            }
            None => links.push(link),
        }
    }

    /// Merges one loaded folder (no-op when it already was). Returns whether it merged.
    pub fn merge(&mut self, loader: &BaseBaiLoader) -> bool {
        if !self.merged.insert(loader.folder.clone()) {
            return false;
        }
        self.stats.folders += 1;
        let mut maps = Vec::with_capacity(loader.net_mission_readers.len());
        for net in &loader.net_mission_readers {
            // the triangle corners index the same mission's verts file
            let verts = loader
                .vertex_mission_readers
                .iter()
                .find(|v| v.mission == net.mission);
            let vmap: Vec<u32> = verts
                .map(|v| {
                    v.obstacles
                        .iter()
                        .map(|o| {
                            self.stats.vertices_read += 1;
                            self.add_vertex(o.pos, world_xy(o.local_pos, v.offset))
                        })
                        .collect()
                })
                .unwrap_or_default();
            let mut nmap = Vec::with_capacity(net.nodes.len());
            for n in &net.nodes {
                self.stats.nodes_read += 1;
                let tri = (|| {
                    let mut t = [0u32; 3];
                    for (k, &o) in n.obstacle.iter().enumerate() {
                        t[k] = *vmap.get(usize::try_from(o).ok()?)?;
                    }
                    (t[0] != t[1] && t[1] != t[2] && t[0] != t[2]).then_some(t)
                })();
                nmap.push(self.add_node(
                    n.pos,
                    world_xy(n.local_pos, net.offset),
                    n.nav_type(),
                    n.bit_field0,
                    tri,
                ));
            }
            for l in &net.links {
                self.stats.links_read += 1;
                let (Some(s), Some(t)) = (l.source, l.target) else {
                    self.stats.links_dropped += 1;
                    continue;
                };
                let (src, dst) = (nmap[s], nmap[t]);
                if src == dst {
                    continue;
                }
                self.connect(
                    src,
                    GraphLink {
                        target: dst,
                        max_pass_radius: quantize_radius(l.max_pass_radius),
                        exposure: l.exposure,
                        edge_center: l.edge_center,
                        flags: if l.is_pure_triangular_link {
                            LINK_FLAG_PURE_TRIANGULAR
                        } else {
                            0
                        },
                        edge: (l.start_index, l.end_index),
                    },
                );
            }
            maps.push(nmap);
        }
        self.node_maps.insert(loader.folder.clone(), maps);
        true
    }

    /// The XY bounding box of a node's triangle (its position for other nodes).
    pub fn node_bounds(&self, node: u32) -> Option<((f64, f64), (f64, f64))> {
        let n = self.nodes.get(node as usize)?;
        Some(match n.triangle {
            Some(t) => {
                let p = t.map(|v| self.vertex_xy[v as usize]);
                (
                    (
                        p[0].0.min(p[1].0).min(p[2].0),
                        p[0].1.min(p[1].1).min(p[2].1),
                    ),
                    (
                        p[0].0.max(p[1].0).max(p[2].0),
                        p[0].1.max(p[1].1).max(p[2].1),
                    ),
                )
            }
            None => (n.xy, n.xy),
        })
    }

    /// The height of a node's triangle plane at an XY point, when the point is inside it.
    pub fn triangle_height(&self, node: u32, x: f64, y: f64) -> Option<f32> {
        let t = self.nodes.get(node as usize)?.triangle?;
        let p = t.map(|v| self.vertex_xy[v as usize]);
        let z = t.map(|v| f64::from(self.vertices[v as usize].z));
        let det = (p[1].1 - p[2].1) * (p[0].0 - p[2].0) + (p[2].0 - p[1].0) * (p[0].1 - p[2].1);
        if det.abs() < 1e-12 {
            return None; // degenerate
        }
        let l0 = ((p[1].1 - p[2].1) * (x - p[2].0) + (p[2].0 - p[1].0) * (y - p[2].1)) / det;
        let l1 = ((p[2].1 - p[0].1) * (x - p[2].0) + (p[0].0 - p[2].0) * (y - p[2].1)) / det;
        let l2 = 1.0 - l0 - l1;
        const EPS: f64 = -1e-9;
        if l0 < EPS || l1 < EPS || l2 < EPS {
            return None;
        }
        Some((l0 * z[0] + l1 * z[1] + l2 * z[2]) as f32)
    }

    /// `GetEnclosing(pos)`: the triangle node containing the XY point and the height of its
    /// plane there; with several (overlapping layers / border overlap) the one whose height is
    /// closest to `pos.z` wins (the first read on ties).
    pub fn enclosing(&self, pos: Vec3) -> Option<(u32, f32)> {
        let (x, y) = (f64::from(pos.x), f64::from(pos.y));
        let cands = self.triangle_grid.get(&triangle_key(x, y))?;
        let mut best: Option<(u32, f32)> = None;
        for &n in cands {
            if let Some(z) = self.triangle_height(n, x, y) {
                if best.is_none_or(|(_, bz)| (z - pos.z).abs() < (bz - pos.z).abs()) {
                    best = Some((n, z));
                }
            }
        }
        best
    }
}

#[cfg(test)]
mod tests {
    use super::super::bai::test_files::{net_mission_full, verts_mission, TLink, TNode};
    use super::super::bai::{NetMission, VertexMission};
    use super::*;

    /// A folder of a square [x0, x1] x [0, 10] split into two triangles (z = x / 10).
    fn square_folder(folder: &str, offset: f32, x0: f32, x1: f32, radius: f32) -> BaseBaiLoader {
        let (l0, l1) = (x0 - offset, x1 - offset);
        let corners = [
            [l0, 0.0, x0 / 10.0],
            [l1, 0.0, x1 / 10.0],
            [l1, 10.0, x1 / 10.0],
            [l0, 10.0, x0 / 10.0],
        ];
        let verts = verts_mission(&corners);
        let c = |a: usize, b: usize, d: usize| {
            [
                (corners[a][0] + corners[b][0] + corners[d][0]) / 3.0,
                (corners[a][1] + corners[b][1] + corners[d][1]) / 3.0,
                (corners[a][2] + corners[b][2] + corners[d][2]) / 3.0,
            ]
        };
        let net = net_mission_full(
            &[
                TNode::tri(1, c(0, 1, 2), [0, 1, 2]),
                TNode::tri(2, c(0, 2, 3), [0, 2, 3]),
            ],
            &[TLink::new(1, 2, radius), TLink::new(2, 1, radius)],
        );
        let off = Vec3::new(offset, 0.0, 0.0);
        let mut n = NetMission::read(&net, 0, off).unwrap();
        n.mission = "0".into();
        let mut v = VertexMission::read(&verts, 0, off).unwrap();
        v.mission = "0".into();
        BaseBaiLoader {
            folder: folder.into(),
            net_mission_readers: vec![n],
            vertex_mission_readers: vec![v],
            ..BaseBaiLoader::default()
        }
    }

    #[test]
    fn folders_merge_vertices_and_nodes_by_xy() {
        let mut g = NavGraph::default();
        // the same square read from two sectors with different offsets
        assert!(g.merge(&square_folder("003_000", 768.0, 1014.0, 1024.0, 1.0)));
        assert!(!g.merge(&square_folder("003_000", 768.0, 1014.0, 1024.0, 1.0)));
        assert!(g.merge(&square_folder("004_000", 1024.0, 1014.0, 1024.0, 2.0)));
        assert_eq!(g.vertices.len(), 4);
        assert_eq!(g.nodes.len(), 2);
        assert_eq!(g.stats.nodes_merged, 2);
        assert_eq!(g.stats.vertices_merged, 4);
        // the links are re-attached, the later folder's attributes win
        assert_eq!(g.nodes[0].links.len(), 1);
        assert_eq!(g.nodes[0].links[0].max_pass_radius, 2.0);
        assert_eq!(g.stats.links_merged, 2);
        assert_eq!(
            g.node_for("004_000", NodeRef { reader: 0, node: 1 }),
            Some(1)
        );
    }

    #[test]
    fn enclosing_interpolates_the_plane() {
        let mut g = NavGraph::default();
        g.merge(&square_folder("000_000", 0.0, 0.0, 10.0, 1.0));
        let (n, z) = g.enclosing(Vec3::new(7.5, 2.0, 100.0)).unwrap();
        assert_eq!(n, 0);
        assert!((z - 0.75).abs() < 1e-6, "{z}");
        let (n, z) = g.enclosing(Vec3::new(2.5, 8.0, 0.0)).unwrap();
        assert_eq!(n, 1);
        assert!((z - 0.25).abs() < 1e-6, "{z}");
        // edges and corners count as inside
        assert!(g.enclosing(Vec3::new(10.0, 10.0, 0.0)).is_some());
        assert!(g.enclosing(Vec3::new(10.5, 5.0, 0.0)).is_none());
    }

    #[test]
    fn pass_radius_quantization_and_filter() {
        assert_eq!(quantize_radius(-1.0), -1.0);
        assert_eq!(quantize_radius(0.004), 0.01);
        assert_eq!(quantize_radius(1000.0), 327.67);
        let l = |r: f32| GraphLink {
            target: 0,
            max_pass_radius: r,
            exposure: 0.0,
            edge_center: Vec3::default(),
            flags: 0,
            edge: (0, 1),
        };
        assert!(l(0.25).passable(DEFAULT_AGENT_RADIUS));
        assert!(!l(0.2).passable(DEFAULT_AGENT_RADIUS));
        assert!(!l(-1.0).passable(0.0));
        assert!(l(0.0).passable(0.0));
    }
}
