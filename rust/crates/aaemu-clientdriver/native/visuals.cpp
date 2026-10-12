#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d10_1.h>
#include <d3d11.h>
#include <gdiplus.h>
#include <string>
#include <vector>
#include <atomic>
#include <algorithm>
#include "visuals.h"

namespace visuals {
typedef HRESULT (STDMETHODCALLTYPE *PresentFn)(IDXGISwapChain*, UINT, UINT);
static void** slot;
static PresentFn original;
static SRWLOCK lock = SRWLOCK_INIT;
static HANDLE ready;
static bool requested;
static std::string result;
static volatile LONG frames;
static bool profiling;
static LONGLONG lastFrame;
static double frequency;
static double intervals[2048];
static size_t intervalCount, intervalNext;
static std::atomic<HWND> clientWindow{NULL};
static std::atomic<bool> hidden{false};
static ULONG_PTR gdiplusToken;

static bool exchange(void** p, void* value) {
    DWORD old;
    if (!VirtualProtect(p, sizeof(void*), PAGE_READWRITE, &old)) return false;
    InterlockedExchangePointer(p, value);
    DWORD ignored;
    VirtualProtect(p, sizeof(void*), old, &ignored);
    return true;
}

template<class T> struct Com {
    T* p = NULL;
    ~Com() { if (p) p->Release(); }
    T* operator->() const { return p; }
};

static std::string png(UINT width, UINT height, UINT pitch, const BYTE* pixels, bool rgba) {
    if (!width || !height || width > 8192 || height > 8192) return "2unsupported frame dimensions";
    // GDI+ expects BGRA with a meaningful alpha channel. Swap chain alpha is usually unused.
    std::vector<BYTE> bgra((size_t)width * height * 4);
    for (UINT y = 0; y < height; ++y) for (UINT x = 0; x < width; ++x) {
        const BYTE* src = pixels + (size_t)y * pitch + x * 4;
        BYTE* dst = &bgra[((size_t)y * width + x) * 4];
        dst[0] = src[rgba ? 2 : 0]; dst[1] = src[1]; dst[2] = src[rgba ? 0 : 2]; dst[3] = 255;
    }
    Com<IStream> stream;
    if (FAILED(CreateStreamOnHGlobal(NULL, TRUE, &stream.p))) return "2cannot create PNG stream";
    Gdiplus::Bitmap bitmap(width, height, width * 4, PixelFormat32bppARGB, bgra.data());
    const CLSID encoder = {0x557cf406, 0x1a04, 0x11d3, {0x9a,0x73,0x00,0x00,0xf8,0x1e,0xf3,0x2e}};
    if (bitmap.Save(stream.p, &encoder, NULL) != Gdiplus::Ok) return "2PNG encoding failed";
    STATSTG stat = {};
    if (FAILED(stream->Stat(&stat, STATFLAG_NONAME)) || stat.cbSize.QuadPart > (64u << 20))
        return "2invalid PNG stream size";
    LARGE_INTEGER zero = {};
    stream->Seek(zero, STREAM_SEEK_SET, NULL);
    std::string out((size_t)stat.cbSize.QuadPart + 1, '\0'); out[0] = '0';
    ULONG n = 0;
    if (FAILED(stream->Read(&out[1], (ULONG)stat.cbSize.QuadPart, &n)) || n != stat.cbSize.QuadPart)
        return "2cannot read PNG stream";
    return out;
}

static bool supported(DXGI_FORMAT f) {
    return f == DXGI_FORMAT_R8G8B8A8_UNORM || f == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB ||
           f == DXGI_FORMAT_B8G8R8A8_UNORM || f == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
}
static bool rgba(DXGI_FORMAT f) { return f == DXGI_FORMAT_R8G8B8A8_UNORM || f == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB; }

static std::string read11(IDXGISwapChain* chain, ID3D11Device* device) {
    Com<ID3D11Texture2D> back, resolved, staging;
    Com<ID3D11DeviceContext> context;
    device->GetImmediateContext(&context.p);
    if (FAILED(chain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&back.p))) return "2cannot read DX11 back buffer";
    D3D11_TEXTURE2D_DESC desc; back->GetDesc(&desc);
    if (!supported(desc.Format)) return "2unsupported DX11 pixel format";
    ID3D11Texture2D* source = back.p;
    if (desc.SampleDesc.Count > 1) {
        desc.SampleDesc.Count = 1; desc.SampleDesc.Quality = 0;
        desc.BindFlags = 0; desc.MiscFlags = 0;
        if (FAILED(device->CreateTexture2D(&desc, NULL, &resolved.p))) return "2cannot resolve DX11 frame";
        context->ResolveSubresource(resolved.p, 0, back.p, 0, desc.Format); source = resolved.p;
    }
    desc.Usage = D3D11_USAGE_STAGING; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    desc.BindFlags = 0; desc.MiscFlags = 0;
    if (FAILED(device->CreateTexture2D(&desc, NULL, &staging.p))) return "2cannot allocate DX11 readback";
    context->CopyResource(staging.p, source);
    D3D11_MAPPED_SUBRESOURCE map;
    if (FAILED(context->Map(staging.p, 0, D3D11_MAP_READ, 0, &map))) return "2cannot map DX11 frame";
    std::string out = png(desc.Width, desc.Height, map.RowPitch, (BYTE*)map.pData, rgba(desc.Format));
    context->Unmap(staging.p, 0);
    return out;
}

