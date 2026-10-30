# Crimson Desert 3424 resource identity evidence

This records read-only static analysis on 30 October 2026 of Steam build
`26317560`, `CrimsonDesert.exe` version `1.0.0.3424`, SHA-256
`d4855c08998638f07fa8fae08bd6249261f10c8fec2215effc29cb339e7fa525`.
The game was closed. No process memory, gameplay, saves or runtime DLC merge
state were sampled. Extracted resources remain in ignored research artifacts;
this document contains only the layout findings, numeric identities and hashes.

## Current resource identities

The installed base `statusinfo` header and body agree on these rows:

| Base table index | Name | Stable key | Status index | Group 100 regeneration slot |
| --- | --- | --- | --- | --- |
| 0 | Hp | 1000000 | 0 | 0 |
| 22 | Stamina | 1000026 | 22 | 12 |
| 23 | Mp | 1000027 | 23 | 13 |
| 24 | Breath | 1000078 | 24 | 14 |
| 25 | EquipMainWeapon | 1000019 | 25 | absent |
| 26 | EquipLeftMainWeapon | 1000058 | 26 | absent |

Both the base and DLC `StatusGroupInfo` row named `100`, key `1000001`, contain
20 regeneration entries. The equipment statuses at indices 25 and 26 have
`UINT32_MAX` in that group's regeneration index list and ordinary status slots
36 and 37. They are not additional stamina or spirit meters. The inherited
25/26 aliases must not authorize resource writes.

The DLC `statusinfo` patch has nine rows with status indices 79 through 87;
none replaces Hp, Stamina, Mp or Breath. A patch file's local row ordinal does
not establish its final runtime table index. Runtime identity must therefore
come from the resolved row key, even when the base ordinal appears unchanged.
Only Hp, Stamina and Mp are authorized resource identities for the existing
health, stamina and spirit features; Breath is documented for comparison.

## Native lookup contract

All addresses below are RVAs in the executable identified above.

`0x18E6AA0` loads the regeneration array at root `+0x58`, reads a **16-bit**
status reference from entry `+0`, and passes it to the resolver `0x3DCF80`.
The loop advances by `0x90` at `0x18E6C8A`, using the count at root `+0x60`.
The entry tag is a status-table index. It is distinct from the row's 32-bit
`_key`, byte `_statType`, and 32-bit `_statusIndexXXXXX`.

The resolver's already-loaded-row path gives this read-only expression:

```text
registry = pointer(moduleBase + 0x71AFA28)  // one dereference
count    = uint32(registry + 0x08)
rows     = pointer(registry + 0x58)
tag      = uint16(entry + 0x00)
row      = pointer(rows + 8 * tag)
key      = uint32(row + 0x08)
```

Require valid readable pointers, a bounded nonzero count, `tag != 0xFFFF`,
and `tag < count` before resolving the row. A missing or unloaded row denies
the corresponding cheat write. Do not invoke the native resolver's lazy-load
path. Validate keys `1000000`, `1000026` and `1000027` for Hp, Stamina and Mp,
respectively. This does not assume a particular DLC merge order.

The exact 37-byte resolver guard at `0x3DCF93` covers the global load, unsigned
index bound, table access and null-row check:

```text
8B FA 48 8B 1D 8C 2A DD 06 3B 53 08 0F 83 37 01 00 00
8B F7 48 8B 43 58 48 8B 04 F8 48 85 C0 0F 85 2E 01 00 00
```

Additional independent evidence:

- `0x171C7D9` stores the deserialized `_key` at row `+8` (`8B 44 24 78 89 47 08`).
- `0x18DDAF2` reads the entry's word tag, resolves its row and serializes row
  `+8` as a 32-bit external key.
- `0x18E8157` writes only a word to entry `+0` (`66 89 06`) after resolving a
  serialized key. Reading a 32-bit entry tag would also consume padding.
- `0x18E1580` maps row `+0x2C` through the group's `+0x50` regeneration index
  list, then computes root `+0x58` plus `0x90 * index`.
- The damage dispatcher `0x18E1930` preserves its `DX` status reference in
  `BX`. At `0x18E1A09` it forwards that reference to `0x18E1D40`, whose
  `0x18E1D6C`/`0x18E1D6F` instructions pass it directly to `0x3DCF80`.
  Therefore damage's health gate also requires the resolved Hp key, rather
  than a literal status index of zero. The native special-health branch itself
  compares against a metadata field at `0x18E198A`.

## Resource parsing and fingerprints

The bounded extraction read eight files from the installed base and `dlc_01`
archives. None of these eight entries was encrypted. Compressed entries used
bounded LZ4 decoding. Header keys and row offsets were checked against body
rows. All 18 base/DLC status-group rows were decoded to their exact ends.

The native `StatusInfo` deserializer is `0x171C770`. Its leading fields are a
32-bit byte count plus string bytes, a 32-bit key, 17 one-byte fields, four
32-bit serialized reference keys, and then the 32-bit message/status-index
fields. Reference loaders convert serialized 32-bit keys into 16-bit runtime
table references. `StatusGroupInfo` at `0x171C160` reads the string, key, blocked
byte, three lists of reference keys, and two lists of 32-bit indices.

All resource paths below are under `gamedata/binarystaticinfo__/bin/`:

| File | Decoded bytes | SHA-256 |
| --- | ---: | --- |
| `statusinfo.staticinfoheader` | 698 | `83df171c325d0fe0b12fc9258e55c58f2c1a9df6d29834ab029451b70e5f994d` |
| `statusinfo.staticinfobody` | 13612 | `21d73277b6d35b98c0a2f9a88c40fb4d9248d888d86ad1145112b1846c9acc87` |
| `statusinfo.dlc_01.staticinfoheader` | 74 | `56ca619e2f3df6ab4ff0c4688ac458d8c54f83be9d0bba3a460ffd34d0df9c29` |
| `statusinfo.dlc_01.staticinfobody` | 1016 | `0f5229d6be393051a584d644f9d4a59374e5c186cdf1199a669f2905d06e948a` |
| `statusgroupinfo.staticinfoheader` | 74 | `b13cd5346f9e5413884bd45b510a628bb31f0e7ac13c85262cf5b23b32d7af0c` |
| `statusgroupinfo.staticinfobody` | 8062 | `c78b648c998cb89beae1513a23082babc0299ec1dfd3b88880b001c9dd7b7371` |
| `statusgroupinfo.dlc_01.staticinfoheader` | 74 | `e546b0f8193134b60f25fb21373d1ce02a7f1d7bd85bc4e1f33af925c3e29b28` |
| `statusgroupinfo.dlc_01.staticinfobody` | 8134 | `e1d53dfb7babcd5886f552357c9d6ee23b32d8365576bf33cb647551e9c691fc` |

The ignored `stat-contract-3424.json`, `status-resource-extraction.json` and
`decoded-status-resources.json` reports retain the bounded native windows and
parsing details. These are static compatibility findings. They do not establish
gameplay effectiveness, resource loading timing or behavior in an active DLC
session.
