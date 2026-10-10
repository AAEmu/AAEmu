//! `PlotManager.Load`: plots, plot events (+ conditions, aoe conditions, effects, next events in
//! position order) and the plot trees (`PlotBuilder.BuildTree`).

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;

/// `Plot`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Plot {
    pub id: u32,
    pub target_type_id: u32,
    /// `EventTemplate` (the event with `position == 1`).
    pub event_id: Option<u32>,
}

/// `PlotCondition`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlotCondition {
    pub id: u32,
    pub not_condition: bool,
    pub kind: i32,
    pub param1: i32,
    pub param2: i32,
    pub param3: i32,
}

/// `PlotEventCondition`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlotEventCondition {
    pub condition: PlotCondition,
    pub position: i32,
    /// `PlotEffectSource`.
    pub source_id: i32,
    /// `PlotEffectTarget`.
    pub target_id: i32,
}

/// `PlotAoeCondition`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlotAoeCondition {
    pub condition: PlotCondition,
    pub position: i32,
}

/// `PlotEventEffect`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlotEventEffect {
    pub position: i32,
    pub source_id: i32,
    pub target_id: i32,
    pub actual_id: u32,
    pub actual_type: String,
}

/// `PlotNextEvent`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlotNextEvent {
    pub id: u32,
    /// `Event.Id`.
    pub event_id: u32,
    pub position: i32,
    pub per_target: bool,
    pub casting: bool,
    pub delay: i32,
    pub speed: i32,
    pub channeling: bool,
    pub casting_inc: i32,
    pub add_anim_cs_time: bool,
    pub casting_delayable: bool,
    pub casting_cancelable: bool,
    pub cancel_on_big_hit: bool,
    pub use_exe_time: bool,
    pub fail: bool,
}

/// `PlotEventTemplate`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct PlotEventTemplate {
    pub id: u32,
    pub plot_id: u32,
    pub position: i32,
    pub source_update_method_id: u32,
    pub target_update_method_id: u32,
    /// `TargetUpdateMethodParam1..9`.
    pub target_update_method_params: [i32; 9],
    pub tickets: i32,
    pub aoe_diminishing: bool,
    pub conditions: Vec<PlotEventCondition>,
    pub aoe_conditions: Vec<PlotAoeCondition>,
    pub effects: Vec<PlotEventEffect>,
    pub next_events: Vec<PlotNextEvent>,
}

/// One node of a `PlotTree` (`PlotNode`): nodes are shared per `PlotNextEvent.Id`, as the C#
/// `existingNextEvents` cache does.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlotNode {
    pub event_id: u32,
    pub parent: Option<usize>,
    /// `ParentNextEvent` (index into the parent event's `next_events`), `None` for the root.
    pub parent_next_event: Option<(u32, usize)>,
    pub children: Vec<usize>,
}

/// `PlotTree` as an arena (node 0 is the root).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct PlotTree {
    pub plot_id: u32,
    pub nodes: Vec<PlotNode>,
}

/// The `LinkedList` insertion of `PlotManager.Load`: before the first node with a greater
/// position, otherwise at the end.
fn insert_by_position<T>(list: &mut Vec<T>, item: T, pos: impl Fn(&T) -> i32) {
    let p = pos(&item);
    match list.iter().position(|x| pos(x) > p) {
        Some(i) => list.insert(i, item),
        None => list.push(item),
    }
}

/// The plot tables (`_plots`, `_eventTemplates`, `_conditions`) and the trees.
#[derive(Debug, Clone, Default)]
pub struct PlotData {
    pub plots: HashMap<u32, Plot>,
    pub events: HashMap<u32, PlotEventTemplate>,
    pub conditions: HashMap<u32, PlotCondition>,
    pub trees: HashMap<u32, PlotTree>,
}

impl PlotData {
    pub fn plot(&self, id: u32) -> Option<&Plot> {
        self.plots.get(&id)
    }

    pub fn event(&self, id: u32) -> Option<&PlotEventTemplate> {
        self.events.get(&id)
    }

    pub fn tree(&self, plot_id: u32) -> Option<&PlotTree> {
        self.trees.get(&plot_id)
    }

    /// The `PlotNextEvent` that leads to `node` (`node.ParentNextEvent`).
    pub fn parent_next_event(&self, tree: &PlotTree, node: usize) -> Option<&PlotNextEvent> {
        let (event, idx) = tree.nodes.get(node)?.parent_next_event?;
        self.events.get(&event)?.next_events.get(idx)
    }