static std::string read10(IDXGISwapChain* chain, ID3D10Device* device) {
    Com<ID3D10Texture2D> back, resolved, staging;
    if (FAILED(chain->GetBuffer(0, __uuidof(ID3D10Texture2D), (void**)&back.p))) return "2cannot read DX10 back buffer";
    D3D10_TEXTURE2D_DESC desc; back->GetDesc(&desc);
    if (!supported(desc.Format)) return "2unsupported DX10 pixel format";
    ID3D10Texture2D* source = back.p;
    if (desc.SampleDesc.Count > 1) {
        desc.SampleDesc.Count = 1; desc.SampleDesc.Quality = 0;
        desc.BindFlags = 0; desc.MiscFlags = 0;
        if (FAILED(device->CreateTexture2D(&desc, NULL, &resolved.p))) return "2cannot resolve DX10 frame";
        device->ResolveSubresource(resolved.p, 0, back.p, 0, desc.Format); source = resolved.p;
    }
    desc.Usage = D3D10_USAGE_STAGING; desc.CPUAccessFlags = D3D10_CPU_ACCESS_READ;
    desc.BindFlags = 0; desc.MiscFlags = 0;
    if (FAILED(device->CreateTexture2D(&desc, NULL, &staging.p))) return "2cannot allocate DX10 readback";
    device->CopyResource(staging.p, source);
    D3D10_MAPPED_TEXTURE2D map;
    if (FAILED(staging->Map(0, D3D10_MAP_READ, 0, &map))) return "2cannot map DX10 frame";
    std::string out = png(desc.Width, desc.Height, map.RowPitch, (BYTE*)map.pData, rgba(desc.Format));
    staging->Unmap(0);
    return out;
}

static HRESULT STDMETHODCALLTYPE present(IDXGISwapChain* chain, UINT interval, UINT flags) {
    if (!(flags & DXGI_PRESENT_TEST)) {
        DXGI_SWAP_CHAIN_DESC desc;
        if (FAILED(chain->GetDesc(&desc)) || desc.BufferDesc.Width < 64 || desc.BufferDesc.Height < 64)
            return original(chain, interval, flags);
        clientWindow = desc.OutputWindow;
        InterlockedIncrement(&frames);
        AcquireSRWLockExclusive(&lock);
        if (profiling) {
            LARGE_INTEGER now; QueryPerformanceCounter(&now);
            if (lastFrame && frequency>0) {
                intervals[intervalNext]=(now.QuadPart-lastFrame)*1000.0/frequency;
                intervalNext=(intervalNext+1)%2048;
                if(intervalCount<2048) ++intervalCount;
            }
            lastFrame=now.QuadPart;
        }
        if (requested) {
            Com<ID3D11Device> d11; Com<ID3D10Device> d10;
            if (SUCCEEDED(chain->GetDevice(__uuidof(ID3D11Device), (void**)&d11.p))) result = read11(chain, d11.p);
            else if (SUCCEEDED(chain->GetDevice(__uuidof(ID3D10Device), (void**)&d10.p))) result = read10(chain, d10.p);
            else result = "2swap chain is neither DX10 nor DX11";
            requested = false;
            SetEvent(ready);
        }
        ReleaseSRWLockExclusive(&lock);
    }
    HRESULT hr = original(chain, interval, flags);
    // A hidden window is occluded by definition. Let the engine keep producing frames.
    return hidden && hr == DXGI_STATUS_OCCLUDED ? S_OK : hr;
}

