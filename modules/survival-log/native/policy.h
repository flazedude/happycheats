#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <string>
namespace policy {
inline bool stat(int attr, int mask, int64_t componentOwner, int64_t player, bool live) {
    return live && player != 0 && componentOwner == player && attr >= 1 && attr <= 5 && (mask & (1 << (attr - 1)));
}
inline int experience(int value, int multiplier) {
    if (value <= 0) return value;
    return static_cast<int>(std::min<int64_t>(static_cast<int64_t>(value) * std::clamp(multiplier, 1, 100), std::numeric_limits<int>::max()));
}
inline bool numeric(const std::string& feature, double value) {
    if (!std::isfinite(value) || std::floor(value) != value) return false;
    if (feature == "sl_money") return value >= 0 && value <= 100000000;
    if (feature == "sl_survival_points") return value >= 1 && value <= 1000000;
    return feature == "sl_xp" && value >= 1 && value <= 100;
}
inline bool fresh(int64_t expires, int64_t now) { return expires > now && expires <= now + 5000; }
}
