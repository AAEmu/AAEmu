//! `Models/Game/AI/v2/Controls/**` (`AiPathHandler`, `AiPathPoint`, `AiPathPointAction`) and
//! `Core/Managers/AiPathsManager` (`Data/Path/*.path`, cached by file name).

use std::collections::{HashMap, VecDeque};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use crate::systems::skills::controllers::{calculate_distance, Vec3};

/// `AiPathPointAction`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AiPathPointAction {
    /// No additional actions for this point.
    None,
    /// Disables movement path loop (default behavior).
    DisableLoop,
    /// Enables movement path loop.
    EnableLoop,
    /// Changes movement speed to be used when following the path.
    Speed,
    /// Stance to be used when following the path.
    ActorFlags,
    /// Inserted by RunCommandSet to mark that it needs to return there.
    ReturnToCommandSet,
    /// An undefined numeric value (`Enum.TryParse` accepts it; `RunCurrentPath` throws).
    Other(i64),
}

impl AiPathPointAction {
    /// `Enum.TryParse<AiPathPointAction>(text, ignoreCase: true)`: names or numeric values.
    pub fn parse(text: &str) -> Option<Self> {
        let all = [
            Self::None,
            Self::DisableLoop,
            Self::EnableLoop,
            Self::Speed,
            Self::ActorFlags,
            Self::ReturnToCommandSet,
        ];
        let t = text.trim();
        if let Ok(n) = t.parse::<i64>() {
            // Enum.TryParse accepts any integer (undefined values parse too).
            return Some(
                usize::try_from(n)
                    .ok()
                    .and_then(|i| all.get(i).copied())
                    .unwrap_or(Self::Other(n)),
            );
        }
        all.into_iter()
            .find(|a| format!("{a:?}").eq_ignore_ascii_case(t))
    }
}

/// `AiPathPoint`.
#[derive(Debug, Clone, PartialEq)]
pub struct AiPathPoint {
    pub position: Vec3,
    pub action: AiPathPointAction,
    pub param: String,
}

/// `AiPathHandler`.
#[derive(Debug, Clone)]
pub struct AiPathHandler {
    /// Loaded AI path points.
    pub ai_path_points: Vec<AiPathPoint>,
    /// Queue of locations to go to next.
    pub ai_path_points_remaining: VecDeque<AiPathPoint>,
    /// Needs to be set to true to trigger initial loading into the queue.
    pub ai_path_looping: bool,
    /// Speed multiplier when moving on the path.
    pub ai_path_speed: f32,
    /// Stance to use when moving on the path; 5-walk, 4-run, 3-stand still.
    pub ai_path_actor_flags: u8,
    /// Currently targeted position for path movement (`Vector3.Zero` = none).
    pub target_position: Vec3,
}

impl Default for AiPathHandler {
    fn default() -> Self {
        Self {
            ai_path_points: Vec::new(),
            ai_path_points_remaining: VecDeque::new(),
            ai_path_looping: true,
            ai_path_speed: 1.0,
            ai_path_actor_flags: 4,
            target_position: Vec3::default(),
        }
    }
}

/// What one `RunCurrentPath` step asks its owner to do.
#[derive(Debug, Clone, PartialEq)]
pub enum PathStep {
    /// `Owner.GoToRunCommandSet(); return true;`
    ReturnToCommandSet,
    /// Move towards `target` with `distance` (already speed / delta scaled) and the path's
    /// actor flags, then update the idle position; the bool is the return value.
    Move {
        target: Vec3,
        speed: f32,
        actor_flags: u8,
        result: bool,
    },
    /// Nothing to move; the bool is the return value (`HasUnhandledPathMovementData()`).
    Idle(bool),
    /// `throw new NotSupportedException(...)` for an action that cannot be handled.
    Unsupported(String),
}

fn is_zero(v: Vec3) -> bool {
    v.x == 0.0 && v.y == 0.0 && v.z == 0.0
}

