#pragma once
#include <string>

// DXGI back-buffer capture: works with the 1.2 DX10/DX11 renderer, including a hidden window.
namespace visuals {
bool install();
void remove();
std::string capture(); // status byte + PNG bytes (or an error string)
std::string window(bool visible); // called on the client main thread
std::string info();
std::string profile(const std::string& action); // bounded DXGI frame interval samples
}
