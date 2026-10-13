// A compressed archive is presented as the exact original r208022 game_pak byte stream.
// Only CrySystem's pak I/O and mount attribute imports are redirected. Other files use stock functions.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <climits>
#include <cstddef>
#include <cerrno>
#include <cstdio>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>
#include <sys/types.h>
#include <sys/stat.h>
#include "virtualpak.h"

namespace virtualpak {
typedef HANDLE (__cdecl *OpenA)(const char*, int, int);
typedef HANDLE (__cdecl *OpenW)(const wchar_t*, int, int);
typedef DWORD (__cdecl *Read)(HANDLE, void*, DWORD);
typedef DWORD (__cdecl *Write)(HANDLE, const void*, DWORD);
typedef bool (__cdecl *Seek)(HANDLE, LONGLONG, LONGLONG*, DWORD);
typedef LONGLONG (__cdecl *Size)(HANDLE);
typedef bool (__cdecl *Close)(HANDLE);
typedef bool (__cdecl *ExistsA)(const char*);
typedef bool (__cdecl *ExistsW)(const wchar_t*);
typedef DWORD (WINAPI *AttributesW)(const wchar_t*);
// MSVCR100 _stat32i64: confirmed in the stock CRT export (RVA 5FD86): mode@6,
// nlink@8, rdev@16, 64-bit size@24 and 32-bit times@32/36/40. Default packing is 8.
using LegacyStat32I64 = struct ::_stat32i64;
static_assert(sizeof(LegacyStat32I64) == 48, "legacy stat32i64 ABI size");
static_assert(offsetof(LegacyStat32I64, st_mode) == 6, "legacy stat32i64 mode offset");
static_assert(offsetof(LegacyStat32I64, st_nlink) == 8, "legacy stat32i64 nlink offset");
static_assert(offsetof(LegacyStat32I64, st_rdev) == 16, "legacy stat32i64 rdev offset");
static_assert(offsetof(LegacyStat32I64, st_size) == 24, "legacy stat32i64 size offset");
static_assert(offsetof(LegacyStat32I64, st_atime) == 32, "legacy stat32i64 atime offset");
static_assert(offsetof(LegacyStat32I64, st_mtime) == 36, "legacy stat32i64 mtime offset");
static_assert(offsetof(LegacyStat32I64, st_ctime) == 40, "legacy stat32i64 ctime offset");
typedef int (__cdecl *Stat32I64)(const char*, LegacyStat32I64*);
static OpenA originalOpenA;
static OpenW originalOpenW;
static Read originalRead;
static Write originalWrite;
static Seek originalSeek;
static Size originalSize;
static Close originalClose;
static ExistsA originalExistsA;
static ExistsW originalExistsW;
static AttributesW originalAttributesW;
static Stat32I64 originalStat32I64;

typedef unsigned (__cdecl *Abi)();
typedef void* (__cdecl *BridgeOpen)(const unsigned char*, unsigned);
typedef LONGLONG (__cdecl *BridgeSize)(void*);
typedef DWORD (__cdecl *BridgeRead)(void*, ULONGLONG, void*, DWORD);
typedef unsigned (__cdecl *BridgeError)(char*, unsigned);
static BridgeRead bridgeRead;
static BridgeError bridgeError;
static void* reader;
static LONGLONG logicalSize;
static std::wstring manifestPath;
static HANDLE manifestLock = INVALID_HANDLE_VALUE;
static std::mutex handlesLock;
struct File {
    std::mutex lock;
    LONGLONG offset = 0;
};
static std::unordered_map<HANDLE, std::shared_ptr<File>> handles;
static volatile LONG readCount, openCount;
// Opt-in, bounded startup diagnostics. Shipping launches never create path logs.
static bool traceEnabled;
static std::wstring tracePath;
static std::mutex traceLock;
static volatile LONG tracedOpenA, tracedOpenW, tracedAttributesW, tracedStat32I64;
struct TraceSlot {
    const char* module;
    const char* name;
    void** slot;
    void* original;
    void* replacement;
    void* previous;
};
static std::vector<TraceSlot> traceSlots;

static void trace_line(const std::string& line) {
    if (!traceEnabled || tracePath.empty()) return;
    DWORD savedError = GetLastError();
    {
        std::lock_guard<std::mutex> guard(traceLock);
        HANDLE log = CreateFileW(tracePath.c_str(), FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (log != INVALID_HANDLE_VALUE) {
            char prefix[96];
            _snprintf_s(prefix, sizeof(prefix), _TRUNCATE, "pid=%lu tick=%lu thread=%lu ",
                GetCurrentProcessId(), GetTickCount(), GetCurrentThreadId());
            std::string row = std::string(prefix) + line + "\r\n";
            DWORD written;
            WriteFile(log, row.data(), static_cast<DWORD>(row.size()), &written, nullptr);
            CloseHandle(log);
        }
    }
    SetLastError(savedError);
}

static std::string trace_utf8(const wchar_t* path) {
    if (!path) return "<null>";
    int count = WideCharToMultiByte(CP_UTF8, 0, path, -1, nullptr, 0, nullptr, nullptr);
    if (count <= 0 || count > 32768) return "<invalid-or-too-long>";
    std::vector<char> bytes(count);
    if (!WideCharToMultiByte(CP_UTF8, 0, path, -1, bytes.data(), count, nullptr, nullptr)) return "<conversion-failed>";
    std::string text(bytes.data());
    for (char& c : text) if (c == '\r' || c == '\n') c = ' ';
    return text;
}

static void trace_path(const char* kind, const wchar_t* path, bool match, int flags, LONG number) {
    if (!traceEnabled || number > 16) return;
    DWORD savedError = GetLastError();
    wchar_t normalized[32768];
    DWORD count = path ? GetFullPathNameW(path, 32768, normalized, nullptr) : 0;
    char prefix[128];
    _snprintf_s(prefix, sizeof(prefix), _TRUNCATE, "%s call=%ld match=%d flags=%d path=", kind, number, match ? 1 : 0, flags);
    trace_line(std::string(prefix) + trace_utf8(path) + " normalized=" +
        (count && count < 32768 ? trace_utf8(normalized) : "<unavailable>"));
    SetLastError(savedError);
}

static void trace_path(const char* kind, const char* path, bool match, int flags, LONG number) {
    if (!traceEnabled || number > 16) return;
    DWORD savedError = GetLastError();
    int count = path ? MultiByteToWideChar(CP_UTF8, 0, path, -1, nullptr, 0) : 0;
    std::vector<wchar_t> wide(count > 0 && count <= 32768 ? count : 1, 0);
    if (count > 0 && count <= 32768) MultiByteToWideChar(CP_UTF8, 0, path, -1, wide.data(), count);
    trace_path(kind, path ? wide.data() : nullptr, match, flags, number);
    SetLastError(savedError);
}

static DWORD WINAPI trace_watch(void*) {
    // Observe, never repair, any post-resume IAT rewriting. ReadProcessMemory avoids
    // faulting if a module is unloaded during an early abort. At most 64 bounded polls.
    for (unsigned poll = 0; poll < 64; ++poll) {
        Sleep(100);
        for (auto& item : traceSlots) {
            void* current = nullptr;
            SIZE_T copied = 0;
            if (!ReadProcessMemory(GetCurrentProcess(), item.slot, &current, sizeof(current), &copied) || copied != sizeof(current)) continue;
            if (current == item.previous) continue;
            char row[384];
            _snprintf_s(row, sizeof(row), _TRUNCATE, "IAT_CHANGE poll=%u %s!%s slot=%p original=%p replacement=%p previous=%p current=%p",
                poll, item.module, item.name, item.slot, item.original, item.replacement, item.previous, current);
            trace_line(row);
            item.previous = current;
        }
    }
    return 0;
}

static bool pak_name(const wchar_t* path) {
    if (!path) return false;
    const wchar_t* name = path;
    for (const wchar_t* p = path; *p; ++p) if (*p == L'/' || *p == L'\\') name = p + 1;
    return _wcsicmp(name, L"game_pak") == 0;
}
static bool pak_name(const char* path) {
    if (!path) return false;
    const char* name = path;
    for (const char* p = path; *p; ++p) if (*p == '/' || *p == '\\') name = p + 1;
    return _stricmp(name, "game_pak") == 0;
}
static std::shared_ptr<File> file(HANDLE handle) {
    std::lock_guard<std::mutex> guard(handlesLock);
    auto found = handles.find(handle);
    return found == handles.end() ? std::shared_ptr<File>() : found->second;
}
bool is_handle(HANDLE handle) { return !!file(handle); }
bool position(HANDLE handle, LONGLONG* offset) {
    auto state = file(handle);
    if (!state) return false;
    std::lock_guard<std::mutex> guard(state->lock);
    *offset = state->offset;
    return true;
}
static HANDLE open_virtual() {
    // A real read-only handle preserves Windows file type and prevents replacement while
    // the game uses the manifest. Each TLS pak open gets its own independent cursor.
    HANDLE handle = CreateFileW(manifestPath.c_str(), GENERIC_READ, FILE_SHARE_READ,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) return handle;
    {
        std::lock_guard<std::mutex> guard(handlesLock);
        handles.emplace(handle, std::make_shared<File>());
    }
    InterlockedIncrement(&openCount);
    return handle;
}
static HANDLE __cdecl open_a(const char* path, int flags, int mode) {
    bool match = pak_name(path);
    if (traceEnabled) trace_path("openA", path, match, flags, InterlockedIncrement(&tracedOpenA));
    return match ? open_virtual() : originalOpenA(path, flags, mode);
}
static HANDLE __cdecl open_w(const wchar_t* path, int flags, int mode) {
    bool match = pak_name(path);
    if (traceEnabled) trace_path("openW", path, match, flags, InterlockedIncrement(&tracedOpenW));
    return match ? open_virtual() : originalOpenW(path, flags, mode);
}
static DWORD __cdecl read(HANDLE handle, void* buffer, DWORD count) {
    auto state = file(handle);
    if (!state) return originalRead(handle, buffer, count);
    std::lock_guard<std::mutex> guard(state->lock);
    DWORD result = bridgeRead(reader, static_cast<ULONGLONG>(state->offset), buffer, count);
    if (result == MAXDWORD) {
        SetLastError(ERROR_READ_FAULT);
        return result;
    }
    state->offset += result;
    InterlockedIncrement(&readCount);
    return result;
}
static DWORD __cdecl write(HANDLE handle, const void* buffer, DWORD count) {
    if (!is_handle(handle)) return originalWrite(handle, buffer, count);
    SetLastError(ERROR_ACCESS_DENIED);
    return MAXDWORD;
}
static bool __cdecl seek(HANDLE handle, LONGLONG delta, LONGLONG* result, DWORD origin) {
    auto state = file(handle);
    if (!state) return originalSeek(handle, delta, result, origin);
    std::lock_guard<std::mutex> guard(state->lock);
    LONGLONG base;
    switch (origin) {
    case FILE_BEGIN: base = 0; break;
    case FILE_CURRENT: base = state->offset; break;
    case FILE_END: base = logicalSize; break;
    default: SetLastError(ERROR_INVALID_PARAMETER); return false;
    }
    if ((delta < 0 && delta < -base) || (delta > 0 && base > LLONG_MAX - delta)) {
        SetLastError(ERROR_NEGATIVE_SEEK);
        return false;
    }
    state->offset = base + delta;
    if (result) *result = state->offset;
    return true;
}
static LONGLONG __cdecl size(HANDLE handle) {
    return is_handle(handle) ? logicalSize : originalSize(handle);
}
static bool __cdecl close(HANDLE handle) {
    bool owned;
    {
        std::lock_guard<std::mutex> guard(handlesLock);
        owned = handles.erase(handle) != 0;
    }
    return owned ? CloseHandle(handle) != FALSE : originalClose(handle);
}
static bool __cdecl exists_a(const char* path) {
    return pak_name(path) ? true : originalExistsA(path);
}
static bool __cdecl exists_w(const wchar_t* path) {
    return pak_name(path) ? true : originalExistsW(path);
}
static DWORD WINAPI attributes_w(const wchar_t* path) {
    // r208022 CrySystem mount (RVA DBE0) checks this before Pack::Open. A compressed-only
    // installation has no physical game_pak, but its virtual source is a regular file.
    bool match = pak_name(path);
    if (traceEnabled) trace_path("attrsW", path, match, 0, InterlockedIncrement(&tracedAttributesW));
    return match ? FILE_ATTRIBUTE_NORMAL : originalAttributesW(path);
}
static int __cdecl stat32i64_a(const char* path, LegacyStat32I64* output) {
    if (!output || !path) {
        errno = EINVAL;
        SetLastError(ERROR_INVALID_PARAMETER);
        return -1;
    }
    bool match = pak_name(path);
    if (traceEnabled) trace_path("stat32i64", path, match, 0, InterlockedIncrement(&tracedStat32I64));
    if (!match) return originalStat32I64(path, output);
    // ANSI mount (CrySystem RVA D750) tests the CRT regular-file bit before Pack::Open.
    // Populate only the exact legacy structure; no physical game_pak placeholder exists.
    memset(output, 0, sizeof(*output));
    output->st_mode = _S_IFREG | _S_IREAD;
    output->st_nlink = 1;
    output->st_size = logicalSize;
    return 0;
}
bool owns_read_function(void* function) { return function == reinterpret_cast<void*>(&read); }
bool owns_close_function(void* function) { return function == reinterpret_cast<void*>(&close); }

struct Import {
    const char* name;
    void* replacement;
    void** original;
    bool required;
    const char* module = "xlcommon.dll";
    void** slot = nullptr;
};
static bool install(BYTE* base) {
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + reinterpret_cast<IMAGE_DOS_HEADER*>(base)->e_lfanew);
    if (nt->FileHeader.TimeDateStamp != 0x543CB856 || nt->OptionalHeader.SizeOfImage != 0x169000) return false;
    Import imports[] = {
        {"?XlOpenFile@@YAPAXPBDHH@Z", reinterpret_cast<void*>(&open_a), reinterpret_cast<void**>(&originalOpenA), true},
        {"?XlOpenFile@@YAPAXPBGHH@Z", reinterpret_cast<void*>(&open_w), reinterpret_cast<void**>(&originalOpenW), false},
        {"?XlReadFile@@YAIPAX0I@Z", reinterpret_cast<void*>(&read), reinterpret_cast<void**>(&originalRead), true},
        {"?XlWriteFile@@YAIPAXPBXI@Z", reinterpret_cast<void*>(&write), reinterpret_cast<void**>(&originalWrite), false},
        {"?XlSetFilePointer@@YA_NPAX_JPA_JI@Z", reinterpret_cast<void*>(&seek), reinterpret_cast<void**>(&originalSeek), true},
        {"?XlGetFileSize@@YA_JPAX@Z", reinterpret_cast<void*>(&size), reinterpret_cast<void**>(&originalSize), true},
        {"?XlCloseFile@@YA_NPAX@Z", reinterpret_cast<void*>(&close), reinterpret_cast<void**>(&originalClose), true},
        {"?XlFileExists@@YA_NPBD@Z", reinterpret_cast<void*>(&exists_a), reinterpret_cast<void**>(&originalExistsA), false},
        {"?XlFileExists@@YA_NPBG@Z", reinterpret_cast<void*>(&exists_w), reinterpret_cast<void**>(&originalExistsW), false},
        {"GetFileAttributesW", reinterpret_cast<void*>(&attributes_w), reinterpret_cast<void**>(&originalAttributesW), true, "KERNEL32.dll"},
        {"_stat32i64", reinterpret_cast<void*>(&stat32i64_a), reinterpret_cast<void**>(&originalStat32I64), true, "MSVCR100.dll"},
    };
    auto descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);
    for (; descriptor->Name; ++descriptor) {
        auto names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->OriginalFirstThunk);
        auto addresses = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++addresses) {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto entry = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
            for (auto& item : imports) if (_stricmp(item.module, reinterpret_cast<char*>(base + descriptor->Name)) == 0 &&
                strcmp(item.name, reinterpret_cast<char*>(entry->Name)) == 0) {
                item.slot = reinterpret_cast<void**>(&addresses->u1.Function);
                *item.original = *item.slot;
            }
        }
    }
    for (const auto& item : imports) if (item.required && !item.slot) return false;
    std::vector<Import*> installed;
    for (auto& item : imports) {
        if (!item.slot) continue;
        DWORD protect;
        if (!VirtualProtect(item.slot, sizeof(void*), PAGE_READWRITE, &protect)) {
            // Still suspended: rollback any partial installation before returning failure.
            for (auto* previous : installed) {
                DWORD old;
                if (VirtualProtect(previous->slot, sizeof(void*), PAGE_READWRITE, &old)) {
                    InterlockedExchangePointer(previous->slot, *previous->original);
                    VirtualProtect(previous->slot, sizeof(void*), old, &old);
                }
            }
            return false;
        }
        InterlockedExchangePointer(item.slot, item.replacement);
        VirtualProtect(item.slot, sizeof(void*), protect, &protect);
        installed.push_back(&item);
    }
    if (traceEnabled) {
        char row[384];
        _snprintf_s(row, sizeof(row), _TRUNCATE, "installed base=%p timestamp=%08lx image_size=%08lx",
            base, nt->FileHeader.TimeDateStamp, nt->OptionalHeader.SizeOfImage);
        trace_line(row);
        for (const auto& item : imports) {
            if (!item.slot) continue;
            _snprintf_s(row, sizeof(row), _TRUNCATE, "IAT %s!%s slot=%p original=%p replacement=%p current=%p",
                item.module, item.name, item.slot, *item.original, item.replacement, *item.slot);
            trace_line(row);
            traceSlots.push_back({item.module, item.name, item.slot, *item.original, item.replacement, *item.slot});
        }
        HANDLE watcher = CreateThread(nullptr, 0, trace_watch, nullptr, 0, nullptr);
        if (watcher) CloseHandle(watcher);
    }
    return true;
}

