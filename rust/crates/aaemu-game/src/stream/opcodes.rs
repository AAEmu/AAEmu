//! Stream opcodes for `client_12_r208022` (`CTOffsets` / `TCOffsets`).

/// C->S (`Core/Packets/C2S/CTOffsets.cs`). `0x00b` has no packet in 1.2.
pub mod ct {
    pub const CT_JOIN: u16 = 0x001;
    pub const CT_REQUEST_CELL: u16 = 0x002;
    pub const CT_REQUEST_EMBLEM: u16 = 0x003;
    pub const CT_CANCEL_CELL: u16 = 0x004;
    pub const CT_CONTINUE: u16 = 0x005;
    pub const CT_UCC_COMPLEX: u16 = 0x006;
    pub const CT_UCC_STRING: u16 = 0x007;
    pub const CT_UCC_POSITION: u16 = 0x008;
    pub const CT_UCC_CHARACTER_NAME: u16 = 0x009;
    pub const CT_QUERY_CHAR_NAME: u16 = 0x00a;
    pub const CT_UPLOAD_EMBLEM_STREAM: u16 = 0x00c;
    pub const CT_EMBLEM_STREAM_UPLOAD_STATUS: u16 = 0x00d;
    pub const CT_START_UPLOAD_EMBLEM_STREAM: u16 = 0x00e;
    pub const CT_EMBLEM_STREAM_DOWNLOAD_STATUS: u16 = 0x00f;
    pub const CT_ITEM_UCC: u16 = 0x010;
    pub const CT_EMBLEM_PART_DOWNLOADED: u16 = 0x011;
    pub const CT_UCC_COMPLEX_CHECK_VALID: u16 = 0x012;
}

/// S->C (`Core/Packets/S2C/TCOffsets.cs`).
pub mod tc {
    pub const TC_JOIN_RESPONSE: u16 = 0x001;
    pub const TC_DOODAD_STREAM: u16 = 0x002;
    pub const TC_DOODAD_IDS: u16 = 0x003;
    pub const TC_DOWNLOAD_EMBLEM: u16 = 0x004;
    pub const TC_UCC_COMPLEX: u16 = 0x005;
    pub const TC_UCC_STRING: u16 = 0x006;
    pub const TC_UCC_POSITION: u16 = 0x007;
    pub const TC_UCC_CHAR_NAME: u16 = 0x008;
    pub const TC_CHAR_NAME_QUERIED: u16 = 0x009;
    pub const TC_EMBLEM_STREAM_RECV_STATUS: u16 = 0x00a;
    pub const TC_EMBLEM_STREAM_SEND_STATUS: u16 = 0x00b;
    pub const TC_EMBLEM_STREAM_DOWNLOAD: u16 = 0x00c;
    pub const TC_ITEM_UCC_DATA: u16 = 0x00d;
    pub const TC_HOUSE_FARM: u16 = 0x00e;
    pub const TC_UCC_COMPLEX_CHECK_VALID: u16 = 0x00f;
}
