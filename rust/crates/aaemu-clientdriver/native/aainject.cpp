// ============================================================================
//  aainject.exe - minimal 32-bit LoadLibrary injector for aaemu-clientdriver.
//
//  Usage: aainject <pid> <dll-path>
//
//  A same-bitness (x86) injector so it matches the 32-bit ArcheAge 1.2 client: Windows does
//  not support CreateRemoteThread across the WOW64 boundary, so the 64-bit controller
//  (aaemu-clientdriver) shells out to this when the i686 Rust target is unavailable. Standard
//  OpenProcess + VirtualAllocEx + WriteProcessMemory + CreateRemoteThread(LoadLibraryW); the
//  client's own files are never modified. Exit code 0 = loaded, non-zero = failure (message on
//  stderr).
// ============================================================================
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>

static int fail(const char* what) {
    fprintf(stderr, "aainject: %s failed (Win32 error %lu)\n", what, GetLastError());
    return 1;
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) { fprintf(stderr, "usage: aainject <pid> <dll-path>\n"); return 2; }
    DWORD pid = (DWORD)_wtoi(argv[1]);
    const wchar_t* dll = argv[2];

    wchar_t full[MAX_PATH];
    if (!GetFullPathNameW(dll, MAX_PATH, full, NULL)) return fail("GetFullPathNameW");
    if (GetFileAttributesW(full) == INVALID_FILE_ATTRIBUTES) {
        fprintf(stderr, "aainject: %ls not found\n", full); return 1;
    }

    HANDLE h = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE
                           | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, FALSE, pid);
    if (!h) return fail("OpenProcess");

    BOOL wow = FALSE;
    if (IsWow64Process(h, &wow) && !wow) {
        fprintf(stderr, "aainject: pid %lu is not a 32-bit process\n", pid);
        CloseHandle(h); return 1;
    }

    SIZE_T bytes = (wcslen(full) + 1) * sizeof(wchar_t);
    void* remote = VirtualAllocEx(h, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) { CloseHandle(h); return fail("VirtualAllocEx"); }

    if (!WriteProcessMemory(h, remote, full, bytes, NULL)) {
        VirtualFreeEx(h, remote, 0, MEM_RELEASE); CloseHandle(h); return fail("WriteProcessMemory");
    }

    FARPROC load = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
    if (!load) { VirtualFreeEx(h, remote, 0, MEM_RELEASE); CloseHandle(h); return fail("GetProcAddress"); }

    HANDLE th = CreateRemoteThread(h, NULL, 0, (LPTHREAD_START_ROUTINE)load, remote, 0, NULL);
    if (!th) { VirtualFreeEx(h, remote, 0, MEM_RELEASE); CloseHandle(h); return fail("CreateRemoteThread"); }

    WaitForSingleObject(th, 30000);
    DWORD code = 0; GetExitCodeThread(th, &code);
    VirtualFreeEx(h, remote, 0, MEM_RELEASE);
    CloseHandle(th); CloseHandle(h);
    if (code == 0) { fprintf(stderr, "aainject: LoadLibraryW returned 0 (DLL failed to load)\n"); return 1; }
    printf("aainject: loaded (hmodule %08lX)\n", code);
    return 0;
}
