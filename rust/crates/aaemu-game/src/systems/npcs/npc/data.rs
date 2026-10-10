//! `NpcManager.Load`: NPC templates and their look / equipment / postures / bonuses / buffs,
//! merchant packs (stub: TODO(npcs) — being ported).

use std::collections::HashMap;

use crate::systems::DataLoadCtx;

/// `NpcTemplate` (stub).
#[derive(Debug, Clone, Default)]
pub struct NpcTemplate {
    pub id: u32,
}

/// `NpcManager` tables.
#[derive(Debug, Default)]
pub struct NpcData {
    pub templates: HashMap<u32, NpcTemplate>,
}

impl NpcData {
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }

    /// `NpcManager.Exist`.
    pub fn exist(&self, template_id: u32) -> bool {
        self.templates.contains_key(&template_id)
    }

    /// `NpcManager.GetTemplate`.
    pub fn template(&self, template_id: u32) -> Option<&NpcTemplate> {
        self.templates.get(&template_id)
    }
}