bool prepare_start() {
    int count = 0;
    LPWSTR* args = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!args) return false;
    for (int i = 1; i < count; ++i) if (wcscmp(args[i], L"-aaemu-pak-manifest") == 0) {
        if (i + 1 < count) manifestPath = args[++i];
        else { LocalFree(args); return false; }
    }
    LocalFree(args);
    if (manifestPath.empty()) return true;
    wchar_t driver[MAX_PATH], client[MAX_PATH];
    HMODULE self = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&prepare_start), &self)) return false;
    DWORD driverLength = GetModuleFileNameW(self, driver, MAX_PATH);
    DWORD clientLength = GetModuleFileNameW(nullptr, client, MAX_PATH);
    if (!driverLength || driverLength >= MAX_PATH || !clientLength || clientLength >= MAX_PATH) return false;
    wchar_t* separator = wcsrchr(driver, L'\\');
    wchar_t* clientSeparator = wcsrchr(client, L'\\');
    if (!separator || !clientSeparator) return false;
    *(separator + 1) = 0;
    *(clientSeparator + 1) = 0;
    wchar_t traceFlag[2] = {};
    traceEnabled = GetEnvironmentVariableW(L"AAEMU_PAK_TRACE", traceFlag, 2) == 1 && traceFlag[0] == L'1';
    if (traceEnabled) {
        tracePath = std::wstring(driver) + L"virtualpak.log";
        trace_line("startup manifest=" + trace_utf8(manifestPath.c_str()));
    }
    std::wstring bridgePath = std::wstring(driver) + L"aaemu_pak.dll";
