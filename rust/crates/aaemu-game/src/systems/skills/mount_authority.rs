//! Client-side mate cast authority. Skill eligibility and slave seat mapping are separate rules.

use crate::systems::vehicles::data::attach_point;
use crate::world::{MateRef, PlayerRef};

pub(super) fn may_use_mate(player: &PlayerRef, mate: &MateRef) -> bool {
    let (point, riding, parent) = {
        let player = player.data();
        (
            player.attached_point,
            player.is_riding,
            player.unit.transform.parent_obj_id,
        )
    };
    let mate_data = mate.data();
    if mate_data.owner_obj_id == player.obj_id {
        return true; // Pet commands also work while the owner is unmounted.
    }
    riding
        && point != attach_point::NONE
        && parent == mate.obj_id
        && mate_data
            .passenger(point)
            .is_some_and(|seat| seat.obj_id == player.obj_id)
}
