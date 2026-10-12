#pragma once
#include <string>

namespace pakio {
std::string control(const std::string& action);
std::string verify(); // Main thread only: compare real engine reads under both profiles.
void remove();
}
