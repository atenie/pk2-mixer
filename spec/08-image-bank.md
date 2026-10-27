# Spec 08 — Image banks (`.imb`) and palettes

Status: **container and tile format decoded**; the map's class → tile table is
still open. Recovered from the loader `FUN_004f8b80` and the blitter's sprite
reads (`FUN_004ff900`, `FUN_00510670`). Implemented in `src/assets/imb.cpp` and
`src/assets/terrain_gfx.cpp`; rendered by `src/render/terrain_view.cpp`.

Verification tools: `tools/recon/imb_probe.py` (walk), `imb_detail.py`
(fields), `imb_sheet.py` (contact sheet), `pal_strip.py` (palette),
`imb_codec.py` (frame pixel decoding), `../open-rtii/tools/palette_verify.py`
(palette gate / PALT / shade-row audit over every bank, with the addresses
that prove each rule - `../open-rtii/tools/disasm_va.py` prints them), and
the `imbview` binary, which renders a retail map isometrically to a PPM.

`.imb` entries hold sprites — ground/road/track tiles, bridges, buildings,
locomotives and effects. The PK2 entries are stored uncompressed, but individual
frame pixels are either raw or row-RLE (see "Frame pixel encoding" below). They
are addressed with the PK2 name hash (`01-archive-pk2.md`). Referenced families:
`ground.imb`, `road.imb`, `tracks.imb`, `trackd.imb`, `bridge01..04.imb`,
`bpbuildi.imb`, `arch1..4.imb`, `trainbuy.imb`, `trainlst.imb`, `traindtl.imb`,
`trainbdl.imb`, `smoke.imb`, `carbkg.imb` (see `07-asset-names.md`).

## Container layout

All integers little-endian. The reader (`FUN_004f8b80`) is a pure sequential
stream reader: it reads 4 bytes for the count, then 0x44 bytes per group, then a
variable sub-array, then frame records in order.

```
u16  groupCount                         // read as 4 bytes, low u16 used
repeat groupCount times:                // 0x44-byte group header (see below)
    u8   header[0x40]                   // 0x40 = 64 bytes
    u32  subCount                       // at group +0x40
    repeat subCount times:              // 14 bytes in memory, 6 read from the stream
        u32  blobSize
        u16  frameCount
        if blobSize >= 2: u8 blob[blobSize]      // shared sprite/pixel blob
        repeat frameCount times:
            u8   frame[0x1d]            // 29-byte frame record
            u32  dataLen
            u8   data[dataLen]          // per-frame pixel bytes
```

The in-memory record for a sub-array entry is 14 bytes because two 4-byte
*pointer* fields are filled at load time (the blob pointer at +6 and the frame
array pointer at +10); only `blobSize` (+0) and `frameCount` (+4) come from the
file. Frame records are 29 bytes in memory, with a pointer to the decoded pixel
data stored at +0x19 (overwriting that field from disk).

The palette gate is a **frame** flag, not a header flag. After a group's frames
are read, `FUN_004f8b80` asks `FUN_004f0f80(group, 0, 0, 0)` for the first
frame record of the group's zoom-0 mip (the mip table is at group `+0xc`), and
then, at `0x004f8d0d`:

```asm
004f8d0d  call 0x004f0f80            ; -> first frame record of mip[0]
004f8d12  test byte ptr [eax], 2     ; frame record byte 0, bit 1
004f8d15  je   0x004f8d41            ; no palette for this group
004f8d1a  mov  ecx, [edi + eax + 4]  ; group + 4 = u32 palette index
004f8d23  push 0x55e5e0              ; "PALT%04d.PAL"
004f8d29  call 0x0051be4f            ; sprintf
004f8d35  call 0x004f4bb0            ; open through the asset path
004f8d3d  mov  [edi + ecx + 8], eax  ; group + 8 = palette object
```

So a group uses `PALT<group.u32 @ +4>.PAL` when **bit 1 of its first frame
record** is set, and that palette pointer lives at group `+8` (the header's own
`+8` bytes are overwritten only in that case). Group header byte 0 is the *mip
mask*: bit 0 = mip 0 present, bit 1 = mip 1 (`FUN_004f8740` draws plane
`param_4` only when `group[0] & param_4`). `ground.imb`'s header byte is `0x01`
while its frames carry `0x2a`/`0x2b` — which is why a header-based test
mis-classifies every terrain bank.

Measured over both installs (2,982 banks, 22,494 groups): **every** first frame
has bit 1 set, every named `PALT` file exists (`tools/palette_verify.py`), and
all 58,866 frames carry the bit, so in practice every group owns a palette —
indices 0…715 are used, 625 distinct files. No group falls back to a global
palette (the code would leave `group+8` at zero and the blitter would fault).

Those files are **16-bit palette data**, not 768-byte VGA palettes
(`FUN_004f8ed0`, `0x004f8ed0`):

```
u32  rowCount                     ; read into struct+0
s8   shadeToRow[35]               ; read into struct+0xf  (0x23 bytes)
u16  rows[rowCount][256]          ; malloc(rowCount << 9) then read
```

