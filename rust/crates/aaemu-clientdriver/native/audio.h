#pragma once
#include <windows.h>
namespace audio {
// Reject unknown and foreign process identities, including cross-process sessions.
inline bool owns_session(DWORD owner, DWORD current) { return owner != 0 && owner == current; }
bool prepare_start(bool silent);
}