impl AiPathHandler {
    /// The bookkeeping half of `RunCurrentPath(delta)`: refills / dequeues and tells the caller
    /// what to do. `owner_pos` = `Owner.Owner.Transform.World.Position`, `template_scale` =
    /// `Owner.Owner.Template.Scale`.
    pub fn run_current_path(&mut self, owner_pos: Vec3, template_scale: f32) -> PathStep {
        // Queue empty? refill!
        if self.ai_path_points_remaining.is_empty()
            && !self.ai_path_points.is_empty()
            && self.ai_path_looping
        {
            self.ai_path_looping = false;
            self.ai_path_points_remaining
                .extend(self.ai_path_points.iter().cloned());
        }
        // Are we there yet?
        if !is_zero(self.target_position)
            && calculate_distance(self.target_position, owner_pos, true) < template_scale
        {
            self.target_position = Vec3::default();
        }
        // No current target? Set it!
        if is_zero(self.target_position) {
            if let Some(next) = self.ai_path_points_remaining.pop_front() {
                match next.action {
                    AiPathPointAction::None => {}
                    AiPathPointAction::DisableLoop => self.ai_path_looping = false,
                    AiPathPointAction::EnableLoop => self.ai_path_looping = true,
                    AiPathPointAction::Speed => {
                        if let Some(s) = parse_invariant_f32(&next.param) {
                            self.ai_path_speed = s;
                        }
                    }
                    AiPathPointAction::ActorFlags => {
                        if let Ok(f) = next.param.trim().parse::<u8>() {
                            self.ai_path_actor_flags = f;
                        }
                    }
                    AiPathPointAction::ReturnToCommandSet => return PathStep::ReturnToCommandSet,
                    AiPathPointAction::Other(v) => {
                        return PathStep::Unsupported(format!(
                            "Not supported nextPos.Action value: {v}"
                        ))
                    }
                }
                // Set next move point if it's not zero
                if !is_zero(next.position) {
                    self.target_position = next.position;
                }
            }
        }
        if !is_zero(self.target_position) {
            return PathStep::Move {
                target: self.target_position,
                speed: self.ai_path_speed,
                actor_flags: self.ai_path_actor_flags,
                result: self.has_unhandled_path_movement_data(),
            };
        }
        PathStep::Idle(self.has_unhandled_path_movement_data())
    }

    /// `HasUnhandledPathMovementData()`.
    pub fn has_unhandled_path_movement_data(&self) -> bool {
        !self.ai_path_points_remaining.is_empty()
            || self.ai_path_looping
            || !is_zero(self.target_position)
    }

    /// `HasPathMovementData()`.
    pub fn has_path_movement_data(&self) -> bool {
        !self.ai_path_points_remaining.is_empty()
            || (self.ai_path_looping && !self.ai_path_points.is_empty())
    }
}

/// `float.TryParse(text, CultureInfo.InvariantCulture)`.
fn parse_invariant_f32(text: &str) -> Option<f32> {
    let t = text.trim();
    if t.is_empty() {
        return None;
    }
    t.parse::<f32>().ok()
}

/// `float.TryParse(text)` with the server's (invariant) culture; failures give 0.
fn parse_coord(text: &str) -> f32 {
    parse_invariant_f32(text).unwrap_or(0.0)
}

/// `AiPathsManager`: path files cached by name.
#[derive(Debug, Default)]
pub struct AiPathsManager {
    folder: Mutex<Option<PathBuf>>,
    cache: Mutex<HashMap<String, Arc<Vec<AiPathPoint>>>>,
}

/// One `.path` file (`File.ReadAllLines` + the per-line parse).
pub fn parse_path_file(text: &str) -> Vec<AiPathPoint> {
    let mut res = Vec::new();
    for line in text.lines() {
        let columns: Vec<&str> = line.split('|').collect();
        if columns.len() != 5 {
            continue;
        }
        let action = AiPathPointAction::parse(columns[0]).unwrap_or(AiPathPointAction::None);
        res.push(AiPathPoint {
            position: Vec3::new(
                parse_coord(columns[1]),
                parse_coord(columns[2]),
                parse_coord(columns[3]),
            ),
            action,
            param: columns[4].to_string(),
        });
    }
    res
}

impl AiPathsManager {
    /// Sets `Data/Path` (the C# `Path.Combine("Data", "Path")` relative to the working dir).
    pub fn set_folder(&self, folder: impl AsRef<Path>) {
        *self.folder.lock().expect("paths poisoned") = Some(folder.as_ref().to_path_buf());
    }

