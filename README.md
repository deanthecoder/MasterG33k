# MasterG33k

Shell project for a Sega Master System emulator.

Status: UI scaffold only (main window + about dialog). Core emulation to be added.

## VDP TODO Notes (moved from code comments)

### TODO(6): Line interrupts (H-INT) and H counter
Effect if missing:
- Games relying on per-line splits (for example, Sonic HUD/parallax) will not update mid-frame; layers appear misaligned or scroll uniformly.
- Some engines poll the H counter; returning 0 breaks timing heuristics and can cause logic that "waits until X" to never trigger.

Will fix:
- Mid-screen scroll changes/splits triggered by the line counter (R10) and line interrupt enable will start working; raster effects look correct.
- H counter reads (port 0x7F) will return a plausible horizontal value.

Summary of fix:
- Add an internal line counter register mirroring VDP R10 semantics. Each scanline, decrement; when it reaches zero, if line IRQ enabled (R0 bit 4),
  assert IRQ (set m_interruptPending). Reload the counter from R10 when it triggers. Clear pending IRQ on ReadStatus().
- Implement ReadHCounter() to derive an approximate 0..255 position from m_cycleAccumulator within the current scanline
  (for example, m_cycleAccumulator * 256 / CyclesPerScanline). Keep timing approximate for now but monotonic.
- Integrate with AdvanceCycles/AdvanceScanline so both counters update consistently and IRQ is generated during the active display period.

### TODO(7): Verify sprite base/details
Effect if missing:
- Rare misplacement or clipping of sprites at the left edge; occasional wrong tiles when pattern base nuances are not followed precisely;
  early-clock (-8 px) behavior may be slightly off near X < 8.

Will fix:
- Consistent sprite positions and tiles vs. known-good emulators; correct behavior when early clock is enabled;
  predictable clipping/wrapping at screen boundaries.

Summary of fix:
- Re-check SAT base calculation: SAT base = (R5 & 0x7E) << 7 is correct alignment; verify index/terminator scan.
- Confirm sprite pattern base selection: Mode 4 uses R6 bit 2 -> 0x0000/0x2000. Validate for 8x16 continuity.
- Refine early clock handling (R0 bit 3): maintain -8 shift but ensure proper clipping (no wrap below X < 0) and
  correct visibility for partially off-screen sprites.
- Audit left/right/top/bottom bounds to ensure pixels outside [0..255] x [0..191] are dropped without artifacts.

### TODO(8): CRAM/backdrop accuracy
Effect if missing:
- Colors may look slightly off vs. real hardware (gamma/levels); backdrop color usage for palette index 0 might be subtly incorrect;
  CRAM addressing mirroring nuances may be missed in tests.

Will fix:
- More authentic color reproduction and correct backdrop behavior when color index 0 is selected; better match with palette test ROMs.

Summary of fix:
- Adjust 2-bit-per-channel DAC mapping to a calibrated table (many emulators use {0, 85, 170, 255} or a gamma-adjusted set).
  Keep implementation table-driven.
- Confirm CRAM addressing behavior (32-byte space) and mirroring; ensure writes/reads mask properly and match SMS.
- Ensure color index 0 of both BG palettes uses the backdrop from R7 consistently across all render paths.

### TODO(9): Make pattern-base heuristic configurable
Effect if missing:
- Heuristic may incorrectly choose the alternate pattern region on certain scenes, causing wrong tiles to render when a game intentionally uses
  the register-selected base.

Will fix:
- Stable behavior for correctness-sensitive titles while still allowing a bring-up mode that favors the alternate base when appropriate.

Summary of fix:
- Add a runtime flag/setting to enable/disable the "alternate base if more hits" heuristic. Default to strict register-selected base;
  expose the heuristic as an optional debug/help mode.
