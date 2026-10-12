#define AADRIVER_INPUT_TEST
#include "input.cpp"
#include <assert.h>
static std::vector<int> events;
namespace input {
bool record(const Key& key,bool down) { events.push_back(down?key.id:-key.id);return true; }
}
int main() {
    void* env=(void*)1;
    assert(input::control_at(env,"w 100",500)[0]=='0');
    assert(events.size()==1 && events.back()==16);
    assert(input::control_at(env,"w 100",500)[0]=='2');
    assert(input::control_at(env,"a 200",500)[0]=='0');
    input::tick_at(env,599);assert(events.size()==2);
    // Deadline is serviced independently of the requester / pipe connection.
    input::tick_at(env,600);assert(events.back()==-16 && input::held.size()==1);
    input::tick_at(env,700);assert(events.back()==-29 && input::held.empty());
    for(const char* bad: {"w 0","w 10001","w 1 extra","escape 10","w","w -1"})
        assert(input::control_at(env,bad,700)[0]=='2');
    assert(input::held.empty());
    assert(input::control_at(NULL,"s 100",700)[0]=='2');
    assert(input::control_at(env,"s 100",700)[0]=='0');
    assert(input::control_at(env,"space 100",700)[0]=='0');
    input::release(env);assert(input::held.empty());assert(events.back()==-56);
    puts("input key IDs, bounds, overlapping keys, deadlines and disconnect-independent release passed");
}
