#define AADRIVER_INPUT_TEST
#include "input.cpp"
#include <assert.h>
#include <stddef.h>
static std::vector<input::Event> events;
static int attempts=0,fail_at=0;
namespace input {
int physical=0;
bool record(const Event& event) {
    if(++attempts==fail_at) return false;
    events.push_back(event);return true;
}
}
static void event(size_t i,int id,bool down,int mask) {
    const input::Event& actual=events.at(i);
    assert(actual.id==id && actual.state==(down?1:2));
    assert(actual.modifiers==mask && actual.value==(down?1.f:0.f));
    assert(actual.device==0 && actual.unicode==0 && actual.symbol==NULL);
    // The exact x86 event bytes consumed by native listeners, including mods+20.
    input::Event expected={0,down?1:2,actual.name,id,0,mask,down?1.f:0.f,NULL};
    assert(!memcmp(&actual,&expected,32));
}
int main() {
    static_assert(offsetof(input::Event,modifiers)==20,"r208022 modifiers offset");
    static_assert(offsetof(input::Event,value)==24,"r208022 value offset");
    void* env=(void*)1;
    assert(input::control_at(env,"w 100",500)[0]=='0');
    assert(events.size()==1);event(0,16,true,0);
    assert(input::control_at(env,"w 100",500)[0]=='2');
    assert(input::control_at(env,"a 200",500)[0]=='0');
    input::tick_at(env,599);assert(events.size()==2);
    // Deadline is serviced independently of the requester / pipe connection.
    input::tick_at(env,600);event(2,16,false,0);assert(input::held.size()==1);
    input::tick_at(env,700);event(3,29,false,0);assert(input::held.empty());
    size_t before=events.size();
    for(const char* bad: {"w 0","w 10001","w 1 extra","escape 10","w","w -1",
        "ctrl+ctrl+1 10","meta+1 10","ctrl+ 10","ctrl+shift+alt+ctrl+1 10",
        "+1 10","1+ctrl 10","release 10","ctrl+release 10"})
        assert(input::control_at(env,bad,700)[0]=='2');
    assert(input::held.empty() && events.size()==before);
    assert(input::control_at(NULL,"s 100",700)[0]=='2');
    assert(input::control_at(env,"s 100",700)[0]=='0');
    assert(input::control_at(env,"space 100",700)[0]=='0');
    input::release(env);assert(input::held.empty());event(events.size()-1,56,false,0);

    events.clear();
    input::physical=0x200; // Existing physical CapsLock is retained in every event.
    assert(input::control_at(env,"ctrl+1 100",1000)[0]=='0');
    event(0,28,true,0x211);event(1,1,true,0x211);
    assert(!strcmp(events[0].name,"lctrl") && !strcmp(events[1].name,"1"));
    assert(input::control_at(env,"shift+ctrl+2 200",1000)[0]=='0');
    event(2,41,true,0x233);event(3,2,true,0x233);
    assert(input::control_at(env,"alt+1 10",1000)[0]=='2'); // Primary1 already held.
    input::tick_at(env,1099);assert(events.size()==4);
    input::tick_at(env,1100);event(4,1,false,0x233);assert(events.size()==5);
    // Shared ctrl must stay down after the first chord reaches its deadline.
    input::tick_at(env,1200);
    event(5,2,false,0x233);event(6,41,false,0x211);event(7,28,false,0x200);
    assert(input::held.empty());

    events.clear();input::physical=0;
    assert(input::control_at(env,"alt+shift+ctrl+0 10000",2000)[0]=='0');
    event(0,28,true,0x11);event(1,41,true,0x33);event(2,55,true,0x77);event(3,10,true,0x77);
    assert(input::control_at(env,"release",2001)=="0released");
    event(4,10,false,0x77);event(5,55,false,0x33);event(6,41,false,0x11);event(7,28,false,0);
    assert(input::held.empty());

    // An unavailable primary post rolls back only newly posted modifiers.
    events.clear();fail_at=attempts+2;
    assert(input::control_at(env,"ctrl+3 10",3000)[0]=='2');
    assert(input::held.empty() && events.size()==2);
    event(0,28,true,0x11);event(1,28,false,0);fail_at=0;
    // Failure in a second modifier must leave an older chord's ctrl hold intact.
    events.clear();
    assert(input::control_at(env,"ctrl+1 100",3100)[0]=='0');
    fail_at=attempts+2;
    assert(input::control_at(env,"ctrl+shift+alt+2 10",3100)[0]=='2');
    assert(input::held.size()==1 && events.size()==4);
    event(2,41,true,0x33);event(3,41,false,0x11);
    fail_at=0;input::tick_at(env,3200);
    event(4,1,false,0x11);event(5,28,false,0);assert(input::held.empty());
    // Number row IDs, including0 ->10, and deadline cleanup without a pipe call.
    for(int i=0;i<10;++i) {
        events.clear();char command[16];sprintf_s(command,sizeof(command),"%d 1",i);
        assert(input::control_at(env,command,4000)[0]=='0');
        input::tick_at(env,4001);assert(events.size()==2 && input::held.empty());
        event(0,i?i:10,true,0);event(1,i?i:10,false,0);
    }
    puts("input event bytes, modifier chords, shared holds, bounds, rollback and deadline release passed");
}
