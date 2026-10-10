//! `Models/Game/AI/AStar/PathNode`: the reusable A* pathfinder over the navmesh nodes and
//! links of the `.bai` files.
//!
//! C# quirks mirrored: `G` of a neighbour is the distance from the link's *source* node to the
//! goal (not a path length; kept on purpose, `sub_navmesh.md` §5.11), `H` the distance from the
//! target node to the goal; the open set is scanned linearly and the first node with the lowest
//! `F` wins (`OrderBy` is stable); the search stops after `ceil(rawDistance * 10) + 50`
//! iterations and returns the best path so far.
//!
//! Engine behaviour instead of the C# (see `PORTING.md` "geodata deviations"):
//! * the search runs on the merged [`NavGraph`](super::graph::NavGraph), so it crosses sector
//!   and cell borders (the cells a node's triangle touches are loaded before its links are
//!   followed);
//! * the start / goal node is the triangle enclosing the point (`GetEnclosing`), the C#
//!   nearest node only when no triangle encloses it;
//! * links whose `maxPassRadius` is below the agent radius, and blocked links (negative
//!   radius, -1 in the files), are not traversed. [`PathNode::find_path`] uses
//!   [`DEFAULT_AGENT_RADIUS`] (the X2 walkability probe radius 0.25);
//!   [`PathNode::find_path_with_radius`] takes the caller's.

use std::collections::VecDeque;

use crate::systems::skills::controllers::{calculate_distance, Vec3};

use super::bai::{length, sub};
use super::graph::DEFAULT_AGENT_RADIUS;
use super::manager::{douglas_peucker_reduction, AiGeoData};

/// `PathNode` (the AI's pathfinding state).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct PathNode {
    /// `ZoneKey`.
    pub zone_key: u32,
    /// `CurrentTargetPos`.
    pub current_target_pos: Vec3,
    /// `StartPointPos`.
    pub start_point_pos: Vec3,
    /// `EndPointPos`.
    pub end_point_pos: Vec3,
    /// `FoundPath`.
    pub found_path: VecDeque<Vec3>,
    /// `Position`.
    pub position: Vec3,
}

/// One search node.
#[derive(Debug, Clone)]
struct Node {
    current_target_pos: Vec3,
    position: Vec3,
    /// The merged-graph node (none for a start point without navmesh).
    graph: Option<u32>,
    came_from: Option<usize>,
    g: f32,
    h: f32,
}

impl Node {
    fn f(&self) -> f32 {
        self.g + self.h
    }
}

/// Why `FindPath` threw.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PathError(pub String);

impl PathNode {
    /// `FindPath(world, start, goal)` for an NPC-sized agent ([`DEFAULT_AGENT_RADIUS`]).
    pub fn find_path(
        &mut self,
        geo: &AiGeoData,
        start: Vec3,
        goal: Vec3,
    ) -> Result<Vec<Vec3>, PathError> {
        self.find_path_with_radius(geo, start, goal, DEFAULT_AGENT_RADIUS)
    }

