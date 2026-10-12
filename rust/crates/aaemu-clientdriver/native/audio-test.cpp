// No game or audible samples: check session ownership and durable mute callbacks.
#include "audio.cpp"
#include <audioclient.h>
#ifndef AUDCLNT_S_NO_SINGLE_PROCESS
#define AUDCLNT_S_NO_SINGLE_PROCESS AUDCLNT_SUCCESS(0x00d)
#endif
#include <stdio.h>
#include <stdlib.h>
static void check(bool value) { if (!value) { fputs("audio policy test failed\n", stderr); exit(1); } }
class Volume : public ISimpleAudioVolume {
public:
    volatile LONG muted = FALSE;
    volatile LONG writes = 0;
    ULONG STDMETHODCALLTYPE AddRef() override { return 1; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void**) override { return E_NOINTERFACE; }
    HRESULT STDMETHODCALLTYPE SetMasterVolume(float, LPCGUID) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetMasterVolume(float*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetMute(BOOL value, LPCGUID) override { InterlockedExchange(&muted,value); InterlockedIncrement(&writes); return S_OK; }
    HRESULT STDMETHODCALLTYPE GetMute(BOOL* value) override { *value = muted; return S_OK; }
};
static void await_writes(Volume& volume,LONG expected) {
    for (int i=0;i<1000 && volume.writes<expected;++i) Sleep(1);
    check(volume.writes==expected);
}
class Control : public IAudioSessionControl2 {
    DWORD pid;
    std::wstring id;
    HRESULT identifier(LPWSTR* value) {
        *value = static_cast<LPWSTR>(CoTaskMemAlloc((id.size()+1)*sizeof(wchar_t)));
        if (!*value) return E_OUTOFMEMORY;
        wcscpy_s(*value,id.size()+1,id.c_str()); return S_OK;
    }
public:
    Volume volume;
    HRESULT identity_result = S_OK;
    IAudioSessionEvents* events = nullptr;
    Control(DWORD p,LPCWSTR name) : pid(p),id(name) {}
    ULONG STDMETHODCALLTYPE AddRef() override { return 1; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** p) override {
        if (iid == __uuidof(ISimpleAudioVolume)) { *p = &volume; return S_OK; }
        if (iid == __uuidof(IAudioSessionControl2)) { *p = this; return S_OK; }
        *p = nullptr; return E_NOINTERFACE;
    }
    HRESULT STDMETHODCALLTYPE GetState(AudioSessionState*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetDisplayName(LPWSTR*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetDisplayName(LPCWSTR,LPCGUID) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetIconPath(LPWSTR*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetIconPath(LPCWSTR,LPCGUID) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetGroupingParam(GUID*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetGroupingParam(LPCGUID,LPCGUID) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE RegisterAudioSessionNotification(IAudioSessionEvents* e) override { events = e; return S_OK; }
    HRESULT STDMETHODCALLTYPE UnregisterAudioSessionNotification(IAudioSessionEvents*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetSessionIdentifier(LPWSTR* p) override { return identifier(p); }
    HRESULT STDMETHODCALLTYPE GetSessionInstanceIdentifier(LPWSTR* p) override { return identifier(p); }
    HRESULT STDMETHODCALLTYPE GetProcessId(DWORD* p) override { *p = pid; return identity_result; }
    HRESULT STDMETHODCALLTYPE IsSystemSoundsSession() override { return S_FALSE; }
    HRESULT STDMETHODCALLTYPE SetDuckingPreference(BOOL) override { return E_NOTIMPL; }
};
class Enumerator : public IMMDeviceEnumerator {
public:
    ULONG STDMETHODCALLTYPE AddRef() override { return 1; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID,void**) override { return E_NOINTERFACE; }
    HRESULT STDMETHODCALLTYPE EnumAudioEndpoints(EDataFlow,DWORD,IMMDeviceCollection**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetDefaultAudioEndpoint(EDataFlow,ERole,IMMDevice**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetDevice(LPCWSTR,IMMDevice**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE RegisterEndpointNotificationCallback(IMMNotificationClient*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE UnregisterEndpointNotificationCallback(IMMNotificationClient*) override { return E_NOTIMPL; }
};
class Device : public IMMDevice, public IMMEndpoint {
    EDataFlow flow;
public:
    int activations = 0, id_reads = 0;
    explicit Device(EDataFlow f) : flow(f) {}
    ULONG STDMETHODCALLTYPE AddRef() override { return 1; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** p) override {
        if (iid == __uuidof(IMMEndpoint)) { *p = static_cast<IMMEndpoint*>(this); return S_OK; }
        *p = nullptr; return E_NOINTERFACE;
    }
    HRESULT STDMETHODCALLTYPE Activate(REFIID,DWORD,PROPVARIANT*,void**) override { ++activations; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE OpenPropertyStore(DWORD,IPropertyStore**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetId(LPWSTR* id) override {
        ++id_reads; *id = static_cast<LPWSTR>(CoTaskMemAlloc(16*sizeof(wchar_t)));
        if (!*id) return E_OUTOFMEMORY;
        wcscpy_s(*id,16,L"mock-render"); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetState(DWORD* state) override { *state=DEVICE_STATE_ACTIVE; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetDataFlow(EDataFlow* f) override { *f=flow; return S_OK; }
};
int main() {
    check(audio::owns_session(12,12));
    check(!audio::owns_session(13,12));
    check(!audio::owns_session(0,12));
    check(!audio::owns_session(0,0));
    Volume volume;
    audio::Session* policy = new audio::Session(&volume);
    check(SUCCEEDED(policy->OnSimpleVolumeChanged(0.7f,FALSE,nullptr)));
    await_writes(volume,1);
    check(volume.muted && volume.writes == 1);
    check(SUCCEEDED(policy->OnSimpleVolumeChanged(0.7f,TRUE,nullptr)));
    check(volume.writes == 1); // Avoid recursive writes from our own notifications.
    volume.muted = FALSE; // Profile reset requests unmute again.
    check(SUCCEEDED(policy->OnSimpleVolumeChanged(0.5f,FALSE,nullptr)));
    await_writes(volume,2);
    check(volume.muted && volume.writes == 2);
    policy->Release();
    audio::Watcher silent(true);
    Control mine(GetCurrentProcessId(),L"own-silent"), foreign(GetCurrentProcessId()+1,L"foreign"), unknown(0,L"cross-process");
    Control shared(GetCurrentProcessId(),L"shared-session-started-by-us");
    shared.identity_result = AUDCLNT_S_NO_SINGLE_PROCESS;
    check(SUCCEEDED(shared.identity_result));
    check(SUCCEEDED(silent.initialize(&shared)) && shared.volume.writes == 0 && !shared.events);
    check(SUCCEEDED(silent.initialize(&foreign)) && foreign.volume.writes == 0 && !foreign.events);
    check(SUCCEEDED(silent.initialize(&unknown)) && unknown.volume.writes == 0 && !unknown.events);
    check(SUCCEEDED(silent.initialize(&mine)) && mine.volume.muted && mine.events);
    check(mine.volume.writes == 1);
    check(SUCCEEDED(silent.initialize(&mine)) && mine.volume.writes == 1);
    check(SUCCEEDED(mine.events->OnSimpleVolumeChanged(0.4f,FALSE,nullptr)));
    await_writes(mine.volume,2);
    mine.events->Release();
    audio::Watcher audible(false);
    Control optin(GetCurrentProcessId(),L"own-audible"), next(GetCurrentProcessId(),L"own-audible-next");
    optin.volume.muted = next.volume.muted = TRUE; // Prior per-application mute can persist.
    check(SUCCEEDED(audible.initialize(&optin)) && !optin.volume.muted && !optin.events);
    optin.volume.SetMute(TRUE,nullptr); // User chooses to mute after startup.
    check(SUCCEEDED(audible.initialize(&optin)) && optin.volume.muted && optin.volume.writes == 2);
    check(SUCCEEDED(audible.initialize(&next)) && !next.volume.muted && next.volume.writes == 1);
    check(SUCCEEDED(audible.initialize(&foreign)) && foreign.volume.writes == 0);
    check(SUCCEEDED(audible.initialize(&shared)) && shared.volume.writes == 0);
    Enumerator enumerator;
    audio::Devices devices(&enumerator,true);
    Device capture(eCapture), render(eRender);
    check(SUCCEEDED(devices.attach(&capture)) && capture.activations == 0 && capture.id_reads == 0);
    check(FAILED(devices.attach(&render)) && render.activations == 1 && render.id_reads == 1);
    puts("render-only, own/foreign/unknown/shared sessions, durable silence, duplicate notifications and sound opt-in/manual mute tests passed");
}
