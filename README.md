# MasterG33k

Shell project for a **Sega Master System** emulator.

Status: **Core emulation in progress**. The emulator is now far enough along to run real games, but several hardware behaviours (notably VDP interrupts, sprite evaluation edge cases, and paging nuances) are still incomplete or approximate.

---

## Current Behaviour (Bring-up Notes)

Working / partially working:
- **Columns**
  - Reference emulators show a populated tile map containing the full COLUMNS logo.
  - Current implementation produces a mostly black tile map, suggesting sprite pixel or palette handling issues.

Known issues:
- **Sonic the Hedgehog**
  - Both background and foreground sprites show corruption (missing pixels).
  - Background scroll direction appears wrong (or just not implemented) when Sonic moves left/right.
- **Altered Beast**
  - Intro/title appears, but the game does not progress further.
- Pressing **Z** (mapped to Button 1 / Start) appears to reset the emulator.
- **Columns (title screen)**

These issues are likely a combination of missing VDP behaviour (line interrupts, timing), sprite/palette bugs, paging edge cases, and plain bugs.

---

## Debugging & Diagnostics (Immediate Focus)

Improving diagnostics is the current priority, as it will unblock several rendering and gameplay issues more quickly than guessing at fixes.

### Planned Debug Snapshot Bundle

Add a UI-accessible debug option to capture a **snapshot bundle** consisting of:

- A binary machine snapshot (`.sav`) containing full emulated state
- A sidecar JSON file with human-readable diagnostics

This allows broken scenes to be captured, inspected offline, and turned into regression tests.

#### Snapshot (`.sav`) should capture:
- CPU registers, flags, IM/IFF state, pending interrupts
- RAM and paging state
- VDP registers, status flags, internal counters
- VRAM and CRAM
- Based on same feature in the G33kBoy repo.

#### Diagnostic JSON (`.json`) should include:
- ROM name and hash
- CPU tick count
- Framebuffer hash
- Decoded paging state (which banks mapped where)
- Key decoded VDP registers (mode bits, name table base, SAT base, pattern base, scroll values)

### Background Tile Map Dump (Initial Focus)

As an initial, lightweight diagnostic, record **background tile data only** (ignore sprite layer for now):

- Dump a grid representing the background tilemap (typically 32×24 visible tiles)
- For each tile, record:
  - tile index (A0..A8)
  - attribute bits (palette select, priority, H/V flip)

Example conceptual format (tile indices):
```
01 01 01 01 04 05 02 01 ...
...
```

Attributes can be stored in parallel (either as decoded flags or raw bytes). This is expected to immediately highlight issues such as:
- incorrect name table base
- palette select errors
- tiles resolving to index 0 unexpectedly ("mostly black" exports)

This mirrors behaviour observed in reference emulators where the **Columns** title screen tilemap clearly shows the full logo composition.

### Example Diagnostic JSON

Below is a rough example of the intended sidecar JSON file. Exact fields are flexible; the goal is to make state *inspectable at a glance*.

```json
{
  "rom": {
    "name": "Columns (World)",
    "sha1": "3f2a1e0b8d0c9a5d9c6e7a0e4f9d2c7a1b8e1234"
  },
  "ticks": 18432,
  "paging": {
    "frame0": 0,
    "frame1": 2,
    "frame2": 5,
    "frame2Mode": "ROM"
  },
  "vdp": {
    "mode": 4,
    "nameTableBase": "0x3800",
    "patternBase": "0x0000",
    "spriteAttributeBase": "0x3F00",
    "scroll": {
      "x": 0,
      "y": 0
    }
  },
  "background": {
    "width": 32,
    "height": 24,
    "tiles": [
      [ { "tile": 1, "pal": 0, "pri": 0, "h": 0, "v": 0 }, { "tile": 1, "pal": 0, "pri": 0, "h": 0, "v": 0 } ],
      [ { "tile": 4, "pal": 1, "pri": 0, "h": 0, "v": 0 }, { "tile": 5, "pal": 1, "pri": 0, "h": 0, "v": 0 } ]
    ]
  }
}
```

Notes:
- `tiles` is shown truncated for brevity; real dumps would include the full grid.
- Attributes can be stored decoded (as above) or as raw bytes if preferred.
- This JSON is intentionally redundant and verbose to favour debugging clarity over size.


---

## Ordered TODO List

0. **Debugging Improvements**

Planned diagnostic logging additions:
- CPU interrupt accept/acknowledge (IFF state, IM mode, vector source)
- VDP events: VBlank, line interrupt trigger/reload
- VDP status reads and flag clears
- Memory paging writes (`0xFFFC–0xFFFF`)

Goal: correlate visual or gameplay faults with concrete hardware-level events.

