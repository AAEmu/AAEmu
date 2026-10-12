// Launch-only policy for this injected process. Never obtains endpoint volume or
// writes another process's audio session. The default process session is muted
// before the launcher resumes the client; notifications cover other sessions and
// profile-driven unmute requests for the lifetime of the process.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmdeviceapi.h>
#include <audiopolicy.h>
#include <vector>
#include <string>
#include "audio.h"

namespace audio {
static const GUID context = {0xb0d27164,0x1234,0x40eb,{0x88,0x63,0x33,0xd1,0x12,0x20,0x80,0x22}};

class Session : public IAudioSessionEvents {
    volatile LONG refs = 1;
    ISimpleAudioVolume* volume;
    static DWORD WINAPI reapply(LPVOID p) {
        Session* self = static_cast<Session*>(p);
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (SUCCEEDED(hr)) self->volume->SetMute(TRUE, &context);
        self->Release();
        if (SUCCEEDED(hr)) CoUninitialize();
        return 0;
    }
public:
    explicit Session(ISimpleAudioVolume* v) : volume(v) { volume->AddRef(); }
    ~Session() { volume->Release(); }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG n = InterlockedDecrement(&refs); if (!n) delete this; return n;
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** p) override {
        if (!p) return E_POINTER; *p = nullptr;
        if (iid != __uuidof(IUnknown) && iid != __uuidof(IAudioSessionEvents)) return E_NOINTERFACE;
        *p = static_cast<IAudioSessionEvents*>(this); AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnSimpleVolumeChanged(float, BOOL mute, LPCGUID) override {
        if (mute) return S_OK;
        AddRef();
        if (QueueUserWorkItem(reapply,this,WT_EXECUTEDEFAULT)) return S_OK;
        Release(); return HRESULT_FROM_WIN32(GetLastError());
    }
    HRESULT STDMETHODCALLTYPE OnDisplayNameChanged(LPCWSTR, LPCGUID) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnIconPathChanged(LPCWSTR, LPCGUID) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnChannelVolumeChanged(DWORD, float[], DWORD, LPCGUID) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnGroupingParamChanged(LPCGUID, LPCGUID) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnStateChanged(AudioSessionState) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnSessionDisconnected(AudioSessionDisconnectReason) override { return S_OK; }
};

class Watcher : public IAudioSessionNotification {
    volatile LONG refs = 1;
    bool silent;
    CRITICAL_SECTION lock;
    std::vector<std::wstring> sessions;
    struct Work { Watcher* self; IAudioSessionControl* control; };
    static DWORD WINAPI created(LPVOID p) {
        Work* work = static_cast<Work*>(p);
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (SUCCEEDED(hr)) work->self->initialize(work->control);
        work->control->Release(); work->self->Release(); delete work;
        if (SUCCEEDED(hr)) CoUninitialize();
        return 0;
    }
public:
    explicit Watcher(bool s) : silent(s) { InitializeCriticalSection(&lock); }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG n = InterlockedDecrement(&refs); if (!n) delete this; return n;
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** p) override {
        if (!p) return E_POINTER; *p = nullptr;
        if (iid != __uuidof(IUnknown) && iid != __uuidof(IAudioSessionNotification)) return E_NOINTERFACE;
        *p = static_cast<IAudioSessionNotification*>(this); AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnSessionCreated(IAudioSessionControl* control) override {
        // WASAPI forbids registering another callback inside this callback.
        // Retain the COM objects and perform setup on an MTA worker instead.
        AddRef(); control->AddRef();
        Work* work = new Work{this,control};
        if (QueueUserWorkItem(created,work,WT_EXECUTEDEFAULT)) return S_OK;
        control->Release(); Release(); delete work; return HRESULT_FROM_WIN32(GetLastError());
    }
    HRESULT initialize(IAudioSessionControl* control) {
        IAudioSessionControl2* control2 = nullptr;
        HRESULT hr = control->QueryInterface(__uuidof(IAudioSessionControl2), reinterpret_cast<void**>(&control2));
        if (FAILED(hr)) return hr;
        DWORD pid = 0; hr = control2->GetProcessId(&pid); control2->Release();
        // AUDCLNT_S_NO_SINGLE_PROCESS is a positive status that reports only
        // the first PID of a shared session. Require exact S_OK, not SUCCEEDED.
        if (hr != S_OK || !owns_session(pid, GetCurrentProcessId())) return S_OK;
        // Each new session is initialized once. In sound mode, later manual
        // mute changes must survive duplicate device/session notifications.
        hr = control->QueryInterface(__uuidof(IAudioSessionControl2), reinterpret_cast<void**>(&control2));
        if (FAILED(hr)) return hr;
        LPWSTR id = nullptr; hr = control2->GetSessionInstanceIdentifier(&id); control2->Release();
        if (FAILED(hr)) return hr;
        EnterCriticalSection(&lock);
        for (const auto& previous : sessions) if (previous == id) {
            LeaveCriticalSection(&lock); CoTaskMemFree(id); return S_OK;
        }
        ISimpleAudioVolume* volume = nullptr;
        hr = control->QueryInterface(__uuidof(ISimpleAudioVolume), reinterpret_cast<void**>(&volume));
        Session* events = nullptr;
        if (SUCCEEDED(hr)) {
            hr = volume->SetMute(silent ? TRUE : FALSE, &context);
            if (SUCCEEDED(hr) && silent) {
                events = new Session(volume);
                hr = control->RegisterAudioSessionNotification(events);
            }
            volume->Release();
        }
        // The registration requires a retained caller reference. Keep listeners
        // alive until process exit; detached drivers also retain the module.
        if (FAILED(hr) && events) events->Release();
        if (SUCCEEDED(hr)) sessions.emplace_back(id);
        LeaveCriticalSection(&lock); CoTaskMemFree(id);
        return hr;
    }
};

class Devices : public IMMNotificationClient {
    volatile LONG refs = 1;
    IMMDeviceEnumerator* enumerator;
    CRITICAL_SECTION lock;
    std::vector<std::wstring> ids;
    // Registrations and COM objects remain alive for this process's lifetime.
    std::vector<IAudioSessionManager2*> managers;
    Watcher* watcher;
    bool silent;
    struct Work { Devices* self; std::wstring id; };
    static DWORD WINAPI changed(LPVOID p) {
        Work* work = static_cast<Work*>(p);
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (SUCCEEDED(hr)) work->self->attach_id(work->id.c_str());
        work->self->Release(); delete work;
        if (SUCCEEDED(hr)) CoUninitialize();
        return 0;
    }
    HRESULT queue(LPCWSTR id) {
        AddRef(); Work* work = new Work{this,id};
        if (QueueUserWorkItem(changed,work,WT_EXECUTEDEFAULT)) return S_OK;
        Release(); delete work; return HRESULT_FROM_WIN32(GetLastError());
    }
public:
    Devices(IMMDeviceEnumerator* e, bool s) : enumerator(e), watcher(new Watcher(s)), silent(s) { enumerator->AddRef(); InitializeCriticalSection(&lock); }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG n = InterlockedDecrement(&refs); if (!n) delete this; return n;
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** p) override {
        if (!p) return E_POINTER; *p = nullptr;
        if (iid != __uuidof(IUnknown) && iid != __uuidof(IMMNotificationClient)) return E_NOINTERFACE;
        *p = static_cast<IMMNotificationClient*>(this); AddRef(); return S_OK;
    }
    HRESULT attach(IMMDevice* device) {
        IMMEndpoint* endpoint = nullptr;
        HRESULT hr = device->QueryInterface(__uuidof(IMMEndpoint), reinterpret_cast<void**>(&endpoint));
        if (FAILED(hr)) return hr;
        EDataFlow flow = eAll; hr = endpoint->GetDataFlow(&flow); endpoint->Release();
        if (FAILED(hr) || flow != eRender) return hr; // Never touches capture sessions.
        LPWSTR id = nullptr; hr = device->GetId(&id); if (FAILED(hr)) return hr;
        EnterCriticalSection(&lock);
        for (const auto& prior : ids) if (prior == id) {
            LeaveCriticalSection(&lock); CoTaskMemFree(id); return S_OK;
        }
        IAudioSessionManager2* manager = nullptr;
        hr = device->Activate(__uuidof(IAudioSessionManager2), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(&manager));
        IAudioSessionEnumerator* sessions = nullptr;
        if (SUCCEEDED(hr)) hr = manager->GetSessionEnumerator(&sessions);
        if (SUCCEEDED(hr)) hr = manager->RegisterSessionNotification(watcher);
        // GUID_NULL and flags=0 select this process's default session. Muting it
        // before resume prevents a first buffer escaping a notification callback.
        ISimpleAudioVolume* volume = nullptr;
        if (SUCCEEDED(hr)) hr = manager->GetSimpleAudioVolume(nullptr, 0, &volume);
        if (SUCCEEDED(hr)) hr = volume->SetMute(silent ? TRUE : FALSE, &context);
        if (volume) volume->Release();
        int count = 0;
        if (SUCCEEDED(hr)) hr = sessions->GetCount(&count);
        for (int i = 0; SUCCEEDED(hr) && i < count; ++i) {
            IAudioSessionControl* control = nullptr;
            hr = sessions->GetSession(i, &control);
            if (SUCCEEDED(hr)) { hr = watcher->initialize(control); control->Release(); }
        }
        if (sessions) sessions->Release();
        if (SUCCEEDED(hr)) { ids.emplace_back(id); managers.push_back(manager); }
        else if (manager) { manager->UnregisterSessionNotification(watcher); manager->Release(); }
        LeaveCriticalSection(&lock); CoTaskMemFree(id); return hr;
    }
    HRESULT attach_id(LPCWSTR id) {
        IMMDevice* device = nullptr; HRESULT hr = enumerator->GetDevice(id, &device);
        if (FAILED(hr)) return hr;
        DWORD state = 0; hr = device->GetState(&state);
        if (SUCCEEDED(hr) && state == DEVICE_STATE_ACTIVE) hr = attach(device);
        device->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE OnDeviceAdded(LPCWSTR id) override { return queue(id); }
    HRESULT STDMETHODCALLTYPE OnDeviceStateChanged(LPCWSTR id, DWORD state) override {
        return state == DEVICE_STATE_ACTIVE ? queue(id) : S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnDefaultDeviceChanged(EDataFlow flow, ERole, LPCWSTR id) override {
        return flow == eRender && id ? queue(id) : S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnDeviceRemoved(LPCWSTR) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnPropertyValueChanged(LPCWSTR, const PROPERTYKEY) override { return S_OK; }
};

bool prepare_start(bool silent) {
    if (FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED))) return false;
    IMMDeviceEnumerator* enumerator = nullptr;
    HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL,
        __uuidof(IMMDeviceEnumerator), reinterpret_cast<void**>(&enumerator));
    if (FAILED(hr)) return false;
    Devices* devices = new Devices(enumerator, silent);
    hr = enumerator->RegisterEndpointNotificationCallback(devices);
    IMMDeviceCollection* collection = nullptr;
    if (SUCCEEDED(hr)) hr = enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &collection);
    UINT count = 0;
    if (SUCCEEDED(hr)) hr = collection->GetCount(&count);
    for (UINT i = 0; SUCCEEDED(hr) && i < count; ++i) {
        IMMDevice* device = nullptr; hr = collection->Item(i, &device);
        if (SUCCEEDED(hr)) { hr = devices->attach(device); device->Release(); }
    }
    if (collection) collection->Release();
    enumerator->Release();
    return SUCCEEDED(hr);
}
}
