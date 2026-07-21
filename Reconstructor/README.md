# Reconstructor Plugin for [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver)

Path-following + human-motion aim assist. Builds a guide line through hit objects and gently nudges the cursor toward it with inertial smoothing for natural, human-like movement.

## Reconstructor:

**Strength:** Aim assist intensity. 0 = off, 1 = default, 3 = strong, 5+ = very strong.

**Range:** Activation radius (in note radius multiples). How close the cursor must be before assist engages.

**Curviness:** Guide line curvature. 0 = straight lines between notes, 1 = full curve (smooth flow).

**Max Offset:** Maximum distance the cursor can deviate from raw input. Lower = gentler/more natural, higher = pulls harder (more noticeable).

**Inertia:** Assist inertia (SmoothDamp time). Higher = slower and smoother (more human), lower = faster reaction. Core spike/snap prevention.

**Dead Zone:** Dead zone radius (note radius multiple, 0~1). When the cursor gets within this distance of a note's center, the assist gently fades off. Prevents the cursor from being tugged toward the note center while inside it. 0 = no dead zone, 0.5 = half the note radius (recommended), 1.0 = the entire note radius is a dead zone.

## Requirements

- [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) 0.6.1.0+

## Installation

1. Download the latest release `.zip`.
2. In OpenTabletDriver, open **Plugin Manager** -> **Settings** (gear icon) -> **Install** and select the `.zip` file.
3. Enable the **Reconstructor** plugin under **Filter** or **Output** mode (depending on pipeline position).

## License

Reconstructor is licensed under [GPL-3.0](LICENSE).