    /// `LoadAiPathPoints(aiPathFileName)`: the cached points, or the file's (cached when the
    /// file exists), or nothing.
    pub fn load_ai_path_points(&self, name: &str) -> Arc<Vec<AiPathPoint>> {
        let mut cache = self.cache.lock().expect("paths poisoned");
        if let Some(p) = cache.get(name) {
            return p.clone();
        }
        let Some(folder) = self.folder.lock().expect("paths poisoned").clone() else {
            return Arc::new(Vec::new());
        };
        let file = folder.join(format!("{name}.path"));
        let Ok(bytes) = std::fs::read(&file) else {
            return Arc::new(Vec::new());
        };
        let text = String::from_utf8_lossy(&bytes);
        let text = text.strip_prefix('\u{feff}').unwrap_or(&text);
        let points = Arc::new(parse_path_file(text));
        cache.insert(name.to_string(), points.clone());
        points
    }

    /// Installs a path directly (tests).
    pub fn insert(&self, name: &str, points: Vec<AiPathPoint>) {
        self.cache
            .lock()
            .expect("paths poisoned")
            .insert(name.to_string(), Arc::new(points));
    }

    /// `Load()`: caches every `*.path` file of the folder (sub folders excluded). Returns the
    /// number of non-empty files.
    pub fn load(&self) -> usize {
        let Some(folder) = self.folder.lock().expect("paths poisoned").clone() else {
            return 0;
        };
        let Ok(entries) = std::fs::read_dir(&folder) else {
            tracing::error!("Could not read path folder {}", folder.display());
            return 0;
        };
        let mut names: Vec<String> = entries
            .filter_map(Result::ok)
            .filter(|e| e.file_type().is_ok_and(|t| t.is_file()))
            .filter_map(|e| {
                let p = e.path();
                let is_path = p
                    .extension()
                    .is_some_and(|x| x.eq_ignore_ascii_case("path"));
                is_path.then(|| p.file_stem()?.to_str().map(str::to_string))?
            })
            .collect();
        names.sort();
        let loaded = names
            .iter()
            .filter(|n| !self.load_ai_path_points(n).is_empty())
            .count();
        tracing::info!("Loaded {loaded} path files from {}", folder.display());
        loaded
    }

    /// `ClearCache()`.
    pub fn clear_cache(&self) {
        self.cache.lock().expect("paths poisoned").clear();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn path_file_lines() {
        let p = parse_path_file(
            "None|100.5|200|30|\nSpeed|0|0|0|3\nbogus line\nACTORFLAGS|1|2|3|5\nEnableLoop|x|1|2|\n7|1|1|1|\n",
        );
        assert_eq!(p.len(), 5);
        assert_eq!(p[0].position, Vec3::new(100.5, 200.0, 30.0));
        assert_eq!(p[1].action, AiPathPointAction::Speed);
        assert_eq!(p[1].param, "3");
        assert_eq!(p[2].action, AiPathPointAction::ActorFlags);
        assert_eq!(p[3].action, AiPathPointAction::EnableLoop);
        assert_eq!(p[3].position, Vec3::new(0.0, 1.0, 2.0));
        // an undefined numeric action parses (and throws when reached)
        assert_eq!(p[4].action, AiPathPointAction::Other(7));
    }

    #[test]
    fn run_current_path_walks_and_loops() {
        let mut h = AiPathHandler {
            ai_path_points: vec![
                AiPathPoint {
                    position: Vec3::new(10.0, 0.0, 0.0),
                    action: AiPathPointAction::Speed,
                    param: "2.5".into(),
                },
                AiPathPoint {
                    position: Vec3::new(20.0, 0.0, 0.0),
                    action: AiPathPointAction::DisableLoop,
                    param: String::new(),
                },
            ],
            ..AiPathHandler::default()
        };
        // first call refills the queue and targets the first point
        let s = h.run_current_path(Vec3::new(0.0, 0.0, 0.0), 1.0);
        assert_eq!(
            s,
            PathStep::Move {
                target: Vec3::new(10.0, 0.0, 0.0),
                speed: 2.5,
                actor_flags: 4,
                result: true
            }
        );
        assert!(!h.ai_path_looping);
        // reaching the point (closer than Template.Scale) moves on
        let s = h.run_current_path(Vec3::new(9.5, 0.0, 0.0), 1.0);
        assert!(matches!(s, PathStep::Move { target, .. } if target == Vec3::new(20.0, 0.0, 0.0)));
        // last point reached: nothing left, no loop
        let s = h.run_current_path(Vec3::new(20.0, 0.0, 0.0), 1.0);
        assert_eq!(s, PathStep::Idle(false));
        assert!(!h.has_path_movement_data());
    }
}