    pub fn load(conn: &Connection) -> rusqlite::Result<Self> {
        let mut d = PlotData::default();
        let dup = |what: &str, id: u32| {
            rusqlite::Error::InvalidParameterName(format!("duplicate {what} {id} (Dictionary.Add)"))
        };
        for_each_row(conn, "SELECT * FROM plots", [], |r| {
            let p = Plot {
                id: r.u32("id")?,
                target_type_id: r.u32("target_type_id")?,
                event_id: None,
            };
            if d.plots.insert(p.id, p.clone()).is_some() {
                return Err(dup("plot", p.id));
            }
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM plot_events", [], |r| {
            let mut params = [0i32; 9];
            for (i, v) in params.iter_mut().enumerate() {
                *v = r.i32(&format!("target_update_method_param{}", i + 1))?;
            }
            let t = PlotEventTemplate {
                id: r.u32("id")?,
                plot_id: r.u32("plot_id")?,
                position: r.i32("position")?,
                source_update_method_id: r.u32("source_update_method_id")?,
                target_update_method_id: r.u32("target_update_method_id")?,
                target_update_method_params: params,
                tickets: r.i32("tickets")?,
                aoe_diminishing: r.bool_str("aoe_diminishing")?,
                ..PlotEventTemplate::default()
            };
            if t.position == 1 {
                if let Some(p) = d.plots.get_mut(&t.plot_id) {
                    p.event_id = Some(t.id);
                }
            }
            let id = t.id;
            if d.events.insert(id, t).is_some() {
                return Err(dup("plot event", id));
            }
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM plot_conditions", [], |r| {
            let c = PlotCondition {
                id: r.u32("id")?,
                not_condition: r.bool_str("not_condition")?,
                kind: r.i32("kind_id")?,
                param1: r.i32("param1")?,
                param2: r.i32("param2")?,
                param3: r.i32("param3")?,
            };
            if d.conditions.insert(c.id, c.clone()).is_some() {
                return Err(dup("plot condition", c.id));
            }
            Ok(())
        })?;
        let missing = |what: &str, id: u32| {
            rusqlite::Error::InvalidParameterName(format!("{what} {id} not found (KeyNotFound)"))
        };
        for_each_row(conn, "SELECT * FROM plot_event_conditions", [], |r| {
            let event_id = r.u32("event_id")?;
            let cond_id = r.u32("condition_id")?;
            let condition = d
                .conditions
                .get(&cond_id)
                .cloned()
                .ok_or_else(|| missing("plot condition", cond_id))?;
            let c = PlotEventCondition {
                condition,
                position: r.i32("position")?,
                source_id: r.i32("source_id")?,
                target_id: r.i32("target_id")?,
            };
            let ev = d
                .events
                .get_mut(&event_id)
                .ok_or_else(|| missing("plot event", event_id))?;
            insert_by_position(&mut ev.conditions, c, |x| x.position);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM plot_aoe_conditions", [], |r| {
            let event_id = r.u32("event_id")?;
            let cond_id = r.u32("condition_id")?;
            let condition = d
                .conditions
                .get(&cond_id)
                .cloned()
                .ok_or_else(|| missing("plot condition", cond_id))?;
            let c = PlotAoeCondition {
                condition,
                position: r.i32("position")?,
            };
            let ev = d
                .events
                .get_mut(&event_id)
                .ok_or_else(|| missing("plot event", event_id))?;
            insert_by_position(&mut ev.aoe_conditions, c, |x| x.position);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM plot_effects", [], |r| {
            let event_id = r.u32("event_id")?;
            let e = PlotEventEffect {
                position: r.i32("position")?,
                source_id: r.i32("source_id")?,
                target_id: r.i32("target_id")?,
                actual_id: r.u32("actual_id")?,
                actual_type: r.string("actual_type")?,
            };
            let ev = d
                .events
                .get_mut(&event_id)
                .ok_or_else(|| missing("plot event", event_id))?;
            insert_by_position(&mut ev.effects, e, |x| x.position);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM plot_next_events", [], |r| {
            let event_id = r.u32("event_id")?;
            let next_id = r.u32("next_event_id")?;
            if !d.events.contains_key(&next_id) {
                return Err(missing("plot event", next_id));
            }
            let n = PlotNextEvent {
                id: r.u32("id")?,
                event_id: next_id,
                position: r.i32("position")?,
                per_target: r.bool_str("per_target")?,
                casting: r.bool_str("casting")?,
                delay: r.i32("delay")?,
                speed: r.i32("speed")?,
                channeling: r.bool_str("channeling")?,
                casting_inc: r.i32("casting_inc")?,
                add_anim_cs_time: r.bool_str("add_anim_cs_time")?,
                casting_delayable: r.bool_str("casting_delayable")?,
                casting_cancelable: r.bool_str("casting_cancelable")?,
                cancel_on_big_hit: r.bool_str("cancel_on_big_hit")?,
                use_exe_time: r.bool_str("use_exe_time")?,
                fail: r.bool_str("fail")?,
            };
            let ev = d
                .events
                .get_mut(&event_id)
                .ok_or_else(|| missing("plot event", event_id))?;
            insert_by_position(&mut ev.next_events, n, |x| x.position);
            Ok(())
        })?;
        let mut trees = HashMap::new();
        for p in d.plots.values() {
            if let Some(root) = p.event_id {
                trees.insert(p.id, d.build_tree(p.id, root));
            }
        }
        d.trees = trees;
        Ok(d)
    }

