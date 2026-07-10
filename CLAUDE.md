# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **Disco Elysium Accessibility Mod** that adds screen reader support and keyboard navigation to the game. The mod uses **MelonLoader** (a Unity game modding framework) and **Harmony** (for runtime patching) to inject accessibility features into the game.

Key technologies:
- **MelonLoader**: Unity modding framework for .NET 6.0
- **Il2Cpp interop**: Required for Unity IL2CPP builds
- **Tolk**: Screen reader integration library
- **Harmony**: Runtime method patching for game hooks

## Build Commands

### Environment Setup
The mod requires `DISCO_ELYSIUM_PATH` environment variable pointing to the game installation:
```bash
export DISCO_ELYSIUM_PATH="/mnt/c/Program Files (x86)/Steam/steamapps/common/Disco Elysium"
```

### Build Commands
```bash
# Build and auto-copy to game (if DISCO_ELYSIUM_PATH is set)
./mod/build.sh

# Manual build
cd mod && dotnet build AccessibilityMod.csproj --configuration Release

# Test the build
dotnet build mod/AccessibilityMod.csproj --configuration Release
```

The build automatically copies the resulting DLL to `$DISCO_ELYSIUM_PATH/Mods/` if the environment variable is set.

## Architecture Overview

The mod follows a **modular system architecture** with clear separation of concerns:

### Core Systems (mod/ directory)
- **AccessibilityMod.cs**: Main entry point, initializes all subsystems and handles MelonLoader lifecycle
- **TolkScreenReader.cs**: Screen reader integration (NVDA, JAWS, SAPI fallback)

### Navigation System (mod/Navigation/)
- **SmartNavigationSystem.cs**: Central navigation coordinator with categorized object selection
- **NavigationStateManager.cs**: Manages current selection and object categories  
- **MovementController.cs**: Automated pathfinding and player movement
- **ObjectCategorizer.cs**: Classifies game objects (NPCs, locations, containers, etc.)

### Input Handling (mod/Input/)
- **InputManager.cs**: Centralized keyboard input processing with accessibility hotkeys - dispatches via `KeyBindings.IsPressed(GameKey.X)` rather than hardcoded `UnityEngine.Input.GetKeyDown(KeyCode...)` calls

### Settings (mod/Settings/)
- **GameKey.cs / KeyBindings.cs**: Every hotkey is a remappable `GameKey`, persisted via MelonPreferences to `UserData/AccessibilityMod.cfg` (`[KeyBindings]` category, one `"UnityKeyCodeName|RequireCtrl|RequireAlt|RequireShift"` string per action). Two built-in presets: the original US-QWERTY punctuation bindings (`Defaults`), and a layout-independent alternative (`NumpadSafePreset`) that reuses stardew-access's conventions (Escape/PageUp/PageDown/Ctrl+Home) where an equivalent exists and falls back to the numpad for the rest - punctuation keys like `[`, `]`, `\` move or require AltGr on non-US layouts (e.g. German QWERTZ), which was the root cause of the mod being effectively unusable there.
- **AccessibilityPreferences.cs**: Dialog reading mode / orb announcements / speech interrupt, same cfg file, `[AccessibilityMod]` category.

### UI Integration (mod/UI/)
- **UINavigationHandler.cs**: Detects and announces UI element selection
- **UIElementFormatter.cs**: Formats UI elements for screen reader output
- **DialogStateManager.cs**: Manages dialog reading modes

### Game Integration (mod/Patches/)
- **InteractableSelectionPatches.cs**: Harmony patches for game's interaction system
- **DialogSystemPatches.cs**: Patches for dialog system accessibility
- **OrbTextVocalizationPatches.cs**: Patches for skill check announcements
- **NotificationVocalizationPatches.cs**: Patches for game notifications
- **InventoryPatches.cs**: Patches for inventory accessibility

### Inventory System (mod/Inventory/)
- **InventoryNavigationHandler.cs**: Keyboard navigation for inventory screens