    /// `FindPath(world, start, goal)` for an agent of `agent_radius`: links narrower than the
    /// radius (and blocked links) are not traversed.
    pub fn find_path_with_radius(
        &mut self,
        geo: &AiGeoData,
        start: Vec3,
        goal: Vec3,
        agent_radius: f32,
    ) -> Result<Vec<Vec3>, PathError> {
        let mut start = start;
        let mut goal = goal;
        // the navmesh nodes replace the start / end points
        let start_node = geo.resolve_nav_node(self.zone_key, start);
        if let Some((_, p)) = start_node {
            start = p;
        }
        if let Some((_, p)) = geo.resolve_nav_node(self.zone_key, goal) {
            goal = p;
        }
        self.end_point_pos = goal;
        let raw_distance = length(sub(start, goal));

        let mut nodes: Vec<Node> = Vec::new();
        let mut closed: Vec<usize> = Vec::new();
        let mut open: Vec<usize> = Vec::new();
        nodes.push(Node {
            current_target_pos: start,
            position: start,
            graph: start_node.map(|(n, _)| n),
            came_from: None,
            g: 0.0,
            h: calculate_distance(start, self.end_point_pos, false),
        });
        open.push(0);
        let mut max_loops_left = (raw_distance * 10.0).ceil() as i64 + 50;
        while !open.is_empty() {
            max_loops_left -= 1;
            // Step 3: the open node with the lowest F (first of equals)
            let mut best = 0usize;
            for (i, &n) in open.iter().enumerate() {
                if nodes[n].f() < nodes[open[best]].f() {
                    best = i;
                }
            }
            let current = open[best];
            // Step 4
            if nodes[current].position == goal || max_loops_left <= 0 {
                let mut result = path_for_node(&nodes, current);
                result.push(self.end_point_pos);
                let result = douglas_peucker_reduction(&result, 2.0);
                self.position = result[0];
                self.current_target_pos = Vec3::default();
                return Ok(result);
            }
            // Step 5
            open.remove(best);
            closed.push(current);
            // Step 6
            for nb in self.neighbours(geo, &nodes, current, agent_radius)? {
                // Step 7
                if closed.iter().any(|&c| nodes[c].position == nb.position) {
                    continue;
                }
                match open
                    .iter()
                    .copied()
                    .find(|&o| nodes[o].position == nb.position)
                {
                    // Step 8
                    None => {
                        nodes.push(nb);
                        open.push(nodes.len() - 1);
                    }
                    // Step 9
                    Some(o) if nodes[o].g > nb.g => {
                        nodes[o].came_from = Some(current);
                        nodes[o].g = nb.g;
                    }
                    Some(_) => {}
                }
            }
        }
        // Step 10
        Ok(Vec::new())
    }

    /// `GetNeighbours(world, pathNode)` over the merged graph.
    fn neighbours(
        &self,
        geo: &AiGeoData,
        nodes: &[Node],
        idx: usize,
        agent_radius: f32,
    ) -> Result<Vec<Node>, PathError> {
        let mut result = Vec::new();
        let at = nodes[idx].current_target_pos;
        // Check which navmesh file is valid at this position (loads the cell)
        match geo.bai_by_pos(at) {
            Ok(Some(_)) => {}
            Ok(None) => return Ok(result),
            Err(_) => {
                return Err(PathError(
                    "Index was outside the bounds of the array.".into(),
                ))
            }
        }
        let Some(node) = nodes[idx].graph else {
            return Ok(result);
        };
        // the neighbouring cells' copies of a border triangle carry its cross-border links
        geo.ensure_node_cells(node);
        let end = self.end_point_pos;
        let candidates: Vec<(u32, Vec3, Vec3)> = {
            let graph = geo.graph();
            let source_pos = graph.nodes[node as usize].pos;
            graph.nodes[node as usize]
                .links
                .iter()
                .filter(|l| l.passable(agent_radius))
                .map(|l| (l.target, graph.nodes[l.target as usize].pos, source_pos))
                .collect()
        };
        for (target, target_pos, source_pos) in candidates {
            // Checking that the point falls within the forbidden area where it is not allowed
            if geo.check_impossible_walk(target_pos) {
                continue;
            }
            result.push(Node {
                current_target_pos: target_pos,
                position: target_pos,
                graph: Some(target),
                came_from: Some(idx),
                g: length(sub(source_pos, end)),
                h: length(sub(target_pos, end)),
            });
        }
        Ok(result)
    }
}

/// `GetPathForNode(pathNode)`.
fn path_for_node(nodes: &[Node], mut idx: usize) -> Vec<Vec3> {
    let mut result = vec![nodes[idx].position];
    while let Some(prev) = nodes[idx].came_from {
        result.push(nodes[prev].position);
        idx = prev;
    }
    result.reverse();
    result
}