#ifdef AADRIVER_PLAYER
    // The GUI launcher extracts its embedded runtime into a private helper
    // directory. The compressed bridge ships beside the client root instead.
    bridgePath = std::wstring(client) + L"..\\aaemu_pak.dll";
#endif
    HMODULE bridge = LoadLibraryExW(bridgePath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!bridge) return false;
    auto abi = reinterpret_cast<Abi>(GetProcAddress(bridge, "aaemu_pak_abi"));
    auto open = reinterpret_cast<BridgeOpen>(GetProcAddress(bridge, "aaemu_pak_open"));
    auto getSize = reinterpret_cast<BridgeSize>(GetProcAddress(bridge, "aaemu_pak_size"));
    bridgeRead = reinterpret_cast<BridgeRead>(GetProcAddress(bridge, "aaemu_pak_read_at"));
    bridgeError = reinterpret_cast<BridgeError>(GetProcAddress(bridge, "aaemu_pak_error"));
    if (!abi || abi() != 1 || !open || !getSize || !bridgeRead || !bridgeError) return false;
    int bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, manifestPath.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (bytes <= 1) return false;
    std::vector<unsigned char> utf8(bytes);
    if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, manifestPath.c_str(), -1,
        reinterpret_cast<char*>(utf8.data()), bytes, nullptr, nullptr)) return false;
    // Acquire the native share-read lock before constructing the Rust snapshot. Otherwise
    // an updater could publish between bridgeOpen and this lock, mixing two generations.
    manifestLock = CreateFileW(manifestPath.c_str(), GENERIC_READ, FILE_SHARE_READ,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (manifestLock == INVALID_HANDLE_VALUE) return false;
    reader = open(utf8.data(), static_cast<unsigned>(bytes - 1));
    if (!reader || (logicalSize = getSize(reader)) < 0) return false;
    // CrySystem is normally loaded dynamically by the executable. Load the same stock
    // module while suspended so no FAT/header read can race hook installation.
    std::wstring systemPath = std::wstring(client) + L"CrySystem.dll";
    HMODULE system = LoadLibraryExW(systemPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    return system && install(reinterpret_cast<BYTE*>(system));
}
std::string info() {
    if (!reader) return " virtual_pak=0";
    char buffer[160];
    _snprintf_s(buffer, sizeof(buffer), _TRUNCATE, " virtual_pak=1 pak_size=%lld pak_opens=%ld pak_reads=%ld",
        logicalSize, openCount, readCount);
    return buffer;
}
std::string error() {
    char buffer[1024] = {};
    if (bridgeError && bridgeError(buffer, sizeof(buffer)) != 0) return buffer;
    return "Windows error " + std::to_string(GetLastError());
}
}
