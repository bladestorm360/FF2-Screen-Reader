# FF2 Screen Reader Mod

Screen reader accessibility mod for Final Fantasy II Pixel Remaster (MelonLoader + Tolk).

## Critical Rules

1. **Build command**: `cmd //c "D:\Games\Dev\Unity\FFPR\ff2\ff2-screen-reader\build_and_deploy.bat"`
2. **Never load dump.cs** (~490K lines) - use Grep to search
3. **No polling/per-frame patches** - use event-driven hooks only
4. **Update docs after changes** - debug.md for implementation, plan.md for status
5. **Check logs**: Glob `*.log` in `D:\Games\steamlibrary\steamapps\common\FINAL FANTASY II PR\MelonLoader\Logs`

## Documentation

| File | Purpose |
|------|---------|
| [plan.md](docs/plan.md) | Feature status, project overview |
| [debug.md](docs/debug.md) | Implementation details, memory offsets, bug fixes |
| [port.md](docs/port.md) | Features to port from FF3 |

## Directory Structure

```
Core/       Entry point, input handling
Patches/    Harmony patches (one per method)
Menus/      Menu readers, text discovery
Field/      Entity scanning, pathfinding
Utils/      Tolk wrapper, caching, helpers
```

## Code Conventions

```csharp
// IL2CPP type aliases
using FieldMap = Il2Cpp.FieldMap;

// Private field access
BindingFlags.NonPublic | BindingFlags.Instance

// Speech
SpeakText(text, interrupt: true);  // State changes
SpeakText(text, interrupt: false); // Navigation
```

## References

- **FF3 screen reader**: `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader` (primary reference)
- **Game dump**: `D:\Games\Dev\Unity\FFPR\ff2\dump.cs` (search only)
