// Raw game_pak I/O through CrySystem's xlcommon imports; no asset files are changed.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string>
#include <unordered_map>
#include <vector>
#include "pakio.h"
#include "virtualpak.h"

namespace pakio {
typedef DWORD (__cdecl *Read_t)(HANDLE, void*, DWORD);
typedef bool (__cdecl *Close_t)(HANDLE);
static Read_t originalRead;
static Close_t originalClose;
static void** readSlot;
static void** closeSlot;
static SRWLOCK lock = SRWLOCK_INIT;
static bool enabled;
static LARGE_INTEGER frequency;
static std::unordered_map<HANDLE, bool> handles;
struct Sample { LONGLONG offset; DWORD bytes; LONGLONG ticks; DWORD thread; };
static std::vector<Sample> samples;
static ULONGLONG reads, bytes, ticks;
static const size_t maxSamples = 100000;
static BYTE* readAheadInstruction;
static bool optimized;

static BYTE* knownSystem() {
    BYTE* base = reinterpret_cast<BYTE*>(GetModuleHandleW(L"CrySystem.dll"));
    if (!base) return NULL;
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + reinterpret_cast<IMAGE_DOS_HEADER*>(base)->e_lfanew);
    if (nt->FileHeader.TimeDateStamp != 0x543CB856 || nt->OptionalHeader.SizeOfImage != 0x169000) return NULL;
    return base;
}

static bool readAhead(bool small) {
    BYTE* base = knownSystem();
    if (!base) return false;
    // Binary-derived Pack::Read at RVA 14B10 calls the chunk-size calculator
    // (130D0) with push 0x100000 at 14CC4. The calculator already takes max(request,
    // read-ahead), capped to the remaining 1 MB buffer and rounded to sector size.
    // Change only that read-ahead to 64 KB, retaining the original buffer capacity.
    const BYTE expected[] = {0x68,0x00,0x00,0x10,0x00,0x52,0x50,0x8b,0x07,0x50,0x8b,0xc3,0xe8,0xfb,0xe3,0xff,0xff};
    BYTE* instruction = base + 0x14CC4;
    BYTE current[sizeof(expected)];
    memcpy(current, instruction, sizeof(current));
    if (current[3] != 0x10 && current[3] != 0x01) return false;
    current[3] = 0x10;
    if (memcmp(current, expected, sizeof(expected)) != 0) return false;
    // This aligned 16-bit store changes 0x00100000 <-> 0x00010000 atomically.
    // Either value is valid for any worker already entering the original function.
    auto immediate = reinterpret_cast<volatile SHORT*>(instruction + 2);
    DWORD old;
    if (!VirtualProtect(instruction, sizeof(expected), PAGE_EXECUTE_READWRITE, &old)) return false;
    SHORT value = small ? 0x0100 : 0x1000;
    SHORT previous = small ? 0x1000 : 0x0100;
    SHORT observed = InterlockedCompareExchange16(immediate, value, previous);
    FlushInstructionCache(GetCurrentProcess(), instruction, 5);
    VirtualProtect(instruction, sizeof(expected), old, &old);
    if (observed != previous && observed != value) return false;
    readAheadInstruction = instruction;
    optimized = small;
    return true;
}

static bool isPak(HANDLE file) {
    if (virtualpak::is_handle(file)) return true;
    AcquireSRWLockShared(&lock);
    auto found = handles.find(file);
    bool known = found != handles.end();
    bool pak = known && found->second;
    ReleaseSRWLockShared(&lock);
    if (known) return pak;
    wchar_t path[1024];
    DWORD length = GetFinalPathNameByHandleW(file, path, 1024, FILE_NAME_NORMALIZED);
    pak = length > 8 && length < 1024 && (path[length - 9] == L'\\' || path[length - 9] == L'/') &&
          _wcsicmp(path + length - 8, L"game_pak") == 0;
    AcquireSRWLockExclusive(&lock);
    handles[file] = pak;
    ReleaseSRWLockExclusive(&lock);
    return pak;
}

static DWORD __cdecl hookRead(HANDLE file, void* buffer, DWORD count) {
    DWORD previousError = GetLastError();
    AcquireSRWLockShared(&lock);
    bool active = enabled;
    ReleaseSRWLockShared(&lock);
    if (!active || !isPak(file)) {
        SetLastError(previousError);
        return originalRead(file, buffer, count);
    }
    LARGE_INTEGER position = {}, zero = {}, start, end;
    bool positioned = virtualpak::position(file, &position.QuadPart) ||
        SetFilePointerEx(file, zero, &position, FILE_CURRENT) != FALSE;
    SetLastError(previousError);
    QueryPerformanceCounter(&start);
    DWORD result = originalRead(file, buffer, count);
    DWORD error = GetLastError();
    QueryPerformanceCounter(&end);
    AcquireSRWLockExclusive(&lock);
    if (result != MAXDWORD) {
        ++reads;
        bytes += result;
        ticks += end.QuadPart - start.QuadPart;
        if (positioned && samples.size() < maxSamples)
            samples.push_back({position.QuadPart, result, end.QuadPart - start.QuadPart, GetCurrentThreadId()});
    }
    ReleaseSRWLockExclusive(&lock);
    SetLastError(error);
    return result;
}