#[cfg(test)]
mod tests {
    use std::collections::HashMap;
    use std::sync::Arc;

    use super::super::bai::test_files::{
        areas_mission, net_mission, net_mission_full, verts_mission, TLink, TNode,
    };
    use super::super::bai::{BaiFiles, UsedAreaNames};
    use super::*;
    use crate::data::world_templates::WorldTemplate;

    /// An in-memory client file set.
    pub struct MemFiles(pub HashMap<String, Vec<u8>>);

    impl BaiFiles for MemFiles {
        fn files_in_directory(&self, dir: &str, pattern: &str) -> Vec<String> {
            let dir = dir.to_lowercase();
            let pat = pattern.to_lowercase().replace('*', "");
            let mut v: Vec<String> = self
                .0
                .keys()
                .filter(|k| k.to_lowercase().starts_with(&dir))
                .filter(|k| {
                    let name = k.rsplit('/').next().unwrap_or("").to_lowercase();
                    let (pre, post) = pat.split_once('.').unwrap_or((&pat, ""));
                    name.starts_with(pre) && name.ends_with(&format!(".{post}"))
                })
                .cloned()
                .collect();
            v.sort();
            v
        }
        fn file_exists(&self, name: &str) -> bool {
            self.0.contains_key(name)
        }
        fn read_file(&self, name: &str) -> Option<Vec<u8>> {
            self.0.get(name).cloned()
        }
    }

