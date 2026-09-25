# FF2-screen-reader

## Purpose

Adds NVDA output, pathfinding, sound queues and other accessibility aides to Final Fantasy II Pixel Remaster.

## Known Issues

When speaking to an NPC with only one keyword in the ask list or opening the key terms menu from main menu with only one keyword, may not be any speech output. Will function normally once 2 or more keywords are obtained.

Shop menus are reading the first highlighted item on both entry and exit.

Items that can not be purchased due to a lack of gil are not reading, either upon highlight or the description by pressing I.

Secret passages, even when opened, do not show properly on the pathfinder. Can use wall bumps and estimation to find, usually near the opening mechanism.

Area transition triggers (area boundary) only announce that the player is moving to a new area. No apparent way to get destination information. Map Exits are named.

Key terms menu reads item names but not descriptions.

May be an issue with shops command menu (buy/sell/equipment/exit) reading shop items instead of menu after backing out of buy or sell menu, needs further testing.

## Install

Create an account at store.steampowered.com, login, join steam.

Once account is created, install steam download app (should be prompted to do so after account creation.)

Log into desktop app.

to purchase games, the easiest way is to use the web interface. You can search for a game when logged into the browser, purchase it there and will be asked if you want to install your games, which opens the desktop app to finish installation.

Ensure you purchase Final Fantasy II, the page should mention being remastered in the description.

Install MelonLoader into game's installation directory. Ensure nightly builds are enabled.
https://github.com/LavaGang/MelonLoader/releases

Copy NVDAControllerClient64.dll, tolk.dll and SDL3.dll into installation directory with game executable, usually c:\\Program Files (x86)\\Steam\\Steamapps\\common\\Final Fantasy II PR. SDL3.dll provides the mod's sounds and controller support.

If you created a steam library on another drive, the path will be Drive Letter\\Path to steam library\\SteamLibrary\\steamapps\\common\\Final Fantasy II PR.

FFII\_screenreader.dll   goes in MelonLoader/mods folder.

## Keys

### Game

- WASD or arrow keys: movement
- Enter: Confirm
- Backspace/escape: cancel
- Q: Toggle between statistics and description in menus that have them.
- f1: toggle between walk and run.
- f3: toggle random encounters on and off.

### Mod

- J and L or \[ and ]: cycle destinations in pathfinder
- Shift+J and L or - and =: change destination categories
- \\ or p: get directions to selected destination
- Shift+\\ or P: Toggle pathfinding filter so that not all destinations are visible, just ones with a valid path.
- Ctrl+\\ or Ctrl+P: Toggle layer transition filter (hides stairs and layer-change destinations from navigation).
- K: announce the currently selected entity again, with its position in the list.
- Backtick (the key above Tab): rescan nearby entities.
- Shift+k: Reset category to all
- ': Toggle footsteps
- ;: toggle wall tones
- f6: toggle audio beacons
- G: Announce current Gil
- M: Announce current map.
- Shift+M: Toggle map exit filter so multiple exits to the same place collapse to the nearest one.
- H: In battle, announce the active character's hp, mp and status effects.
- R: Repeat the current dialogue or message.
- I: In configuration menu accessible from tab menu, read description of highlighted setting. In shop, item, equipment and magic menus, reads the description or stats of the highlighted entry. In battle, reads the description of the highlighted spell or item.
- Shift+I: announce the controls for the current screen.
- U: in shop menus and the equipment menu, confirms that any character can equip the highlighted item (Final Fantasy II has no class restrictions).
- V: Announce active vehicle state.
- Ctrl+Arrow keys: Teleport to direction of selected entity (Ctrl+Up = north of entity, etc.)

### Waypoints (field only)

- , (comma): previous waypoint.
- . (period): next waypoint.
- Shift+, : previous waypoint category.
- Shift+. : next waypoint category.
- / (slash): pathfind to current waypoint.
- Shift+/ : add new waypoint at current location (prompts for name).
- Ctrl+. : rename current waypoint.
- Ctrl+/ : remove current waypoint.
- Ctrl+Shift+/ : clear all waypoints for the current map.

### Other toggles