1. **Add diagnostic snapshot bundle support**
   - `.sav` + `.json` output
   - frame hash naming

2. **Add background tilemap dump as ASCII to Console**
   - tile indices + attributes
   - visible area at minimum

3. **Improve logging around VDP + interrupts**
   - line interrupt trigger/reload
   - VDP status reads and clears
   - CPU interrupt accept/ack

4. **Fix VDP line interrupts (H-INT)**
   - required for Sonic HUD / raster effects

5. **Tighten sprite evaluation edge cases**
   - 8-per-scanline rule
   - early-clock handling

6. **Resolve paging corner cases**

7. **Add sound support**

---

## Hardware Notes (Collated)

### Memory Map and Paging

Typical Master System memory layout:
- `0x0000–0x03FF` Start-up code and interrupt handlers
- `0x0400–0x3FFF` ROM Frame 0 (15KB)
- `0x4000–0x7FFF` ROM Frame 1 (16KB)
- `0x8000–0xBFFF` Frame 2 (16KB, ROM or cartridge RAM)
- `0xC000–0xDFFF` 8KB system RAM
- `0xE000–0xFFFF` Mirror of 8KB RAM

Paging is controlled by writes to:
- `0xFFFC` – Frame 2 control
- `0xFFFD` – Frame 0 page
- `0xFFFE` – Frame 1 page
- `0xFFFF` – Frame 2 page

Frame 2 control (`0xFFFC`) key bits:
- `bit 3 (0x08)` ROM/RAM select for Frame 2
- `bit 2 (0x04)` RAM page select (0 or 1) when RAM is selected

## Incremental Test ROMs

Useful ROMs for validating display and VDP behaviour (simplest first):
- OnlyWords
- 64 Color Palette Test Program
- SMSPalette
- Amiga Boing Demo
- SMSSoundTest

---

## References

- Java Master System emulator source: https://www.zophar.net/download_file/12497
- JavaGear emulator development report: https://www.smspower.org/uploads/Development/JavaGear-Report.pdf

---

## VDP TODO Notes (from code comments)

### TODO: Line interrupts (H-INT) and H counter

Effect if missing:
- Games relying on per-line splits (e.g. Sonic HUD/parallax) will not update mid-frame.
- Engines polling the H counter may stall if it always returns 0.

Summary of fix:
- Implement an internal line counter mirroring VDP register R10.
- Decrement per scanline; on expiry and if enabled (R0 bit 4), assert an interrupt.
- Reload counter from R10 when triggered.
- Clear pending interrupt on status register read.
- Implement an approximate but monotonic H counter based on cycles within the scanline.

---

### TODO: Verify sprite base and evaluation details

Effect if missing:
- Sprite misplacement or clipping near the left edge.
- Incorrect tile selection in some modes.

Summary of fix:
- Verify SAT base calculation: `(R5 & 0x7E) << 7`.
- Confirm sprite pattern base selection via R6 bit 2.
- Refine early-clock (-8 px) handling and clipping.
- Ensure pixels outside `[0..255] x [0..191]` are dropped cleanly.

---

### TODO: CRAM and backdrop accuracy

Effect if missing:
- Slight colour inaccuracies.
- Incorrect backdrop colour usage when palette index 0 is selected.

Summary of fix:
- Use a calibrated DAC mapping table for colour conversion.
- Verify CRAM addressing and mirroring behaviour.
- Ensure backdrop colour (R7) is applied consistently.

---

### TODO: Make pattern-base heuristic configurable

Effect if missing:
- Heuristic pattern-base selection may produce incorrect tiles in some scenes.

Summary of fix:
- Add a runtime flag to enable/disable heuristic base selection.
- Default to strict register-selected behaviour.

---

## Snapshot / State Capture Idea

Introduce a **system snapshot** feature (as used previously in G33kBoy) to capture broken states and turn them into regression tests.

Proposed workflow:
1. Capture a snapshot when rendering or behaviour is incorrect.
2. Name snapshot using a hash of the rendered frame.
3. Regression test:
   - load snapshot
   - run N frames
   - assert resulting frame hash differs from the broken hash

Once fixed, stronger tests can assert equality with a known-good frame hash.

Snapshots should capture:
- CPU registers and interrupt state
- RAM and paging state
- VDP registers, VRAM, CRAM, and internal counters

---

## General Notes

- The emulator has reached a stage where multiple commercial titles partially run, providing a solid base for incremental correctness work.
- Many observed issues (sprite corruption, incorrect scrolling, black tilemaps) are expected to become easier to diagnose once background tile and paging state can be inspected directly.
- Snapshot-based debugging proved highly effective in G33kBoy and is expected to serve a similar role here.