    /// A 2x2-cell world whose path block (0, 0) has a 3x3 grid of nodes 10 m apart, linked to
    /// their 4 neighbours, with a forbidden square around the centre node.
    fn geo(with_forbidden: bool) -> AiGeoData {
        let t = WorldTemplate::synthetic(0, "main_world", 2, 2, 100);
        let mut nodes = Vec::new();
        let mut id = 1;
        for x in 0..3 {
            for y in 0..3 {
                nodes.push((id, [10.0 + x as f32 * 10.0, 10.0 + y as f32 * 10.0, 5.0]));
                id += 1;
            }
        }
        let idx = |x: i32, y: i32| (x * 3 + y + 1) as u32;
        let mut links = Vec::new();
        for x in 0..3 {
            for y in 0..3 {
                for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
                    let (nx, ny) = (x + dx, y + dy);
                    if (0..3).contains(&nx) && (0..3).contains(&ny) {
                        links.push((idx(x, y), idx(nx, ny)));
                    }
                }
            }
        }
        let mut files = HashMap::new();
        files.insert(
            "game/worlds/main_world/paths/000_000/netmission0.bai".to_string(),
            net_mission(&nodes, &links),
        );
        if with_forbidden {
            files.insert(
                "game/worlds/main_world/paths/000_000/areasmission0.bai".to_string(),
                areas_mission(&[(
                    "block",
                    vec![
                        [18.0, 18.0, 0.0],
                        [22.0, 18.0, 0.0],
                        [22.0, 22.0, 0.0],
                        [18.0, 22.0, 0.0],
                    ],
                )]),
            );
        }
        AiGeoData::new(
            &t,
            true,
            Arc::new(MemFiles(files)),
            Arc::new(UsedAreaNames::default()),
            None,
        )
    }

    #[test]
    fn finds_a_path_over_the_grid() {
        let g = geo(false);
        let mut pn = PathNode::default();
        let path = pn
            .find_path(&g, Vec3::new(10.0, 10.0, 5.0), Vec3::new(30.0, 30.0, 5.0))
            .unwrap();
        assert_eq!(path.first(), Some(&Vec3::new(10.0, 10.0, 5.0)));
        assert_eq!(path.last(), Some(&Vec3::new(30.0, 30.0, 5.0)));
        assert_eq!(pn.end_point_pos, Vec3::new(30.0, 30.0, 5.0));
        // every kept point is a grid node
        for p in &path {
            assert!(
                (p.x - 10.0) % 10.0 == 0.0 && (p.y - 10.0) % 10.0 == 0.0,
                "{p:?}"
            );
        }
        assert_eq!(g.get_height(Vec3::new(11.0, 11.0, 100.0)), 5.0);
    }

    #[test]
    fn forbidden_areas_are_avoided() {
        let g = geo(true);
        assert!(g.check_impossible_walk(Vec3::new(20.0, 20.0, 5.0)));
        let mut pn = PathNode::default();
        let path = pn
            .find_path(&g, Vec3::new(20.0, 10.0, 5.0), Vec3::new(20.0, 30.0, 5.0))
            .unwrap();
        assert!(!path.contains(&Vec3::new(20.0, 20.0, 5.0)), "{path:?}");
        assert_eq!(path.last(), Some(&Vec3::new(20.0, 30.0, 5.0)));
        // the straight line through the forbidden square is not kept by ReducePath
        let reduced = g
            .reduce_path(
                &[Vec3::new(20.0, 10.0, 5.0), Vec3::new(20.0, 30.0, 5.0)],
                10,
            )
            .unwrap();
        assert_eq!(reduced.len(), 2);
    }

    /// Column x positions of a strip of 10 m squares (y 10..20) across the cell border at
    /// x = 1024; each square is split into two triangles, z = (x - 1000) / 10.
    const XS: [f32; 5] = [1004.0, 1014.0, 1024.0, 1034.0, 1044.0];

    fn z_at(x: f32) -> f32 {
        (x - 1000.0) / 10.0
    }

    /// The verts / net files of a 256 m sector at `offset` holding the given squares;
    /// `between` = the pass radius of the links between consecutive squares.
    fn strip_sector(
        offset: f32,
        squares: &[usize],
        between: impl Fn(usize) -> f32,
    ) -> (Vec<u8>, Vec<u8>) {
        let c0 = squares[0];
        let mut verts = Vec::new();
        for x in &XS[c0..=squares[squares.len() - 1] + 1] {
            for y in [10.0f32, 20.0] {
                verts.push([x - offset, y, z_at(*x)]);
            }
        }
        let vi = |c: usize, r: usize| ((c - c0) * 2 + r) as i32;
        let centroid = |t: [i32; 3]| {
            let p = t.map(|i| verts[i as usize]);
            [
                (p[0][0] + p[1][0] + p[2][0]) / 3.0,
                (p[0][1] + p[1][1] + p[2][1]) / 3.0,
                (p[0][2] + p[1][2] + p[2][2]) / 3.0,
            ]
        };
        let mut nodes = Vec::new();
        let mut links = Vec::new();
        for (k, &c) in squares.iter().enumerate() {
            let ta = [vi(c, 0), vi(c + 1, 0), vi(c + 1, 1)];
            let tb = [vi(c, 0), vi(c + 1, 1), vi(c, 1)];
            let (ia, ib) = (2 * k as i32 + 1, 2 * k as i32 + 2);
            nodes.push(TNode::tri(ia, centroid(ta), ta));
            nodes.push(TNode::tri(ib, centroid(tb), tb));
            links.push(TLink::new(ia as u32, ib as u32, 128.0));
            links.push(TLink::new(ib as u32, ia as u32, 128.0));
            if k + 1 < squares.len() {
                let next_b = ib as u32 + 2;
                links.push(TLink::new(ia as u32, next_b, between(c)));
                links.push(TLink::new(next_b, ia as u32, between(c)));
            }
        }
        (verts_mission(&verts), net_mission_full(&nodes, &links))
    }

    /// Sector 003_000 (cell 0) holds squares 0..=2, sector 004_000 (cell 1) squares 1..=3: the
    /// border squares 1 and 2 are in both, like the engine's overlapping border triangles.
    fn strip_geo(between: impl Fn(usize) -> f32 + Copy) -> AiGeoData {
        let t = WorldTemplate::synthetic(0, "main_world", 2, 2, 100);
        let mut files = HashMap::new();
        for (folder, offset, squares) in [
            ("003_000", 768.0, [0, 1, 2]),
            ("004_000", 1024.0, [1, 2, 3]),
        ] {
            let (v, n) = strip_sector(offset, &squares, between);
            let dir = format!("game/worlds/main_world/paths/{folder}");
            files.insert(format!("{dir}/vertsmission0.bai"), v);
            files.insert(format!("{dir}/netmission0.bai"), n);
        }
        AiGeoData::new(
            &t,
            true,
            Arc::new(MemFiles(files)),
            Arc::new(UsedAreaNames::default()),
            None,
        )
    }

    #[test]
    fn path_crosses_sector_and_cell_borders() {
        let g = strip_geo(|_| 1.0);
        let mut pn = PathNode::default();
        let path = pn
            .find_path(
                &g,
                Vec3::new(1012.0, 11.0, 0.0),
                Vec3::new(1036.0, 18.0, 0.0),
            )
            .unwrap();
        assert_eq!(
            g.loaded_cells(),
            2,
            "the second cell is loaded during the search"
        );
        // start / goal are the enclosing triangles' centroids
        let start = path[0];
        assert!(
            (start.x - (1004.0 + 2.0 * 1014.0) / 3.0).abs() < 1e-3,
            "{path:?}"
        );
        let goal = *path.last().unwrap();
        assert!(
            (goal.x - (2.0 * 1034.0 + 1044.0) / 3.0).abs() < 1e-3,
            "{path:?}"
        );
        assert_eq!(goal, pn.end_point_pos);
        let graph = g.graph();
        // 4 squares, 8 triangles; the border squares are merged
        assert_eq!(graph.nodes.len(), 8);
        assert_eq!(graph.vertices.len(), 10);
        assert_eq!(graph.stats.nodes_merged, 4);
        assert_eq!(graph.stats.vertices_merged, 6);
        // square 1's first triangle links to its twin and to square 2 (read in both sectors)
        let n = graph.enclosing(Vec3::new(1023.0, 11.0, 0.0)).unwrap().0;
        assert_eq!(graph.nodes[n as usize].links.len(), 2);
    }

    #[test]
    fn narrow_and_blocked_links_are_not_traversed() {
        let (a, b) = (Vec3::new(1012.0, 11.0, 0.0), Vec3::new(1036.0, 18.0, 0.0));
        // the links between squares 1 and 2 (at the cell border) are 0.2 m wide
        let g = strip_geo(|c| if c == 1 { 0.2 } else { 1.0 });
        let mut pn = PathNode::default();
        assert!(
            pn.find_path(&g, a, b).unwrap().is_empty(),
            "0.25 m NPC does not fit"
        );
        let path = pn.find_path_with_radius(&g, a, b, 0.1).unwrap();
        assert!(!path.is_empty());
        assert!((path.last().unwrap().x - 1037.333).abs() < 1e-2);
        // -1 = blocked, whatever the radius
        let g = strip_geo(|c| if c == 1 { -1.0 } else { 1.0 });
        assert!(pn.find_path_with_radius(&g, a, b, 0.0).unwrap().is_empty());
    }

    #[test]
    fn height_is_interpolated_on_the_enclosing_triangle() {
        let g = strip_geo(|_| 1.0);
        let z = g.get_height(Vec3::new(1037.5, 15.0, 100.0));
        assert!((z - 3.75).abs() < 1e-4, "{z}");
        let z = g.get_height(Vec3::new(1004.5, 19.0, 0.0));
        assert!((z - 0.45).abs() < 1e-4, "{z}");
        // outside every triangle: the C# nearest node / obstacle vertex (corner 1044, 20)
        assert_eq!(g.get_height(Vec3::new(1050.0, 50.0, 0.0)), z_at(1044.0));
    }
}
