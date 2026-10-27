# Spec 01 — PK2 archives

Status: **container format verified** and **name hash recovered** against
`data/RT2.PK2` (85,048,105 bytes, 2,880 entries). Implemented in
`src/assets/pk2.cpp`; see `07-asset-names.md` for the resolved name catalogue.

## File layout

```
offset 0                     u32                      entryCount
offset 4                     entryCount * 12 bytes    index records
offset dataStart             entry bytes              payload, back to back

dataStart = 4 + entryCount * 12
```

Each 12-byte index record is:

| offset | type | field | meaning |
|--------|------|-------|---------|
| +0 | u32 | `hash` | 32-bit hash of the entry's file name |
| +4 | u32 | `offset` | byte offset **relative to `dataStart`** |
| +8 | u32 | `size` | byte length |

All multi-byte values are **little-endian**.

> The engine distinguishes **PK2** from the older **PAK** by the file name's last
> character (`FUN_004f4c20`): a trailing `K` means the legacy PAK layout
> (`u16` count, index at offset 2); otherwise it reads the `u32` count above.
> Both `.\DATA\rt2.PK2` and the fallback `\DATA\rt2.PAK` are probed at startup,
> and `.\data\*.pk2` is enumerated so every pack shares one namespace.

## Verified invariants

For `RT2.PK2`: `count = 2880`, `dataStart = 0x8704` (34,564). The records are
contiguous and start at 0 — i.e. `offset[i] == offset[i-1] + size[i-1]` for all
`i`, with `offset[0] == 0` — and

```
dataStart + (offset[last] + size[last]) == fileSize
```

holds exactly. The reader rejects any archive whose records are non-contiguous
or whose extents fall outside the file.

## Name hash (recovered)

The executable's own lookup path is:

* `FUN_004f0d70` — per-byte **DOS-style uppercase fold**,
* `FUN_004f48e0` / `FUN_004f4e60` — the accumulators that derive the index hash.

```c
uint8_t fold(uint8_t c) {                       // FUN_004f0d70
    if ((c > 0x60 && c < 0x7b) || c > 0xef) c -= 0x20;
    return c;
}

// Primary hash. Sign-extends each folded byte, as the original `(char)` cast does.
uint32_t name_hash(const char* s, size_t n) {
    uint32_t h = 7;
    for (size_t i = 0; i < n; ++i)
        h = (uint32_t)((int8_t)fold((uint8_t)s[i]) + 0x3039u + h * 0x41c64e6eu);
    return h;
}

// Secondary hash: the engine's cached asset table also accepts this one.
uint32_t name_hash_alt(const char* s, size_t n) {
    uint32_t sum = 0, h = 0;
    for (size_t i = n; i-- > 0;) {
        uint32_t v = (uint32_t)(int8_t)fold((uint8_t)s[i]);
        sum += v;
        h = rotl32(h, 7) + v + sum;
    }
    return h;
}
```

`0x41c64e6e` / `0x3039` are the classic MSVC `rand()` LCG multiplier and
increment, seeded with 7. The secondary accumulator is a rotate-left-7 fold-in
over a backward scan. Because the fold uppercases, lookups are
**case-insensitive** (`mainwin.win` and `MAINWIN.WIN` hash identically, and the
former resolves to `RT2_06.PK2` entry 15). `name_hash_alt` matched none of the
RT2.PK2 index records, so the archive index is addressed with `name_hash`; the
alternate is retained for parity with the engine's cached table.

Sample vectors (also asserted in `tests/test_pk2.cpp`):

| name | `name_hash` | entry |
|------|-------------|-------|
| `FLATC16.DAT` | `16a732e5` | RT2.PK2 #1270 (15,108 B) |
| `ground.imb` | `9f2c528f` | #2072 (1,034,871 B) |
| `arch1.imb` … `arch4.imb` | `1c86e51f` … `b139c34f` | #4 … #7 |
| `trainbuy.imb` | `0f4908bf` | #2049 |
| `mainwin.win` | `3d8780db` | RT2_06.PK2 #15 |

2,399 of the 2,901 entries across the four packs (2,378 in `RT2.PK2`) are named
(see `07-asset-names.md`); the remaining 502 are 499 unnamed IMB banks, two
`.win` layouts and one data table. The `.imb` sprite-bank container those names
point at is described in `08-image-bank.md`.

## Entry contents

Entries store raw bytes. Observed payloads begin with small structured headers
(e.g. `08 00 00 00 03 00 00 00 BC 02 00 00 …`) and are **not** plain text; the
archive itself is not encrypted at the container level (the index is readable).
Individual entry formats are separate specs.

The terrain colour-ramp family is contiguous from entry 1270:

| entry | size | name | content |
|-------|------|------|---------|
| 1270 | 15,108 | `FLATC16.DAT` | 16-bit shading ramp |
| 1271 | 7,556 | `FLATC80.DAT` | 8-bit ramp, palette 0 |
| 1272 | 768 | *(unresolved)* | 256-entry VGA palette (`ffffff 00ffff ff00ff …`) |
| 1273 | 32,768 | `rgbto80.dat` | RGB→8-bit conversion table (256×256) |
| 1274… | | `FLATC81.DAT`, … | one 3-entry group per palette index, up to `rgbto88.dat` (#1296) |

## Open questions

1. **Names for the remaining 502 entries in `RT2.PK2`** (502 across the four
   packs): 499 IMB banks, two `.win` layouts and one data table. The hash is
   known, so any name can be tested. The literal and table-derived families in
   `07-asset-names.md` now resolve 2,378 of 2,880 `RT2.PK2` entries, including
   the 121 `.RAW` sounds; the unresolved IMB banks do not match those families.
   The contiguous `PALT0000.PAL` … `PALT0712.PAL` block in `RT2.PK2` (and
   `PALT0715.PAL` in `RT2_11PL.PK2`) is fully resolved.
2. **Whether entries are individually compressed** is unconfirmed. Payloads look
   like structured records rather than entropy-maximised data (the `rgbto8`
   tables are visibly monotonic), suggesting they are stored raw, but this needs
   per-format confirmation.

## Reader API

```cpp
auto pk2 = rt2::assets::Pk2Archive::fromFile("data/RT2.PK2");
pk2.entryCount();          // 2880
pk2.dataStart();           // 0x8704
pk2.entries()[i];          // Pk2Entry{hash, offset, size}
pk2.entryData(i);          // std::span<const std::byte>
pk2.find(hash);            // std::optional<size_t>
pk2.findByName("FLATC16.DAT");  // std::optional<size_t>  (case-insensitive)

rt2::assets::nameHash("ground.imb");     // 0x9f2c528f
rt2::assets::nameHashAlt("ground.imb");  // secondary accumulator
```

## CLI

```
pk2dump <archive> [--list] [--extract DIR] [--get N --out FILE]
pk2dump <archive> --find NAME
pk2dump <archive> --names FILE
```

