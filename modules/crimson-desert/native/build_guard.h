#pragma once
#include <cstdint>

namespace crimson_build {
// Decimal values are copied from the inspected PE metadata, without a manual
// hexadecimal conversion. Tests compare this gate with a separately captured
// executable-metadata fixture.
inline constexpr std::uint16_t ExpectedMachine=0x8664;
inline constexpr std::uint32_t ExpectedTimestamp=1793296370;
inline constexpr std::uint32_t ExpectedImageSize=341544960; // Inspected PE3424, Steam build 26317560.

enum class HeaderResult { Match, MachineMismatch, TimestampMismatch, ImageSizeMismatch };
inline HeaderResult CheckHeader(std::uint16_t machine,std::uint32_t timestamp,std::uint32_t imageSize) {
    if(machine!=ExpectedMachine) return HeaderResult::MachineMismatch;
    if(timestamp!=ExpectedTimestamp) return HeaderResult::TimestampMismatch;
    if(imageSize!=ExpectedImageSize) return HeaderResult::ImageSizeMismatch;
    return HeaderResult::Match;
}
inline const char* ErrorCode(HeaderResult result) {
    switch(result) {
        case HeaderResult::MachineMismatch: return "pe_machine_mismatch";
        case HeaderResult::TimestampMismatch: return "pe_timestamp_mismatch";
        case HeaderResult::ImageSizeMismatch: return "pe_image_size_mismatch";
        default: return "";
    }
}
inline const char* ErrorMessage(HeaderResult result) {
    switch(result) {
        case HeaderResult::MachineMismatch: return "Executable PE machine mismatch; no hooks installed.";
        case HeaderResult::TimestampMismatch: return "Executable PE timestamp mismatch; no hooks installed.";
        case HeaderResult::ImageSizeMismatch: return "Executable PE image size mismatch; no hooks installed.";
        default: return "";
    }
}
}
