#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cassert>
#include <cstring>
#include <cstdio>
#include "virtualpak.cpp"

static const unsigned char source[] = {0, 1, 2, 3, 4, 5, 6, 7};
static DWORD __cdecl decode(void*, ULONGLONG offset, void* output, DWORD count) {
    if (offset >= sizeof(source)) return 0;
    DWORD amount = static_cast<DWORD>((std::min)(static_cast<ULONGLONG>(count), sizeof(source) - offset));
    memcpy(output, source + offset, amount);
    return amount;
}
static DWORD __cdecl foreign_read(HANDLE, void*, DWORD) { return 91; }
static DWORD __cdecl foreign_write(HANDLE, const void*, DWORD) { return 92; }
static LONGLONG __cdecl foreign_size(HANDLE) { return 93; }

int main() {
    using namespace virtualpak;
    assert(pak_name("D:/Client/game_pak"));
    assert(pak_name(L"D:\\Client\\GAME_PAK"));
    assert(!pak_name("D:/Client/not_game_pak"));
    assert(!pak_name("D:/Client/game_pak/asset"));
    assert(!pak_name(static_cast<const char*>(nullptr)));
    originalAttributesW = GetFileAttributesW;
    wchar_t executable[MAX_PATH], windows[MAX_PATH];
    assert(GetModuleFileNameW(nullptr, executable, MAX_PATH) != 0);
    assert(GetWindowsDirectoryW(windows, MAX_PATH) != 0);
    std::wstring missingPak = std::wstring(executable) + L".missing-directory\\game_pak";
    assert(GetFileAttributesW(missingPak.c_str()) == INVALID_FILE_ATTRIBUTES);
    assert(attributes_w(missingPak.c_str()) == FILE_ATTRIBUTE_NORMAL);
    assert((attributes_w(missingPak.c_str()) & FILE_ATTRIBUTE_DIRECTORY) == 0);
    assert(attributes_w(windows) == GetFileAttributesW(windows));
    assert((attributes_w(windows) & FILE_ATTRIBUTE_DIRECTORY) != 0);
    assert(attributes_w(executable) == GetFileAttributesW(executable));
    std::wstring missingForeign = std::wstring(executable) + L".missing";
    DWORD expectedAttributes = GetFileAttributesW(missingForeign.c_str());
    DWORD expectedError = GetLastError();
    assert(expectedAttributes == INVALID_FILE_ATTRIBUTES);
    SetLastError(ERROR_SUCCESS);
    assert(attributes_w(missingForeign.c_str()) == expectedAttributes);
    assert(GetLastError() == expectedError);
    originalStat32I64 = _stat32i64;
    struct StatCanary {
        unsigned char before[8];
        LegacyStat32I64 value;
        unsigned char after[8];
    } statProbe;
    static_assert(offsetof(StatCanary, value) == 8, "stat canary alignment");
    memset(&statProbe, 0xCD, sizeof(statProbe));
    logicalSize = 24877137920LL;
    const std::string missingPakA = trace_utf8(missingPak.c_str());
    assert(stat32i64_a(missingPakA.c_str(), &statProbe.value) == 0);
    assert(statProbe.value.st_size == 24877137920LL);
    assert((statProbe.value.st_mode & _S_IFREG) != 0);
    assert((statProbe.value.st_mode & _S_IFDIR) == 0);
    assert((statProbe.value.st_mode & _S_IREAD) != 0);
    assert((statProbe.value.st_mode & _S_IWRITE) == 0);
    assert(statProbe.value.st_nlink == 1);
    for (unsigned char byte : statProbe.before) assert(byte == 0xCD);
    for (unsigned char byte : statProbe.after) assert(byte == 0xCD);
    assert(stat32i64_a(missingPakA.c_str(), nullptr) == -1 && GetLastError() == ERROR_INVALID_PARAMETER);
    assert(stat32i64_a(nullptr, &statProbe.value) == -1 && GetLastError() == ERROR_INVALID_PARAMETER);
    assert(statProbe.value.st_size == 24877137920LL);
    const std::string foreignPaths[] = {trace_utf8(executable), trace_utf8(windows), trace_utf8(missingForeign.c_str())};
    for (const auto& path : foreignPaths) {
        LegacyStat32I64 expected, actual;
        memset(&expected, 0xCD, sizeof(expected));
        memset(&actual, 0xCD, sizeof(actual));
        int expectedResult = _stat32i64(path.c_str(), &expected);
        int expectedErrno = errno;
        DWORD expectedLastError = GetLastError();
        errno = 0;
        SetLastError(ERROR_SUCCESS);
        int actualResult = stat32i64_a(path.c_str(), &actual);
        assert(actualResult == expectedResult);
        assert(memcmp(&actual, &expected, sizeof(actual)) == 0);
        if (expectedResult != 0) {
            assert(errno == expectedErrno);
            assert(GetLastError() == expectedLastError);
        }
    }
    bridgeRead = decode;
    logicalSize = sizeof(source);
    reader = const_cast<unsigned char*>(source);
    originalRead = foreign_read;
    originalWrite = foreign_write;
    originalSize = foreign_size;
    HANDLE a = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HANDLE b = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HANDLE other = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    handles[a] = std::make_shared<File>();
    handles[b] = std::make_shared<File>();
    unsigned char buffer[12] = {};
    assert(read(a, buffer, 3) == 3 && memcmp(buffer, source, 3) == 0);
    assert(read(b, buffer, 2) == 2 && memcmp(buffer, source, 2) == 0);
    LONGLONG offset;
    assert(position(a, &offset) && offset == 3);
    assert(position(b, &offset) && offset == 2);
    assert(seek(a, -2, &offset, FILE_END) && offset == 6);
    assert(read(a, buffer, 12) == 2 && buffer[0] == 6 && buffer[1] == 7);
    assert(read(a, buffer, 12) == 0); // EOF returns zero and does not move the cursor.
    assert(position(a, &offset) && offset == 8);
    assert(!seek(a, -9, &offset, FILE_CURRENT));
    assert(position(a, &offset) && offset == 8);
    assert(!seek(a, 0, &offset, 100));
    assert(seek(a, LLONG_MAX, &offset, FILE_BEGIN));
    assert(!seek(a, 1, &offset, FILE_CURRENT));
    assert(position(a, &offset) && offset == LLONG_MAX);
    assert(read(a, buffer, 1) == 0);
    assert(size(a) == sizeof(source) && size(other) == 93);
    assert(write(a, source, 1) == MAXDWORD && GetLastError() == ERROR_ACCESS_DENIED);
    assert(read(other, buffer, 1) == 91);
    assert(write(other, source, 1) == 92);
    assert(owns_read_function(reinterpret_cast<void*>(&read)));
    assert(owns_close_function(reinterpret_cast<void*>(&close)));
    assert(close(a) && !is_handle(a));
    assert(close(b) && !is_handle(b));
    CloseHandle(other);
    puts("virtual pak independent cursors, seek bounds, EOF, write denial, legacy stat ABI, compressed-only mount attributes and foreign-file forwarding passed");
}