    /// `PlotBuilder.BuildTree`: depth-first, one node per `PlotNextEvent` id.
    fn build_tree(&self, plot_id: u32, root_event: u32) -> PlotTree {
        let mut tree = PlotTree {
            plot_id,
            nodes: vec![PlotNode {
                event_id: root_event,
                parent: None,
                parent_next_event: None,
                children: Vec::new(),
            }],
        };
        let mut existing: HashMap<u32, usize> = HashMap::new();
        self.build_children(&mut tree, 0, &mut existing);
        tree
    }

    fn build_children(
        &self,
        tree: &mut PlotTree,
        parent: usize,
        existing: &mut HashMap<u32, usize>,
    ) {
        let event_id = tree.nodes[parent].event_id;
        let Some(ev) = self.events.get(&event_id) else {
            return;
        };
        for (idx, next) in ev.next_events.iter().enumerate() {
            if let Some(&node) = existing.get(&next.id) {
                tree.nodes[parent].children.push(node);
            } else {
                let node = tree.nodes.len();
                tree.nodes.push(PlotNode {
                    event_id: next.event_id,
                    parent: Some(parent),
                    parent_next_event: Some((event_id, idx)),
                    children: Vec::new(),
                });
                tree.nodes[parent].children.push(node);
                existing.insert(next.id, node);
                self.build_children(tree, node, existing);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn insertion_keeps_position_order_and_ties_append() {
        let mut v: Vec<(i32, char)> = Vec::new();
        insert_by_position(&mut v, (2, 'a'), |x| x.0);
        insert_by_position(&mut v, (1, 'b'), |x| x.0);
        insert_by_position(&mut v, (2, 'c'), |x| x.0);
        insert_by_position(&mut v, (3, 'd'), |x| x.0);
        assert_eq!(v, vec![(1, 'b'), (2, 'a'), (2, 'c'), (3, 'd')]);
    }

    #[test]
    fn trees_share_nodes_per_next_event() {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(
            "CREATE TABLE plots (id, name, target_type_id);
             INSERT INTO plots VALUES (1, 'p', 0);
             CREATE TABLE plot_events (id, plot_id, position, name, source_update_method_id,
               target_update_method_id, target_update_method_param1, target_update_method_param2,
               target_update_method_param3, tickets, target_update_method_param4,
               target_update_method_param5, target_update_method_param6,
               target_update_method_param7, aoe_diminishing, target_update_method_param8,
               target_update_method_param9);
             INSERT INTO plot_events VALUES (10,1,1,'',1,2,0,0,0,1,0,0,0,0,'f',0,0);
             INSERT INTO plot_events VALUES (11,1,2,'',1,2,0,0,0,1,0,0,0,0,'f',0,0);
             CREATE TABLE plot_conditions (id, not_condition, kind_id, param1, param2, param3);
             CREATE TABLE plot_event_conditions (id, event_id, condition_id, position, source_id, target_id, notify_failure);
             CREATE TABLE plot_aoe_conditions (id, event_id, condition_id, position);
             CREATE TABLE plot_effects (id, event_id, position, source_id, target_id, actual_id, actual_type);
             CREATE TABLE plot_next_events (id, event_id, position, next_event_id, per_target, casting,
               delay, speed, channeling, casting_inc, add_anim_cs_time, casting_delayable,
               casting_cancelable, cancel_on_big_hit, use_exe_time, fail);
             INSERT INTO plot_next_events VALUES (100,10,1,11,'f','f',0,0,'f',0,'f','f','f','f','f','f');
             INSERT INTO plot_next_events VALUES (101,11,1,11,'f','f',500,0,'f',0,'f','f','f','f','f','f');",
        )
        .unwrap();
        let d = PlotData::load(&c).unwrap();
        let t = d.tree(1).unwrap();
        // root -> node(11) -> node(11 via 101) -> (101 already built) itself
        assert_eq!(t.nodes.len(), 3);
        assert_eq!(t.nodes[0].children, vec![1]);
        assert_eq!(t.nodes[1].children, vec![2]);
        assert_eq!(t.nodes[2].children, vec![2]);
        assert_eq!(d.parent_next_event(t, 2).unwrap().delay, 500);
    }
}