so the file is exactly `39 + 512 * rowCount` bytes — `PALT0000.PAL` (16,423 B,
`rowCount = 32`, rows `0,1,2 … 31` then `-1,-1,-1` at shades 32…34) and
`PALT0001.PAL` (1,575 B, `rowCount = 3`). Rows are RGB555, red in bits 14…10,
green 9…5, blue 4…0. The 35-byte map is indexed by *shade level*; `-1` means
"no palette for that shade" (`FUN_004f9550` returns 0 then).

The row itself comes from `FUN_004f9550(this = group->palette, shade)`:
`row = (signed char)shadeToRow[shade]`, `pointer = rows + row * 0x200` (16 bpp;
`* 0x100` in 8 bpp). The engine's standard sprite/tile draw passes **shade 16**
(`FUN_004f9550(…,0x10)`, `FUN_004f8740(…,0x10,0,3,0xfd)`), and every shipped
shade map defines it: it is the neutral row of a dark → bright ramp (PALT0058
row 0 averages to luminance 20, row 16 to 159, row 31 to 222).

**Palette index 0 is the transparency key.** `FUN_004fed40` (16 bpp, raw
frames) tests the source index and skips the pixel when it is 0
(`0x004fef89: test cl,cl / je`); its 8 bpp twin `FUN_00500330` does the same at
`0x0050055e`. Only the opaque variant (`DAT_0057ab30 != 0`) draws index 0. The
loader reinforces that by forcing entry 0 of a row to 0 (`FUN_004f8ed0` tail,
`FUN_004f9400`). So a PALT row entry 0 is a marker, never paint: e.g.
`ground.imb` #4 (river art) is 48 % index 0 and the sea shows through from the
base fill behind the tile, not from the palette.

> The 768-byte entries interspersed with the terrain ramps (#1272, #1275, …,
> beginning `ffffff 00ffff ff00ff …`) are **not** `PALT` files — they are
> genuine 256×RGB byte tables named `defaul0.pal` … `defaul8.pal`. They are the
> 8 bpp *screen* palettes: `FUN_004ef2a0` opens `defaul<N>.pal` plus
> `rgbto8<N>.dat` (32,768 B) whenever the 8 bpp mode is active, with `N` chosen
> per screen (the main game view is screen 1 → `defaul1.pal`). A group's PALT
> rows are then quantised into indices of that screen palette through
> `rgbto8<N>.dat` (`0x004f8fef`). `FLATC8n.DAT`/`FLATC16.DAT` (236 × 32 ramp
> entries) shade the flat cell fill, they are not sprite palettes either.


`FUN_004f8d80` is the matching free routine, confirming the hierarchy:
`groupCount` × 0x44 groups, each with `subCount` × 14 sub-entries, each with
`frameCount` × 29 frame records, each owning one heap pixel buffer.

## Worked example — `ends1.imb` (RT2.PK2 #0, 41,137 bytes)

```
rel 0x00  u32  8            groupCount = 8
rel 0x04  ...  group 0 header (0x40 bytes); flags byte 0 = 0x03
rel 0x44  u32  2            group 0 subCount = 2
rel 0x48  u32  1            sub 0 blobSize = 1   (blob absent, size < 2)
rel 0x4C  u16  1            sub 0 frameCount = 1
rel 0x4E  ...  frame record (0x1d bytes), then u32 dataLen, then data
```

## Validation

`tools/recon/imb_probe.py` walks this layout and checks that the sequential parse
consumes **exactly** the entry's bytes. All of the sampled banks do:

| entry | bytes | groups | subs | frames | pixel bytes | consumed |
|-------|-------|--------|------|--------|-------------|----------|
| `ends1.imb` | 41,137 | 8 | 16 | 16 | 39,965 | 41,137 ✔ |
| `arch1.imb` | 26,100 | 4 | 8 | 8 | 25,512 | 26,100 ✔ |
| `buildbuy.imb` | 760 | 2 | 2 | 2 | 542 | 760 ✔ |
| `smoke.imb` | 24,310 | 9 | 13 | 156 | 18,325 | 24,310 ✔ |
| `carbkg.imb` | 26,700 | 1 | 1 | 1 | 26,589 | 26,700 ✔ |
| `trndbut.imb` | 29,381 | 16 | 16 | 16 | 27,665 | 29,381 ✔ |

The group/frame counts are semantically consistent: `trndbut.imb` is 16 single-
frame icons, while `smoke.imb`'s first four groups carry 24 frames each (smoke
animation) and the remaining five carry 12.

## Frame record (29 bytes, 1 byte/pixel)

The frame record's fields match the offsets the blitter reads
(`*(int *)(sprite + 9)`, `+0xd`, `+0x11`, `+0x15`):

| offset | type | meaning |
|--------|------|---------|
| +0x00 | u32 | id — low byte is the art code (`0x2a` flat, `0x2b` transition), high byte is the mip level |
| +0x04 | u32 | storage flags: `0x200` = raw pixels, `0x300` = row RLE |
| +0x08 | u32 | x — placement in the bank's virtual sheet (integer, e.g. `66 * group`) |
| +0x0c | u32 | y |
| +0x10 | u32 | **width** (integer, 64 for terrain) |
| +0x14 | u32 | **height** (integer, 64 for terrain) |
| +0x18 | u8  | padding |
| +0x19 | u32 | unused on disk; the loader overwrites it with the pixel-buffer pointer |

