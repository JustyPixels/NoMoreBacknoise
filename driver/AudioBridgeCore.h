// Copyright 2026 JustPixels. MIT. Integer-only, allocation-free audio transfer.
#pragma once
#include <stdint.h>
#include <stddef.h>
namespace nmb {
struct Reader { uint64_t sequence; uint64_t generation; };
// Callers serialize all methods. Kernel integration uses a spin lock.
struct AudioBridge {
    static const size_t Capacity = 4800;
    int32_t samples[Capacity] = {};
    uint64_t written = 0, generation = 1, lastWrite100ns = 0;
    uintptr_t owner = 0;
    bool acquire(uintptr_t producer) {
        if (!producer || (owner && owner != producer)) return false;
        if (!owner) { owner = producer; ++generation; written = 0; lastWrite100ns = 0; }
        return true;
    }
    void release(uintptr_t producer) {
        if (owner == producer) { owner = 0; ++generation; written = 0; lastWrite100ns = 0; }
    }
    void write(uintptr_t producer, const int32_t* input, size_t count, uint64_t now) {
        if (!acquire(producer)) return;
        for (size_t i = 0; i < count; ++i) samples[written++ % Capacity] = input[i];
        lastWrite100ns = now;
    }
    size_t read(Reader& reader, int32_t* output, size_t count, uint64_t now) {
        if (!owner || !lastWrite100ns || now - lastWrite100ns > 500000) {
            for (size_t i=0; i<count; ++i) output[i]=0;
            reader.sequence=written; reader.generation=generation; return 0;
        }
        if (reader.generation != generation) {
            reader.generation=generation; reader.sequence=written > 480 ? written-480 : 0;
        }
        // Bound per-client lag to 20 ms. Slow clients never delay other clients.
        if (written-reader.sequence > 960) reader.sequence=written-480;
        size_t available=static_cast<size_t>(written-reader.sequence);
        size_t supplied=available<count?available:count;
        for (size_t i=0;i<supplied;++i) output[i]=samples[reader.sequence++ % Capacity];
        for (size_t i=supplied;i<count;++i) output[i]=0;
        return supplied;
    }
};
}
