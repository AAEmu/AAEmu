//! `Models/Game/AI/AStar/PathNode`: the reusable A* pathfinder over the navmesh nodes and
//! links of the `.bai` files.
//!
//! C# quirks mirrored: `G` of a neighbour is the distance from the link's *source* node to the
//! goal (not a path length), `H` the distance from the target node to the goal; the open set is
//! scanned linearly and the first node with the lowest `F` wins (`OrderBy` is stable); the search
//! stops after `ceil(rawDistance * 10) + 50` iterations and returns the best path so far.

use std::collections::VecDeque;

use crate::systems::skills::controllers::{calculate_distance, Vec3};

use super::bai::{length, sub};
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
    /// `FindPath(world, start, goal)`.
    pub fn find_path(
        &mut self,
        geo: &AiGeoData,
        start: Vec3,
        goal: Vec3,
    ) -> Result<Vec<Vec3>, PathError> {
        let mut start = start;
        let mut goal = goal;
        // the nearest geodata points replace the start / end points
        let pos_start = geo.find_closest_to_the_current(self.zone_key, start);
        if let Some((b, n)) = &pos_start {
            start = b.node(*n).pos;
        }
        if let Some((b, n)) = geo.find_closest_to_the_current(self.zone_key, goal) {
            goal = b.node(n).pos;
        }
        self.end_point_pos = goal;
        let raw_distance = length(sub(start, goal));

        let mut nodes: Vec<Node> = Vec::new();
        let mut closed: Vec<usize> = Vec::new();
        let mut open: Vec<usize> = Vec::new();
        nodes.push(Node {
            current_target_pos: pos_start.as_ref().map_or(start, |(b, n)| b.node(*n).pos),
            position: start,
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
            for nb in self.neighbours(geo, &nodes, current)? {
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

    /// `GetNeighbours(world, pathNode)`.
    fn neighbours(
        &self,
        geo: &AiGeoData,
        nodes: &[Node],
        idx: usize,
    ) -> Result<Vec<Node>, PathError> {
        let mut result = Vec::new();
        let at = nodes[idx].current_target_pos;
        // Check which navmesh file is valid at this position
        let bai = match geo.bai_by_pos(at) {
            Ok(Some(b)) => b,
            Ok(None) => return Ok(result),
            Err(_) => {
                return Err(PathError(
                    "Index was outside the bounds of the array.".into(),
                ))
            }
        };
        // Find the nearest node
        let Some(nearest) = bai.find_closest_net_mission_node(at) else {
            return Ok(result);
        };
        let end = self.end_point_pos;
        for link in geo.available_points(&bai, nearest) {
            let reader = &bai.net_mission_readers[nearest.reader];
            let (Some(target), Some(source)) = (link.target, link.source) else {
                return Err(PathError(
                    "Object reference not set to an instance of an object.".into(),
                ));
            };
            let target_pos = reader.nodes[target].pos;
            let source_pos = reader.nodes[source].pos;
            // Checking that the point falls within the forbidden area where it is not allowed
            if geo.check_impossible_walk(target_pos) {
                continue;
            }
            result.push(Node {
                current_target_pos: target_pos,
                position: target_pos,
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

    use super::super::bai::test_files::{areas_mission, net_mission};
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
}
