#pragma once
#include <string>
namespace input {
// Main-thread only. Keys are released by a frame deadline, even if the pipe disconnects.
std::string control(void* env, const std::string& command);
void tick(void* env);
void release(void* env);
}
