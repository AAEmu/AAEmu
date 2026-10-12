// Guard/rollback tests with a relocated synthetic instruction fixture. No game needed.
#include "startup.cpp"
#include <stdio.h>
#include <stdlib.h>

static void check(bool condition) {
    if (!condition) { fputs("startup patch test failed\n", stderr); exit(1); }
}

int main() {
    BYTE* fixture = static_cast<BYTE*>(VirtualAlloc(NULL, 0x24000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    check(fixture != NULL);
    BYTE* code = fixture + startup::processImmediate - 1;
    memcpy(code, startup::expected, sizeof(startup::expected));
    DWORD processSlot = (DWORD)fixture + 0x108734, threadSlot = (DWORD)fixture + 0x108738;
    memcpy(code + 13, &processSlot, 4); memcpy(code + 27, &threadSlot, 4);
    check(startup::matches(fixture));
    BYTE saved[sizeof(startup::expected)]; memcpy(saved, code, sizeof(saved));
    code[0] ^= 1; check(!startup::matches(fixture)); code[0] ^= 1;
    code[13] ^= 1; check(!startup::matches(fixture)); code[13] ^= 1;
    check(startup::exchange(fixture, true));
    check(*reinterpret_cast<SHORT*>(fixture + startup::processImmediate) == NORMAL_PRIORITY_CLASS);
    check(fixture[startup::threadImmediate] == THREAD_PRIORITY_NORMAL);
    check(fixture[startup::threadImmediate + 1] == 0xff);
    check(!startup::exchange(fixture, true)); // Do not overwrite an unexpected state.
    check(startup::exchange(fixture, false));
    check(memcmp(saved, code, sizeof(saved)) == 0);
    fixture[startup::threadImmediate] = 1; // A foreign patch must survive failed installation.
    check(!startup::exchange(fixture, true));
    check(*reinterpret_cast<SHORT*>(fixture + startup::processImmediate) == 0x0100);
    check(fixture[startup::threadImmediate] == 1);
    check(!startup::install()); // Absent engine: refuse to patch.
    VirtualFree(fixture, 0, MEM_RELEASE);
    puts("startup patch guards, priorities, relocation, restoration and rollback passed");
}
