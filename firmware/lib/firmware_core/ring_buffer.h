// Fixed-size sample buffer that makes the "no silent data loss" promise possible (FR-07, NFR-03).
// Pure C++ so the eviction/ordering/persistence logic is host-testable (TC-U-FW-04…06).

#pragma once

#include <cstddef>
#include <cstdint>

namespace sr {
namespace storage {

/// One buffered sample. Layout is packed and fixed-width so the whole ring can be persisted as a single blob.
struct SampleRecord {
    uint32_t epoch = 0;          // UTC seconds (NTP-synced; 0 when the clock is unsynced)
    uint64_t sequence = 0;       // monotonic per device; also the server-side dedupe key
    int16_t tempCx100 = 0;       // 28.75 °C -> 2875   (0 means "no reading")
    uint16_t rhx100 = 0;         // 41.20 %  -> 4120
    uint32_t lux = 0;
    uint16_t uvix100 = 0;        // 0.30 UVI -> 30
    int16_t surfaceCx100 = 0;    // 0x8000 (-32768) means "no surface probe fitted"
    uint8_t qualityFlags = 0;
    uint8_t reserved = 0;
};

/// True when a record carries a surface-temperature reading.
inline bool hasSurfaceReading(const SampleRecord& record) {
    return record.surfaceCx100 != static_cast<int16_t>(-32768);
}

/// Marker used to encode "no surface probe" in the packed record.
constexpr int16_t kNoSurfaceReading = -32768;

/// A ring buffer of samples with explicit eviction accounting.
class RingBuffer {
public:
    static constexpr std::size_t kCapacity = 720;   // 12 h at a 60 s interval (BR-07.3)

    RingBuffer() = default;

    /// Clears the buffer and the eviction counter.
    void clear();

    /// Appends a sample. When the buffer is full the oldest sample is overwritten and the drop counter
    /// increases: losing data is allowed, losing it *silently* is not (BR-07.2).
    void push(const SampleRecord& record);

    /// Number of samples currently buffered.
    std::size_t size() const { return count_; }

    /// True when no sample can be appended without eviction.
    bool isFull() const { return count_ == kCapacity; }

    /// Number of samples evicted since the last clear().
    uint32_t droppedCount() const { return dropped_; }

    /// Oldest sample first. Returns false when the index is out of range.
    bool at(std::size_t index, SampleRecord& out) const;

    /// Removes the oldest `n` samples (used after a successful publish). Returns how many were removed.
    std::size_t dropOldest(std::size_t n);

    /// Serialises the buffered samples into `out` (row-major, oldest first). Returns bytes written.
    std::size_t serialize(uint8_t* out, std::size_t outCapacity) const;

    /// Restores a buffer from `serialize` output. Returns the number of samples restored (0 on a bad blob).
    std::size_t deserialize(const uint8_t* in, std::size_t length);

    /// Raw capacity in bytes for a full buffer — used to size the NVS blob.
    static std::size_t serializedSize(std::size_t samples) { return samples * sizeof(SampleRecord); }

private:
    SampleRecord records_[kCapacity] = {};
    std::size_t head_ = 0;      // index of the oldest sample
    std::size_t count_ = 0;
    uint32_t dropped_ = 0;
};

}  // namespace storage
}  // namespace sr