static bool __cdecl hookClose(HANDLE file) {
    AcquireSRWLockExclusive(&lock);
    handles.erase(file);
    ReleaseSRWLockExclusive(&lock);
    return originalClose(file);
}

static bool exchange(void** slot, void* expected, void* replacement) {
    DWORD old;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &old)) return false;
    void* previous = InterlockedCompareExchangePointer(slot, replacement, expected);
    VirtualProtect(slot, sizeof(void*), old, &old);
    return previous == expected;
}

static void** importSlot(BYTE* base, const char* name) {
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + reinterpret_cast<IMAGE_DOS_HEADER*>(base)->e_lfanew);
    DWORD rva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
    if (!rva) return NULL;
    auto descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + rva);
    for (; descriptor->Name; ++descriptor) {
        if (_stricmp(reinterpret_cast<char*>(base + descriptor->Name), "xlcommon.dll") != 0) continue;
        auto names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->OriginalFirstThunk);
        auto addresses = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++addresses) {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto entry = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
            if (strcmp(reinterpret_cast<char*>(entry->Name), name) == 0)
                return reinterpret_cast<void**>(&addresses->u1.Function);
        }
    }
    return NULL;
}

static bool install() {
    if (readSlot) return true;
    BYTE* base = knownSystem();
    HMODULE xl = GetModuleHandleW(L"xlcommon.dll");
    if (!base || !xl) return false;
    void** read = importSlot(base, "?XlReadFile@@YAIPAX0I@Z");
    void** close = importSlot(base, "?XlCloseFile@@YA_NPAX@Z");
    void* stockRead = GetProcAddress(xl, "?XlReadFile@@YAIPAX0I@Z");
    void* stockClose = GetProcAddress(xl, "?XlCloseFile@@YA_NPAX@Z");
    if (!read || !close || !stockRead || !stockClose) return false;
    if (*read != stockRead && !virtualpak::owns_read_function(*read)) return false;
    if (*close != stockClose && !virtualpak::owns_close_function(*close)) return false;
    originalRead = reinterpret_cast<Read_t>(*read);
    originalClose = reinterpret_cast<Close_t>(*close);
    QueryPerformanceFrequency(&frequency);
    samples.reserve(maxSamples);
    if (!exchange(close, reinterpret_cast<void*>(originalClose), reinterpret_cast<void*>(&hookClose))) return false;
    if (!exchange(read, reinterpret_cast<void*>(originalRead), reinterpret_cast<void*>(&hookRead))) {
        exchange(close, reinterpret_cast<void*>(&hookClose), reinterpret_cast<void*>(originalClose));
        return false;
    }
    readSlot = read;
    closeSlot = close;
    return true;
}

std::string control(const std::string& action) {
    if (action == "optimize" || action == "baseline") {
        if (!readAhead(action == "optimize")) return "2pak read-ahead instruction does not match the known r208022 build";
    } else if (action == "profile") {
        if (!install()) return "2pak I/O imports do not match the known r208022 build";
        AcquireSRWLockExclusive(&lock);
        samples.clear(); reads = bytes = ticks = 0; enabled = true;
        ReleaseSRWLockExclusive(&lock);
    } else if (action == "off") {
        AcquireSRWLockExclusive(&lock); enabled = false; ReleaseSRWLockExclusive(&lock);
    } else if (action != "status" && action != "trace") {
        return "2pak action expects optimize, baseline, profile, status, trace or off";
    }
    std::string out = "0";
    char row[256];
    AcquireSRWLockShared(&lock);
    if (action == "trace") {
        out += "offset,bytes,microseconds,thread\n";
        for (const Sample& sample : samples) {
            _snprintf_s(row, sizeof(row), _TRUNCATE, "%lld,%lu,%.3f,%lu\n", sample.offset,
                sample.bytes, frequency.QuadPart ? sample.ticks * 1e6 / frequency.QuadPart : 0.0, sample.thread);
            out += row;
        }
    } else {
        _snprintf_s(row, sizeof(row), _TRUNCATE,
            "{\"optimized\":%s,\"read_ahead_bytes\":%u,\"profiling\":%s,\"reads\":%llu,\"bytes\":%llu,\"read_ms\":%.3f,\"samples\":%zu,\"truncated\":%s}",
            optimized ? "true" : "false", optimized ? 65536u : 1048576u, enabled ? "true" : "false", reads, bytes,
            frequency.QuadPart ? ticks * 1000.0 / frequency.QuadPart : 0.0, samples.size(),
            reads > samples.size() ? "true" : "false");
        out += row;
    }
    ReleaseSRWLockShared(&lock);
    return out;
}