### Utilities (mod/Utils/)
- **GameObjectUtils.cs**: Game object discovery and player position detection
- **ObjectNameCleaner.cs**: Cleans up technical object names for user-friendly announcements
- **DirectionCalculator.cs**: Spatial navigation utilities

## Key Accessibility Features

### Navigation Hotkeys
All hotkeys are remappable (see Settings above); this lists the two built-in presets.

Default (original, US-QWERTY):
- **[** - Select NPCs category
- **]** - Select locations category  
- **\\** - Select containers/loot category
- **=** - Select everything category
- **.** - Cycle within current category
- **,** - Navigate to selected object
- **/** - Stop automated movement
- **;** - Full scene object scan
- **'** - Distance-based scene scan
- **`** - Announce current UI selection
- **-** - Toggle dialog reading mode

Layout-safe preset (recommended for non-US keyboards, or laptops without a numpad for the four navigation keys):
- **Numpad 1/2/3/0** - Select NPCs/locations/loot/everything category
- **Escape** - Stop automated movement
- **Page Down / Page Up** - Cycle category forward/backward
- **Ctrl+Home** - Navigate to selected object
- **Numpad 7/8/6** - Full scene scan / distance scan / announce current selection
- **Numpad -** - Toggle dialog reading mode

### Keybind Editor Tool (tools/KeybindEditor)
A standalone WinForms app (not part of the mod DLL) for editing `UserData/AccessibilityMod.cfg` without launching the game: pick a game folder, load a preset or freely rebind any action to any key, edit the dialog reading mode / orb announcements / speech interrupt settings, save. Localized (English/German). Requires the mod to have been built and installed at least once so its `GameKey` list conceptually matches (the tool's `GameKeyCatalog.cs` is a hand-kept mirror of `mod/Settings/KeyBindings.cs`'s `Defaults`/`NumpadSafePreset` - keep both in sync when adding/renaming a `GameKey`). Build: `cd tools/KeybindEditor && dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`.

### Object Categories
The system categorizes all interactable objects into logical groups:
- **NPCs**: Characters and dialogue targets
- **Locations**: Doors, exits, area transitions
- **Loot**: Containers, items, skill orbs
- **Everything**: All available objects

## Game Integration Points

### Registry Access
The mod taps into `MouseOverHighlight.registry` to access all scene objects, bypassing the game's limited interaction range.

### Character Control Integration  
Uses `CharacterAnalogueControl` and its `InteractableSelectionManager` for player movement and object interaction.

### Unity UI Integration
Monitors `UnityEngine.EventSystems` and `Selectable` components to announce menu navigation.

## Development Workflow

### Testing
The game must be launched with MelonLoader installed to test the mod. Console output appears in MelonLoader's console window (usually F4).

### Debugging
- MelonLoader console shows all debug output
- Enable detailed logging in `AccessibilityMod.cs` if needed
- Use `;` key in-game to test object registry access
- Use `'` key to test distance calculations

### Game Assembly References
The mod references Il2Cpp-generated assemblies from the game installation. These are located at:
- `$DISCO_ELYSIUM_PATH/MelonLoader/Il2CppAssemblies/`
- `$DISCO_ELYSIUM_PATH/MelonLoader/net6/`

## Important Implementation Notes

### Screen Reader Integration
All user-facing announcements go through `TolkScreenReader.Instance.Speak()` which handles:
- Multiple screen reader compatibility (NVDA, JAWS, etc.)
- SAPI fallback when no screen reader is detected
- **Braille display support**: Automatically outputs to both speech and braille displays via `Tolk.Output()`

### Il2Cpp Considerations
- Use `Il2Cpp*` prefixed types for game objects (e.g., `Il2CppTMPro.TextMeshProUGUI`)
- Unity interop requires special handling for some operations
- Game assemblies are IL2CPP-compiled, not standard .NET

### Performance
- Object scanning is optimized to avoid frame drops
- Distance calculations are cached where possible  
- UI monitoring uses minimal overhead polling

### Game Compatibility
Designed for **Disco Elysium - The Final Cut** with MelonLoader. The mod specifically targets the game's interaction and UI systems, so changes to game updates may require mod updates.
