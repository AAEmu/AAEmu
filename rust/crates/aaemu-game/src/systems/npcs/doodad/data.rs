//! `DoodadManager.Load` tables (stub: TODO(npcs) — being ported).

use std::collections::HashMap;

use crate::systems::DataLoadCtx;

/// `DoodadTemplate` (stub).
#[derive(Debug, Clone, Default)]
pub struct DoodadTemplate {
    pub id: u32,
}

#[derive(Debug, Default)]
pub struct DoodadData {
    pub templates: HashMap<u32, DoodadTemplate>,
}

impl DoodadData {
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }

    /// `DoodadManager.Exist`.
    pub fn exist(&self, template_id: u32) -> bool {
        self.templates.contains_key(&template_id)
    }
}