bool install() {
    Gdiplus::GdiplusStartupInput gd;
    if (Gdiplus::GdiplusStartup(&gdiplusToken, &gd, NULL) != Gdiplus::Ok) return false;
    ready = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!ready) { Gdiplus::GdiplusShutdown(gdiplusToken); return false; }
    HWND probe = CreateWindowExW(0, L"STATIC", L"aaemu capture probe", WS_POPUP, 0, 0, 2, 2, NULL, NULL, NULL, NULL);
    DXGI_SWAP_CHAIN_DESC desc = {};
    desc.BufferDesc.Width = 2; desc.BufferDesc.Height = 2; desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1; desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 1; desc.OutputWindow = probe; desc.Windowed = TRUE;
    Com<IDXGISwapChain> chain; Com<ID3D11Device> device;
    // Only discover DXGI's shared Present vtable here. A software device avoids waking
    // the GPU driver a second time during startup; captures still use the game's device.
    HRESULT hr = D3D11CreateDeviceAndSwapChain(NULL, D3D_DRIVER_TYPE_WARP, NULL, 0, NULL, 0,
                                              D3D11_SDK_VERSION, &desc, &chain.p, &device.p, NULL, NULL);
    bool ok = false;
    if (SUCCEEDED(hr)) {
        slot = &(*(void***)chain.p)[8]; // IDXGISwapChain::Present; SDK ABI, no client RVAs.
        original = (PresentFn)*slot;
        ok = exchange(slot, (void*)&present);
    }
    DestroyWindow(probe);
    if (!ok) { slot = NULL; CloseHandle(ready); ready = NULL; Gdiplus::GdiplusShutdown(gdiplusToken); }
    return ok;
}

static BOOL CALLBACK findWindow(HWND w, LPARAM) {
    DWORD pid = 0; GetWindowThreadProcessId(w, &pid);
    if (pid == GetCurrentProcessId() && GetWindow(w, GW_OWNER) == NULL && IsWindowVisible(w)) {
        clientWindow = w; return FALSE;
    }
    return TRUE;
}

std::string window(bool visible) {
    if (!clientWindow) EnumWindows(findWindow, 0);
    if (!clientWindow || !IsWindow(clientWindow)) return "2client window is not ready";
    if (!visible) {
        hidden = true; ShowWindow(clientWindow, SW_HIDE);
    } else {
        hidden = false; ShowWindow(clientWindow, SW_SHOWNOACTIVATE);
    }
    return visible ? "0window visible" : "0window hidden (renderer remains active)";
}

std::string capture() {
    if (!slot) return "2DXGI capture hook unavailable (use the DX10/DX11 renderer)";
    AcquireSRWLockExclusive(&lock);
    ResetEvent(ready); result.clear(); requested = true;
    ReleaseSRWLockExclusive(&lock);
    DWORD wait = WaitForSingleObject(ready, 10000);
    AcquireSRWLockExclusive(&lock);
    requested = false;
    std::string out = wait == WAIT_OBJECT_0 ? result : "2no rendered frame in 10 s (minimised, loading, or DX9 renderer)";
    ReleaseSRWLockExclusive(&lock);
    return out;
}

std::string info() {
    char out[128];
    _snprintf_s(out, sizeof(out), _TRUNCATE, " capture=%d frames=%ld hidden=%d", slot != NULL, frames, (int)hidden.load());
    return out;
}

std::string profile(const std::string& action) {
    if(action!="profile" && action!="status" && action!="off") return "2unknown frame profile action";
    AcquireSRWLockExclusive(&lock);
    if(action=="profile") {
        LARGE_INTEGER hz; QueryPerformanceFrequency(&hz); frequency=(double)hz.QuadPart;
        intervalCount=intervalNext=0;lastFrame=0;profiling=true;
    } else if(action=="off") { profiling=false;lastFrame=0; }
    bool active=profiling;
    std::vector<double> samples(intervals,intervals+intervalCount);
    ReleaseSRWLockExclusive(&lock);
    if(samples.empty()) return active?"0{\"active\":true,\"samples\":0}":"0{\"active\":false,\"samples\":0}";
    std::sort(samples.begin(),samples.end());
    double total=0;for(size_t i=0;i<samples.size();++i) total+=samples[i];
    double mean=total/samples.size();
    char out[512];
    _snprintf_s(out,sizeof(out),_TRUNCATE,
        "0{\"active\":%s,\"samples\":%u,\"fps\":%.3f,\"mean_ms\":%.3f,\"p95_ms\":%.3f,\"p99_ms\":%.3f,\"max_ms\":%.3f}",
        active?"true":"false",(unsigned)samples.size(),mean>0?1000.0/mean:0.0,mean,
        samples[(samples.size()-1)*95/100],samples[(samples.size()-1)*99/100],samples.back());
    return out;
}

void remove() {
    if (hidden && clientWindow) window(true);
    if (slot) exchange(slot, (void*)original);
    // The driver keeps a module reference until process exit: an already dispatched virtual
    // call may enter our thunk after the vtable is restored. Never unmap code underneath it.
}
}
