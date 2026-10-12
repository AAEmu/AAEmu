#pragma once
#include <windows.h>
#include <string>

namespace virtualpak {
// Opt-in startup only. The bridge and manifest are validated before client resume.
bool prepare_start();
bool is_handle(HANDLE handle);
bool position(HANDLE handle, LONGLONG* offset);
bool owns_read_function(void* function);
bool owns_close_function(void* function);
std::string info();
std::string error();
}
