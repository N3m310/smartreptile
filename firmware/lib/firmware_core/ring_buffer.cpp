#include "ring_buffer.h"

#include <cstring>

namespace sr {
namespace storage {

void RingBuffer::clear() {
    head_ = 0;
    count_ = 0;
    dropped_ = 0;
}

void RingBuffer::push(const SampleRecord& record) {
    if (count_ < kCapacity) {
        records_[(head_ + count_) % kCapacity] = record;
        ++count_;
        return;
    }

    // Full: overwrite the oldest slot and advance the head. The counter makes the loss visible on the
    // dashboard and in the coverage report — a silent gap would make a bad week look like a healthy one.
    records_[head_] = record;
    head_ = (head_ + 1) % kCapacity;
    ++dropped_;
}

bool RingBuffer::at(std::size_t index, SampleRecord& out) const {
    if (index >= count_) {
        return false;
    }

    out = records_[(head_ + index) % kCapacity];
    return true;
}

std::size_t RingBuffer::dropOldest(std::size_t n) {
    const std::size_t toDrop = (n < count_) ? n : count_;
    head_ = (head_ + toDrop) % kCapacity;
    count_ -= toDrop;
    return toDrop;
}

std::size_t RingBuffer::serialize(uint8_t* out, std::size_t outCapacity) const {
    if (out == nullptr) {
        return 0;
    }

    const std::size_t required = serializedSize(count_);
    if (outCapacity < required) {
        return 0;
    }

    for (std::size_t i = 0; i < count_; ++i) {
        SampleRecord record;
        at(i, record);
        std::memcpy(out + (i * sizeof(SampleRecord)), &record, sizeof(SampleRecord));
    }

    return required;
}

std::size_t RingBuffer::deserialize(const uint8_t* in, std::size_t length) {
    if (in == nullptr || length == 0 || (length % sizeof(SampleRecord)) != 0) {
        return 0;
    }

    const std::size_t samples = length / sizeof(SampleRecord);
    if (samples > kCapacity) {
        return 0;
    }

    clear();

    for (std::size_t i = 0; i < samples; ++i) {
        SampleRecord record;
        std::memcpy(&record, in + (i * sizeof(SampleRecord)), sizeof(SampleRecord));
        push(record);
    }

    return count_;
}

}  // namespace storage
}  // namespace sr