- f5: in battle, toggle between HP display: full numbers, percentages or no HP display. NO hp display is how the game is intended to be played by the developers.
- f7: Toggle autodetail mode. Reads stats/descriptions of items and spells when navigating instead of pressing I to request the information.
- f8: activate the mod menu where individual sound volume can be adjusted for mod sounds, as well as all togglable options.

### When on a character's status screen, a bestiary entry, or the gamepad/keyboard controls list in the configuration menu

- up and down arrows (or W and S) read through the entries.
- Shift plus arrows: jumps between groups, for example character info, vitals, statistics, combat statistics, weapon skills.
- control plus arrows: jump to beginning or end of the list.

### Game controller

Button names are shown Xbox (PlayStation). Nintendo Pro / Joy-Con labels are also recognized — the controller type is auto-detected.

- Left Stick: movement on field, navigation in menus.
- D-pad: menu navigation only. On the field, D-pad is repurposed by the mod for waypoint cycling (see Mod controller below).
- A (Cross): Confirm.
- B (Circle): Cancel.
- X (Square): Shortcut — statistics/description toggle in menus that have one (keyboard Q equivalent).
- Y (Triangle): Open / close the field menu.
- LB / RB (L1 / R1): Tab switching in menus.
- LT (L2): Page up in non-field menus. On the field, LT is reserved by the mod for pathfinding (see Mod controller).
- RT (R2): Open the pause menu on the field and in battle. In non-field menus, page down.
- L3 (Left Stick Click): the game's walk/run (auto-dash) toggle. R3 (Right Stick Click): the game's random-encounter toggle. Only when Stick-click normalization is enabled in the Mod Menu.

### Mod controller

- Back/Select: Mod mode
- Start/Menu: Mod menu

#### Mod Mode combos (press Back/select, then one of the following)

##### In battle

- X (Square): announce active character HP, MP, and status (keyboard H equivalent).

##### On field and in menus

- X (Square): announce current Gil.
- Y (Triangle): announce current map or location.
- A (Cross): announce active vehicle state.
- Right Stick Up / Down / Left / Right: teleport to the tile next to the selected entity on that side (Up = north of it, etc.; field only), like Ctrl+Arrow keys.

##### While a dialogue or message window is open

- X (Square): repeat the current message (keyboard R equivalent).

#### Stick-click mod actions (preference-controlled in the Mod Menu)

- L3 + R3 together, on the field: toggle stick-click normalization, whichever way it is set. Press both sticks in at once; nothing else happens.
- On the field a single stick click acts when you let go of it, so that it can be part of the L3 + R3 chord.

##### When stick-click normalization is OFF (default)

- L3 (Left Stick Click): toggle audio beacons.
- R3 (Right Stick Click): toggle pathfinding filter.

##### When stick-click normalization is ON

- Back + L3: toggle audio beacons.
- Back + R3: toggle pathfinding filter.
- (L3 / R3 alone become game functions: L3 toggles walk/run, R3 toggles random encounters.)

#### Normal-state mod actions (no Mod button needed)

##### Field — waypoint navigation

- D-pad Up / Down: previous / next waypoint.
- D-pad Left / Right: previous / next waypoint category.

##### Field — entity scanning

- Right Stick Up / Down: previous / next entity.
- Right Stick Left / Right: previous / next entity category.

##### Field — pathfinding

- Left Trigger (L2 / ZL): pathfind to last selected target / restart beacon.

##### In menus (status, bestiary, shop, configuration, and the Mod Menu)

- Right Stick Up: read item details (keyboard I equivalent).
- Right Stick Down: announce context controls / help for the current screen (keyboard Shift+I equivalent).
- Right Stick Left: equipment restriction note in shops and the equipment menu (keyboard U equivalent).
- D-pad Up / Down or Left Stick Up / Down: previous / next entry in the status screen, bestiary detail and controls list.

##### Mod Menu navigation (after Start opens it)

- D-pad or Left Stick Up / Down: navigate items.
- D-pad or Left Stick Left / Right: decrease / increase value.
- A (Cross): toggle / confirm.
- B (Circle) or Start: close the Mod Menu.
