// Included once, by SYSVAD EndpointsCommon/minwavertstream.cpp.
// Caller operates at <=DISPATCH_LEVEL; never call from pageable code while locked.
#pragma once
#include "AudioBridgeCore.h"
static KSPIN_LOCK g_NmbLock = 0;
static nmb::AudioBridge g_NmbBus;
void NmbInitialize() { KeInitializeSpinLock(&g_NmbLock); }
static void NmbProducerLost(void* producer) {
    KIRQL irql; KeAcquireSpinLock(&g_NmbLock,&irql);
    g_NmbBus.release(reinterpret_cast<uintptr_t>(producer));
    KeReleaseSpinLock(&g_NmbLock,irql);
}
static bool NmbPcmFormat(const WAVEFORMATEXTENSIBLE* format) {
    return format && format->Format.nSamplesPerSec==48000 &&
        (format->Format.nChannels==1 || format->Format.nChannels==2) &&
        (format->Format.wBitsPerSample==16 || format->Format.wBitsPerSample==24 || format->Format.wBitsPerSample==32) &&
        format->Format.nBlockAlign == format->Format.nChannels*format->Format.wBitsPerSample/8 &&
        (format->Format.wFormatTag==WAVE_FORMAT_PCM ||
         (format->Format.wFormatTag==WAVE_FORMAT_EXTENSIBLE && IsEqualGUID(format->SubFormat,KSDATAFORMAT_SUBTYPE_PCM)));
}
static int32_t NmbLoad(const BYTE* input, ULONG width) {
    if(width==2) { int16_t value; RtlCopyMemory(&value,input,2); return static_cast<int32_t>(value)*65536; }
    if(width==3) { uint32_t value=(static_cast<uint32_t>(input[0])<<8)|(static_cast<uint32_t>(input[1])<<16)|(static_cast<uint32_t>(input[2])<<24); return static_cast<int32_t>(value); }
    int32_t value; RtlCopyMemory(&value,input,4); return value;
}
static void NmbRender(void* producer,const BYTE* bytes,ULONG length,const WAVEFORMATEXTENSIBLE* format) {
    if(!NmbPcmFormat(format)) return;
    ULONG stride=format->Format.nBlockAlign,width=format->Format.wBitsPerSample/8;
    int32_t block[480]; ULONG position=0;
    while(position+stride<=length) {
        ULONG count=0;
        while(count<480 && position+stride<=length) {
            int64_t value=NmbLoad(bytes+position,width);
            if(format->Format.nChannels==2) value=(value+NmbLoad(bytes+position+width,width))/2;
            block[count++]=static_cast<int32_t>(value); position+=stride;
        }
        KIRQL irql; KeAcquireSpinLock(&g_NmbLock,&irql);
        g_NmbBus.write(reinterpret_cast<uintptr_t>(producer),block,count,KeQueryInterruptTime());
        KeReleaseSpinLock(&g_NmbLock,irql);
    }
}
static void NmbCapture(nmb::Reader& reader,BYTE* bytes,ULONG length,const WAVEFORMATEXTENSIBLE* format) {
    RtlZeroMemory(bytes,length);
    if(!NmbPcmFormat(format)) return;
    ULONG stride=format->Format.nBlockAlign,width=format->Format.wBitsPerSample/8;
    int32_t block[480]; ULONG position=0;
    while(position+stride<=length) {
        ULONG count=min(480,(length-position)/stride);
        KIRQL irql; KeAcquireSpinLock(&g_NmbLock,&irql);
        g_NmbBus.read(reader,block,count,KeQueryInterruptTime());
        KeReleaseSpinLock(&g_NmbLock,irql);
        for(ULONG i=0;i<count;++i) {
            uint32_t value=static_cast<uint32_t>(block[i]);
            for(ULONG channel=0;channel<format->Format.nChannels;++channel) {
                for(ULONG byte=0;byte<width;++byte) bytes[position+channel*width+byte]=static_cast<BYTE>(value >> (8*(4-width+byte)));
            }
            position+=stride;
        }
    }
}
