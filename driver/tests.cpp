#include "AudioBridgeCore.h"
#include <cassert>
#include <iostream>
int main() {
    nmb::AudioBridge bus; nmb::Reader a{},b{}; int32_t input[480],out[480];
    for(int i=0;i<480;++i) input[i]=i*1234;
    bus.read(a,out,480,1);for(auto x:out)assert(x==0);
    bus.write(1,input,480,100); assert(!bus.acquire(2));
    assert(bus.read(a,out,480,101)==480); for(int i=0;i<480;++i)assert(out[i]==input[i]);
    assert(bus.read(b,out,480,101)==480); // Independent capture clients.
    assert(bus.read(a,out,480,102)==0);for(auto x:out)assert(x==0);
    bus.write(1,input,480,200);bus.read(a,out,480,500201);for(auto x:out)assert(x==0);
    bus.release(1);bus.read(b,out,480,500202);for(auto x:out)assert(x==0);
    assert(bus.acquire(2));for(int n=0;n<40;++n)bus.write(2,input,480,600000+n);
    assert(bus.read(a,out,480,600050)==480);for(int i=0;i<480;++i)assert(out[i]==input[i]);
    bus.release(1);assert(bus.owner==2);bus.release(2);assert(bus.owner==0);
    std::cout<<"Audio bridge: silence, producer ownership/loss, independent readers, wrap and bounded lag passed.\n";
}