void remove() {
    if (readAheadInstruction && optimized) readAhead(false);
    AcquireSRWLockExclusive(&lock); enabled = false; ReleaseSRWLockExclusive(&lock);
    if (readSlot) exchange(readSlot, reinterpret_cast<void*>(&hookRead), reinterpret_cast<void*>(originalRead));
    if (closeSlot) exchange(closeSlot, reinterpret_cast<void*>(&hookClose), reinterpret_cast<void*>(originalClose));
}

// Byte parity through the actual mounted client file system, including partial reads,
// sector/read-ahead/1 MB boundaries, backward seeks and EOF. This is diagnostic-only.
std::string verify() {
    BYTE* base = knownSystem();
    if (!base) return "2unknown CrySystem build";
    void* fs = *reinterpret_cast<void**>(base + 0x1523D4);
    if (!fs || *reinterpret_cast<void**>(fs) != base + 0x124674) return "2client file system is not ready";
    auto table = *reinterpret_cast<void***>(fs);
    typedef void* (__thiscall *Open_t)(void*, const char*, const char*, int);
    typedef void (__thiscall *FileClose_t)(void*, void**);
    typedef LONGLONG (__thiscall *Size_t)(void*, void*);
    typedef LONGLONG (__thiscall *FileRead_t)(void*, void*, void*, LONGLONG);
    typedef int (__thiscall *Seek_t)(void*, void**, LONGLONG, int);
    auto open = reinterpret_cast<Open_t>(table[6]);
    auto close = reinterpret_cast<FileClose_t>(table[7]);
    auto size = reinterpret_cast<Size_t>(table[11]);
    auto read = reinterpret_cast<FileRead_t>(table[12]);
    auto seek = reinterpret_cast<Seek_t>(table[21]);
    bool wasOptimized = optimized;
    struct Restore { bool value; ~Restore() { readAhead(value); } } restore = {wasOptimized};
    const char* assets[] = {
        "game/scriptsbin/common.alb",
        "game/objects/characters/nuian/male/face/nu_m_face01_mid.chr",
        "game/db/compact.sqlite3"
    };
    size_t checks = 0;
    for (const char* asset : assets) {
        if (!readAhead(false)) return "2cannot restore baseline read-ahead";
        void* handle = open(fs, asset, "rb", 0);
        if (!handle) return std::string("2cannot open test asset: ") + asset;
        struct File { void* fs; void* h; FileClose_t close; ~File() { close(fs, &h); } } file = {fs, handle, close};
        LONGLONG length = size(fs, handle);
        if (length <= 0) return "2test asset has no data";
        size_t prefix = static_cast<size_t>(length < 2097168 ? length : 2097168);
        std::vector<BYTE> reference(prefix);
        if (read(fs, handle, reference.data(), prefix) != static_cast<LONGLONG>(prefix)) return "2short baseline read";
        const size_t tailSize = static_cast<size_t>(length < 127 ? length : 127);
        std::vector<BYTE> tail(tailSize);
        if (seek(fs, &file.h, length - tailSize, 0) != 0 ||
            read(fs, handle, tail.data(), tailSize) != static_cast<LONGLONG>(tailSize)) return "2baseline tail read failed";
        if (!readAhead(true)) return "2cannot apply optimized read-ahead";
        struct Probe { size_t offset; size_t count; };
        const Probe probes[] = {{0,1},{511,513},{65535,65537},{0,1048577},{1048575,17},{0,17}};
        for (const Probe& probe : probes) {
            if (probe.offset > prefix) continue;
            if (seek(fs, &file.h, probe.offset, 0) != 0) return "2optimized seek failed";
            std::vector<BYTE> buffer(probe.count + 16, 0xCD);
            size_t expected = prefix - probe.offset;
            if (expected > probe.count) expected = probe.count;
            LONGLONG actual = read(fs, handle, buffer.data(), probe.count);
            if (actual != static_cast<LONGLONG>(expected) ||
                memcmp(buffer.data(), reference.data() + probe.offset, expected) != 0)
                return std::string("2optimized bytes differ from baseline: ") + asset;
            for (size_t i = expected; i < buffer.size(); ++i)
                if (buffer[i] != 0xCD) return "2read wrote beyond returned byte count";
            ++checks;
        }
        if (seek(fs, &file.h, length - tailSize, 0) != 0) return "2optimized tail seek failed";
        std::vector<BYTE> buffer(tailSize + 256, 0xCD);
        if (read(fs, handle, buffer.data(), tailSize + 128) != static_cast<LONGLONG>(tailSize) ||
            memcmp(buffer.data(), tail.data(), tailSize) != 0) return "2optimized EOF bytes differ";
        for (size_t i = tailSize; i < buffer.size(); ++i)
            if (buffer[i] != 0xCD) return "2EOF read wrote past returned bytes";
        if (read(fs, handle, buffer.data(), 1) != 0) return "2read past EOF returned data";
        checks += 2;
    }
    char result[160];
    _snprintf_s(result, sizeof(result), _TRUNCATE, "0{\"assets\":3,\"checks\":%zu,\"byte_parity\":true}", checks);
    return result;
}
}
