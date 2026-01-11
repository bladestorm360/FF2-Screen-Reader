# FF2 Screen Reader Accessibility Mod

Accessibility mod for Final Fantasy II Pixel Remaster enabling blind/low-vision gameplay via NVDA screen reader using the Tolk library.

## Hard Rules

1. **Never load large files directly into context.** For files 500+ lines or larger than 50KB, use external tools (Grep, Glob, Task agents) to search for patterns and keywords instead of reading the entire file.

2. **Do not attempt to fix failed shell commands unless asked to do so.** If a command fails with unexpected output or no output, this is usually due to the shell mangling commands. Stop and ask the user for input. Once a solution is found, document it in this file or `docs/debug.md` so it can be referenced in future sessions.

3. **Only use build_and_deploy.bat for building.** Never run `dotnet build` directly. Always use: `cmd //c "D:\Games\Dev\Unity\FFPR\ff2\ff2-screen-reader\build_and_deploy.bat"`

4. **Update docs/debug.md after implementing features.** Document what was implemented, how it works, and remaining to-dos. This serves as a running changelog and task tracker.

5. **Always check logs when debugging issues.** Use Glob to find log files (Bash fails with this path):
   ```
   Glob pattern: *.log
   Glob path: D:\Games\steamlibrary\steamapps\common\FINAL FANTASY II PR\MelonLoader\Logs
   ```
   Then Read the most recent log file (highest date in filename like `26-1-11_6-58-44.log`).

6. **Wait for explicit approval when asked for a summary or plan.** When the user asks for a "summary for approval", "plan for approval", or similar phrasing, present the summary/plan and STOP. Do not proceed with any implementation, code changes, builds, or other actions until the user explicitly approves. Research and investigation are allowed, but no changes.

## Architecture

- **Framework**: MelonLoader (net6.0) + HarmonyLib for method patching
- **Screen Reader**: Tolk.dll wrapper for NVDA/JAWS/Narrator
- **Game Interop**: IL2CppInterop for Unity IL2CPP access

### Directory Structure
```
Core/           # Main mod entry, input handling, entity filters
Patches/        # HarmonyLib patches for game method interception
Menus/          # Menu-specific readers and text discovery
Field/          # Field navigation, entity scanning, pathfinding
Utils/          # Tolk wrapper, text utilities, caching
```

## Core Features

### Navigation & Pathfinding
- Entity scanning (NPCs, chests, exits, events)
- Directional announcements with distance (N/S/E/W, steps)
- Category filtering (cycle through entity types)
- A* pathfinding to reachable entities
- Wall/collision detection feedback

### Menu Accessibility
- All menu cursor movements announced
- Character stats with FF2's unique growth system
- Equipment menus with stat comparisons
- Item inventory with descriptions
- Save/load slot reading with location and playtime

### Battle System
- Turn announcements ("Character's turn")
- Command selection with descriptions
- Target selection with enemy HP/status
- Damage/healing results
- Battle outcome (victory, defeat, rewards)
- Spell/ability level announcements

### FF2-Specific Features
- **Stat Growth System**: HP/MP grow through damage taken; weapon skills level through use; spell levels increase through casting
- **Keyword System**: Learn keywords from NPCs, use in dialogue (Ask/Item/Memorize)
- **Character Rotation**: Party members join/leave throughout story
- **No Traditional Leveling**: Stats grow organically, not via XP

## Rules & Conventions

### IL2CPP Interop
- Always use reflection for private fields: `BindingFlags.NonPublic | BindingFlags.Instance`
- Wrap all IL2CPP calls in try-catch blocks
- Use type aliases for common game types:
  ```csharp
  using FieldMap = Il2Cpp.FieldMap;
  using UserDataManager = Il2CppLast.Management.UserDataManager;
  ```

### Speech Output
- Use `interrupt: true` for important state changes (turn start, scene transitions)
- Use `interrupt: false` for continuous navigation (target cycling, menu browsing)
- Strip icon tags from text: `<IC_*>` patterns

### Harmony Patches
- One patch class per patched method
- Naming: `ClassName_Method_Patch`
- Prefer `[HarmonyPostfix]` over Prefix when possible
- Use `[HarmonyPatch(typeof(Class), nameof(Class.Method))]` for clarity

**Note**: Final Fantasy III required manual Harmony patching (runtime `Harmony.Patch()` calls) instead of attribute-based patching to avoid crashing the game. The same may be true for Final Fantasy II. If attribute-based patches cause crashes, switch to manual patching.

### Code Style
- Static helper classes for menu readers
- GameObjectCache for expensive FindObjectOfType calls
- MelonLogger for debugging, never Debug.Log
- Null-check all IL2CPP object access

## Key Hotkeys (Configurable)
- Entity scan/cycle (field navigation)
- Category filter toggle
- Repeat last announcement
- Announce current position/location
- Announce party status

## Dependencies
- MelonLoader.dll
- Il2CppInterop.Runtime.dll
- 0Harmony.dll
- Assembly-CSharp.dll (game)
- UnityEngine modules
- Tolk.dll (native)

## Reference Projects
- `ff3-screen-reader`: Primary reference, similar FFPR architecture
- `ff5-screen-reader`: Secondary reference, advanced waypoint/vehicle systems

## Game Metadata (ff2 folder)
The parent `ff2/` folder contains IL2CPP dump files from the game:
- **dump.cs**: ~490K line IL2CPP class dump - search with Grep, never load directly
- **il2cpp.h**: C++ headers for IL2CPP types
- **script.json**: Script metadata
- **stringliteral.json**: String literal references
- **DummyDll/**: Dummy assemblies for reference

## Build
```batch
cmd //c "D:\Games\Dev\Unity\FFPR\ff2\ff2-screen-reader\build_and_deploy.bat"
```
Output: `Building...` `Deploying...` `Done.`
Logs are written to `build_log.txt`. Mod deploys to `FINAL FANTASY II PR\Mods\`.
