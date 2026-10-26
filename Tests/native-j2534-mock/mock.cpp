// CI-ONLY synthetic J2534 04.04 driver, NOT TOPDON, NOT vehicle data.
// Compiled on GitHub Windows runner; no physical device can be opened.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstring>
#include <cstdlib>

static_assert(sizeof(DWORD) == 4, "J2534 requires 32-bit unsigned fields");
struct PASSTHRU_MSG {
    DWORD ProtocolID, RxStatus, TxFlags, Timestamp, DataSize, ExtraDataIndex;
    BYTE Data[4128];
};
static_assert(sizeof(PASSTHRU_MSG) == 4152, "J2534 message ABI mismatch");

static bool deviceOpen=false, channelOpen=false, filterOpen=false;
static DWORD reads=0;
static bool scenario(const char* name) {
    const char* s=std::getenv("SRX_NATIVE_SIM_CASE");
    return s && std::strcmp(s,name)==0;
}
static void frame(PASSTHRU_MSG& out, DWORD stamp, BYTE value) {
    std::memset(&out,0,sizeof(out));
    out.ProtocolID=5;
    out.Timestamp=stamp;
    out.DataSize=5;
    const BYTE data[5]={0,0,1,0xF1,value};
    std::memcpy(out.Data,data,5);
}
extern "C" {
__declspec(dllexport) DWORD __stdcall SrxSimulatorIdentity() {return 0x53525853;}
__declspec(dllexport) DWORD __stdcall PassThruOpen(void*,DWORD* device) {
    if (!device) return 7;
    if (scenario("openfail")) return 7;
    deviceOpen=true;*device=1001;reads=0;return 0;
}
__declspec(dllexport) DWORD __stdcall PassThruClose(DWORD device) {
    if (!deviceOpen || device!=1001) return 7;
    if (scenario("closefail")) return 7;
    deviceOpen=false;return 0;
}
__declspec(dllexport) DWORD __stdcall PassThruConnect(
    DWORD device,DWORD protocol,DWORD flags,DWORD baud,DWORD* channel) {
    if (!deviceOpen || device!=1001 || !channel) return 7;
    if (protocol!=5 || flags!=0 || baud!=500000 || scenario("connectfail"))
        return 7;
    channelOpen=true;*channel=2002;return 0;
}
__declspec(dllexport) DWORD __stdcall PassThruDisconnect(DWORD channel) {
    if (!channelOpen || channel!=2002) return 7;
    if (scenario("disconnectfail")) return 7;
    channelOpen=false;filterOpen=false;return 0;
}
__declspec(dllexport) DWORD __stdcall PassThruStartMsgFilter(
    DWORD channel,DWORD filterType,PASSTHRU_MSG* mask,
    PASSTHRU_MSG* pattern,void* flowControl,DWORD* filter) {
    if (!channelOpen || channel!=2002 || filterType!=1 || !mask ||
        !pattern || flowControl!=nullptr || !filter ||
        mask->ProtocolID!=5 || pattern->ProtocolID!=5 ||
        mask->DataSize!=4 || pattern->DataSize!=4 ||
        std::memcmp(mask->Data,pattern->Data,4)!=0 ||
        mask->Data[0]!=0 || mask->Data[1]!=0 || mask->Data[2]!=0 ||
        mask->Data[3]!=0 || scenario("filterfail")) return 7;
    filterOpen=true; *filter=3003;return 0;
}
__declspec(dllexport) DWORD __stdcall PassThruStopMsgFilter(
    DWORD channel,DWORD filter) {
    if (!filterOpen || channel!=2002 || filter!=3003) return 7;
    if (scenario("stopfilterfail")) return 7;
    filterOpen=false;return 0;
}
__declspec(dllexport) DWORD __stdcall PassThruReadMsgs(
    DWORD channel,PASSTHRU_MSG* messages,DWORD* count,DWORD timeout) {
    if (!deviceOpen || !channelOpen || !filterOpen || channel!=2002 ||
        !messages || !count || !timeout) return 7;
    DWORD cap=*count;
    reads++;
    if (scenario("error")) {*count=0;return 7;}
    if (scenario("stale")) return 0x12; // requested count, untouched slots
    if (scenario("badcount")) {*count=cap+1;return 0x12;}
    if (scenario("zero")) {*count=0;return 0x12;}
    if (scenario("empty")) {*count=0;return 0x10;}
    if (cap<2) {*count=0;return 7;}
    std::memset(messages,0,sizeof(PASSTHRU_MSG)*2);
    frame(messages[0],201,0xA1);
    frame(messages[1],202,0xA2);
    if (scenario("timeout")) {*count=1;return 0x09;}
    *count=2;
    return scenario("overflow") ? 0x12 : 0;
}
} // extern "C"