## Frame pixel encoding

The frame flags at `+0x04` select the pixel storage:

| flags | encoding |
|-------|----------|
| `0x200` | raw 8-bit palette indices, row-major; `dataLen == width * height` |
| `0x300` | row-based RLE (the blitter's decoder, `FUN_004ff2c0`) |

Terrain banks such as `ground.imb` and `trackd.imb` use raw frames; most UI,
logo, train and effect banks use RLE. A raw-only reader turns RLE art into
diagonal streaks because compressed bytes are interpreted as pixels.

RLE rows (`imb_codec.py` implements this):

```
row:   u8 length            (0x01..0x7f, includes the length byte)
    or u8 | u8              (0x80..0xbf: ((b & 0x3f) << 8) | next,
                             includes both length bytes)
    then commands until the row-length window ends or width is filled:
      0x01..0x7f  literal: N palette indices follow
      0x80..0x9f  transparent span: (b & 7) pixels (0 => next byte)
      0xa0..0xbf  solid-colour alpha span: (b & 0x1f) pixels (0 => next byte),
                  each with one alpha byte; engine tint overlay, not a palette
                  index (exported as transparent)
      0xc1..0xff  transparent span: (b & 0x3f) pixels
      0x00        end of row
      0xc0        end of image (remaining pixels transparent)
```

The row length may include a trailing `0x00`/`0xc0`; any width left after the
commands is transparent. The stream ends with `0xc0`. A 1x1 frame such as
`SMOKE.imb` group 0 is just `01 c0`: a one-byte empty row followed by the image
end.

## Palettes and the isometric grid

Every bank group - terrain included - carries its own `PALT%04d.PAL` (see
"Container layout"): `ground.imb` #4 -> `PALT0058`, #34 -> `PALT0060`,
#152 -> `PALT0062`, #184 -> `PALT0064`. The `defaul%s.pal` family
(`defaul0.pal` ... `defaul8.pal`, RT2.PK2 #1272...#1295, 768 bytes = 256 RGB
triples) is the 8 bpp *screen* palette, and it does interleave with the `FLATC8n.DAT` ramps and `rgbto8n.dat` conversion tables:

| RT2.PK2 entry | size | name |
|---------------|------|------|
| 1270 | 15,108 | `FLATC16.DAT` (16-bit ramp table) |
| 1271 | 7,556 | `FLATC80.DAT` (8-bit ramp table) |
| 1272 | 768 | `defaul0.pal` |
| 1273 | 32,768 | `rgbto80.dat` |
| 1274…1296 | | `FLATC81.DAT`, `defaul1.pal`, `rgbto81.dat`, … up to `rgbto88.dat` |

`defaul0.pal`'s first 16 entries are the EGA colours followed by a muted
brown/olive/green/ochre terrain ramp — the tiles are dithered, so their high
index counts are genuine, not noise.

## Isometric layout (`thunk_FUN_00417cf0`)

The engine stores, per grid cell, the screen position of its tile:

```
screenX = (x - y) * 64
screenY = (x + y) * 32
```

for its closest zoom; every further zoom halves the vector, so the normal view
steps by `(32, 16)`. A cell's visible diamond is therefore **64 wide by 32
tall**, and the 64x64 sprites are blitted with their top-left at
`(centre - (32, 16))`. Because the lattice is half the sprite size, painting
back to front (increasing `x + y`) leaves exactly each cell's diamond visible.
`TerrainView` implements this; `imbview` renders it to a PPM for comparison.

## Bank selection

`FUN_00510670` decodes each expanded map cell as `bank = (cell >> 3) & 7`,
`tile = cell >> 6`, `orient = cell & 7`, index into the eight banks loaded in
this order: `ground.imb`, `territor.imb`, `rivernan`/`riveran.imb`, `road.imb`,
`tracks.imb`, `trackd.imb`, `groceann`/`groceana.imb`, `grbeacha.imb`.

## Open questions

1. **Class → tile table** (also spec 02's open item). A map's per-cell `+0`
   field holds `(class << 6) | flags` with class up to 392, which is *not* a
   direct `.imb` group index (the biggest bank, `grbeacha.imb`, has 256 groups).
   The engine remaps it while expanding the 10-byte records; that remap is not
   decoded, so `TerrainView` currently picks, per terrain class, the loaded tile
   whose average colour is closest — a documented placeholder.
2. **Group header semantics.** Byte 0 is the *mip mask* (bit 0 = mip 0,
   bit 1 = mip 1) and the palette index is the u32 at +4; the palette gate
   itself is bit 1 of the first *frame* record (see "Container layout").
   The header also carries a zoom -> mip-index table at +0xc, which the
   blitter reads as `*(int *)(group + 0xc + zoom * 8)`. What the remaining
   header bytes mean is still open.
3. **Rotation.** `orient` selects one of four rotated tile vectors; `TerrainView`
   currently draws orientation 0 only.

