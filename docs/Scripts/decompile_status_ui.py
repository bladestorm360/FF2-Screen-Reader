# Ghidra headless script to decompile FF2 Status Screen UI functions
# Compatible with Jython 2.7 (Ghidra's Python interpreter)
# Parses il2cpp.h for type information before decompiling
#
# FF2-specific: Status screen displays character stats, weapon skills, and combat parameters.
# This script targets UI controllers and views that display stat values.
# Goal: Understand how weapon skill levels and combat stat counts are read/displayed.

from ghidra.app.decompiler import DecompInterface
from ghidra.app.util.cparser.C import CParser
from ghidra.program.model.data import DataTypeConflictHandler
from ghidra.util.task import ConsoleTaskMonitor
from ghidra.program.model.symbol import SourceType
import codecs
import json
import os

# Target functions to decompile (RVA -> name mapping)
# These are Relative Virtual Addresses - image base will be added at runtime
TARGET_FUNCTIONS_RVA = {
    # ============================================================
    # SkillLevelContentController - Weapon skill UI (TypeDefIndex: 5332)
    # Each instance displays one weapon skill (Sword, Knife, etc.)
    # weaponType field at offset 0x20 identifies which skill
    # ============================================================
    0x3DBDA0: "SkillLevelContentController$$Initialize",
    0x3DC460: "SkillLevelContentController$$SetWeaponIcon",
    0x3DC7D0: "SkillLevelContentController$$UpdateView_OwnedCharacterData",
    0x3DC1F0: "SkillLevelContentController$$SetSkillLevelTargetType",
    0x3DBEA0: "SkillLevelContentController$$SetAbilityIcon",
    0x3DC2E0: "SkillLevelContentController$$SetSkillLevelUpColor",

    # ============================================================
    # ParameterContentController (KeyInput) - Combat stat UI (TypeDefIndex: 9325)
    # Displays stats like Accuracy, Evasion, Magic Defense in "Nx Y%" format
    # type field at offset 0x18 identifies which parameter
    # ============================================================
    0x5B07D0: "KeyInput_ParameterContentController$$Initialize",
    0x5B0970: "KeyInput_ParameterContentController$$SetData",
    0x5B0930: "KeyInput_ParameterContentController$$SetCountValue",
    0x5B0A10: "KeyInput_ParameterContentController$$SetEnablePercentText",
    0x5B09D0: "KeyInput_ParameterContentController$$SetEnableCountText",

    # ============================================================
    # ParameterContentController (Touch) - Combat stat UI (TypeDefIndex: 8597)
    # Same as KeyInput but different RVAs for some methods
    # ============================================================
    0x927A70: "Touch_ParameterContentController$$Initialize",
    0x927BD0: "Touch_ParameterContentController$$SetCountValue",

    # ============================================================
    # ParameterContentView (KeyInput) - Combat stat view (TypeDefIndex: 9326)
    # Contains Text components: multipliedValueText (0x28), percentText (0x38)
    # ============================================================
    0x5B0CA0: "KeyInput_ParameterContentView$$Initialize",
    0x3CD360: "KeyInput_ParameterContentView$$SetCountText",
    0x2C9D10: "KeyInput_ParameterContentView$$SetParameterText",
    0x3CD9A0: "KeyInput_ParameterContentView$$UseMultipliedValueText",
    0x5B0E30: "KeyInput_ParameterContentView$$UseMultipliedText",
    0x5B0F00: "KeyInput_ParameterContentView$$UsePercentText",

    # ============================================================
    # BattleUtility - Core stat calculation (TypeDefIndex: varies)
    # GetSkillLevel is the main function we use for weapon skill levels
    # ============================================================
    0x913900: "BattleUtility$$GetSkillLevel",
    0x911F70: "BattleUtility$$GetJobLevel",
    0x911410: "BattleUtility$$GetDominationAttack",

    # ============================================================
    # StatusDetailsController (KeyInput) - Main status screen (TypeDefIndex: 5453)
    # Contains skillLevelContentList at offset 0x80
    # ============================================================
    0x3DCAA0: "KeyInput_StatusDetailsController$$Initialize",
    0x3DD1B0: "KeyInput_StatusDetailsController$$UpdateView",
    0x3DCA20: "KeyInput_StatusDetailsController$$InitDisplay",
    0x3DCE90: "KeyInput_StatusDetailsController$$UpdateDisplay",

    # ============================================================
    # StatusDetailsController (Touch) - Main status screen (TypeDefIndex: 5373)
    # Contains skillLevelContentList at offset 0x78
    # ============================================================
    0x6A3740: "Touch_StatusDetailsController$$Initialize",
    0x6A3BA0: "Touch_StatusDetailsController$$UpdateView",
    0x6A3560: "Touch_StatusDetailsController$$InitDisplay",

    # ============================================================
    # CommonGauge - Progress bar UI (TypeDefIndex: 7871)
    # gaugeImage field at offset 0x18 contains fillAmount (0.0-1.0)
    # ============================================================
    0x5335A0: "CommonGauge$$SetValue",

    # ============================================================
    # SkillLevelContentView - Weapon skill view (TypeDefIndex: 5333)
    # Contains: iconText (0x18), levelText (0x20), gauge (0x28)
    # ============================================================
    # Note: SkillLevelContentView methods are simple getters, main logic is in controller
}

# ParameterType enum values (TypeDefIndex: 6023) for reference:
# AccuracyCount = 202
# EvasionCount = 204
# MagicDefenseCount = 24
# AccuracyRate = 16
# EvasionRate = 17
# AbilityEvasionRate = 13
# Attack = 10
# Defense = 11

# SkillLevelTarget enum values (TypeDefIndex: 6032) for reference:
# WeaponSword = 0
# WeaponKnife = 1
# WeaponSpear = 2
# WeaponAxe = 3
# WeaponCane = 4 (Staff)
# WeaponBow = 5
# WeaponShield = 6
# WeaponWrestle = 7 (Bare Hands/Unarmed)

# Paths
OUTPUT_PATH = "D:\\Games\\Dev\\Unity\\FFPR\\ff2\\ff2-screen-reader\\docs\\Scripts\\decompiled_status_ui.c"
SCRIPT_JSON_PATH = "D:\\Games\\Dev\\Unity\\FFPR\\ff2\\script.json"
IL2CPP_HEADER_PATH = "D:\\Games\\Dev\\Unity\\FFPR\\ff2\\il2cpp_ghidra.h"

def parse_il2cpp_header(program):
    """Parse il2cpp_ghidra.h and apply types to the program's data type manager."""
    if not os.path.exists(IL2CPP_HEADER_PATH):
        print("WARNING: il2cpp_ghidra.h not found at: " + IL2CPP_HEADER_PATH)
        return False

    print("Parsing IL2CPP header: " + IL2CPP_HEADER_PATH)
    print("This may take a few minutes for large headers...")

    try:
        # Get the program's data type manager
        dtm = program.getDataTypeManager()

        # Read the header file content
        print("Reading header file...")
        with open(IL2CPP_HEADER_PATH, 'r') as f:
            header_content = f.read()

        print("Header size: " + str(len(header_content)) + " bytes")
        print("Starting C parser...")

        # Create C parser with the program's data type manager
        parser = CParser(dtm)

        # Parse the header content as a string
        try:
            parsed_dtm = parser.parse(header_content)

            if parsed_dtm is not None:
                print("Parsing completed, applying types to program...")
                iterator = dtm.getAllDataTypes()
                count = 0
                while iterator.hasNext():
                    iterator.next()
                    count += 1

                print("Data type manager now has " + str(count) + " types")
                return True
            else:
                print("Parser returned None - types may have been added directly to DTM")
                return True

        except Exception as parse_error:
            error_str = str(parse_error)
            print("C Parser error: " + error_str)

            if "line" in error_str.lower():
                print("This may be a syntax error in the header file.")
                print("Consider editing il2cpp_ghidra.h to fix or comment out the problematic section.")

            # Try alternative method
            print("Attempting alternative parsing method...")
            try:
                from ghidra.app.util.cparser.C import CParserUtils
                from ghidra.app.util import MessageLog
                log = MessageLog()

                file_list = [IL2CPP_HEADER_PATH]
                include_paths = []

                CParserUtils.parseHeaderFiles(dtm, file_list, include_paths, log, ConsoleTaskMonitor())

                if log.hasMessages():
                    print("Parser messages: " + log.toString())

                print("Alternative parsing completed")
                return True
            except Exception as alt_error:
                print("Alternative parsing also failed: " + str(alt_error))
                return False

    except Exception as e:
        print("Error parsing il2cpp_ghidra.h: " + str(e))
        import traceback
        traceback.print_exc()
        return False

def apply_il2cpp_symbols(program):
    """Apply IL2CPP symbol names from script.json."""
    if not os.path.exists(SCRIPT_JSON_PATH):
        print("script.json not found at: " + SCRIPT_JSON_PATH)
        return 0

    print("Loading IL2CPP symbols from: " + SCRIPT_JSON_PATH)
    try:
        with codecs.open(SCRIPT_JSON_PATH, 'r', 'utf-8') as f:
            data = json.load(f)

        symbol_table = program.getSymbolTable()
        address_factory = program.getAddressFactory()
        image_base = program.getImageBase().getOffset()
        applied = 0

        if "ScriptMethod" in data:
            for method in data["ScriptMethod"]:
                addr = method.get("Address")
                name = method.get("Name")
                if addr and name:
                    for target_rva, target_name in TARGET_FUNCTIONS_RVA.items():
                        if name == target_name or name.replace(".", "$$") == target_name:
                            try:
                                ghidra_addr = address_factory.getDefaultAddressSpace().getAddress(image_base + addr)
                                clean_name = name.replace("$$", "__").replace("<", "_").replace(">", "_").replace(",", "_")
                                symbol_table.createLabel(ghidra_addr, clean_name, SourceType.IMPORTED)
                                applied += 1
                            except Exception as e:
                                pass

        print("Applied " + str(applied) + " IL2CPP symbols")
        return applied
    except Exception as e:
        print("Error loading script.json: " + str(e))
        return 0

def decompile_function_at_address(decompiler, program, rva, name):
    """Decompile function at given RVA and return C code."""
    address_factory = program.getAddressFactory()
    image_base = program.getImageBase().getOffset()
    abs_addr = image_base + rva

    try:
        ghidra_addr = address_factory.getDefaultAddressSpace().getAddress(abs_addr)
        func = getFunctionAt(ghidra_addr)

        if func is None:
            print("    Creating function at 0x{:X}...".format(abs_addr))
            func = createFunction(ghidra_addr, name.replace("$$", "_"))
            if func is None:
                return None, "Could not create function at 0x{:X}".format(abs_addr)

        results = decompiler.decompileFunction(func, 120, ConsoleTaskMonitor())

        if results.decompileCompleted():
            decomp_func = results.getDecompiledFunction()
            if decomp_func:
                return decomp_func.getC(), None
            else:
                return None, "Decompilation returned no result"
        else:
            error_msg = results.getErrorMessage()
            if error_msg:
                return None, "Decompilation failed: " + str(error_msg)
            else:
                return None, "Decompilation failed (unknown error)"

    except Exception as e:
        return None, "Exception: " + str(e)

def run():
    """Main script entry point."""
    print("=" * 70)
    print("FF2 Status Screen UI Decompiler")
    print("=" * 70)

    program = getCurrentProgram()
    if program is None:
        print("ERROR: No program loaded!")
        return

    image_base = program.getImageBase().getOffset()
    print("Program: " + program.getName())
    print("Image Base: 0x{:X}".format(image_base))
    print("Output: " + OUTPUT_PATH)
    print("")

    # Step 1: Parse IL2CPP header for type information
    print("-" * 70)
    print("STEP 1: Parsing IL2CPP type definitions")
    print("-" * 70)
    types_parsed = parse_il2cpp_header(program)
    if types_parsed:
        print("Type parsing completed successfully")
    else:
        print("Type parsing failed or skipped - decompilation will use generic types")
    print("")

    # Step 2: Apply symbol names from script.json
    print("-" * 70)
    print("STEP 2: Applying IL2CPP symbol names")
    print("-" * 70)
    apply_il2cpp_symbols(program)
    print("")

    # Step 3: Decompile target functions
    print("-" * 70)
    print("STEP 3: Decompiling target functions")
    print("-" * 70)
    print("Initializing decompiler...")
    decompiler = DecompInterface()
    decompiler.openProgram(program)

    results = []
    results.append("/*")
    results.append(" * FF2 Decompiled Functions - Status Screen UI System")
    results.append(" * Generated by Ghidra headless analysis")
    results.append(" * Program: " + program.getName())
    results.append(" * Image Base: 0x{:X}".format(image_base))
    results.append(" * IL2CPP types applied: " + str(types_parsed))
    results.append(" *")
    results.append(" * Purpose: Understanding FF2's status screen UI for screen reader accessibility")
    results.append(" *")
    results.append(" * Key Classes:")
    results.append(" *   - SkillLevelContentController: Displays weapon skill level + progress bar")
    results.append(" *   - SkillLevelContentView: Contains levelText (0x20), gauge (0x28)")
    results.append(" *   - ParameterContentController: Displays combat stats (Accuracy, Evasion, etc.)")
    results.append(" *   - ParameterContentView: Contains multipliedValueText (0x28), percentText (0x38)")
    results.append(" *   - StatusDetailsController: Main status screen, owns skillLevelContentList")
    results.append(" *   - CommonGauge: Progress bar with gaugeImage.fillAmount (0.0-1.0)")
    results.append(" *   - BattleUtility: Static methods for stat calculations")
    results.append(" *")
    results.append(" * Memory Offsets (from dump.cs):")
    results.append(" *   SkillLevelContentController: view=0x18, weaponType=0x20")
    results.append(" *   SkillLevelContentView: iconText=0x18, levelText=0x20, gauge=0x28")
    results.append(" *   ParameterContentController: type=0x18, subType=0x1C, view=0x20")
    results.append(" *   ParameterContentView: fixedText=0x18, multipliedText=0x20,")
    results.append(" *                         multipliedValueText=0x28, parameterValueText=0x30, percentText=0x38")
    results.append(" *   CommonGauge: gaugeImage=0x18")
    results.append(" *")
    results.append(" * ParameterType enum values:")
    results.append(" *   AccuracyCount=202, EvasionCount=204, MagicDefenseCount=24")
    results.append(" *   AccuracyRate=16, EvasionRate=17, AbilityEvasionRate=13")
    results.append(" *")
    results.append(" * SkillLevelTarget enum values:")
    results.append(" *   Sword=0, Knife=1, Spear=2, Axe=3, Cane=4, Bow=5, Shield=6, Wrestle=7")
    results.append(" */")
    results.append("")

    success_count = 0
    fail_count = 0

    # Group functions by class for better organization
    current_class = ""
    for rva, name in sorted(TARGET_FUNCTIONS_RVA.items(), key=lambda x: x[1]):
        abs_addr = image_base + rva

        # Extract class name for grouping
        class_name = name.split("$$")[0] if "$$" in name else "Unknown"
        if class_name != current_class:
            current_class = class_name
            results.append("")
            results.append("/" + "=" * 68 + "/")
            results.append("/* " + class_name)
            results.append(" " + "=" * 67 + "/")

        print("")
        print("Decompiling: " + name)
        print("  RVA: 0x{:X} -> Absolute: 0x{:X}".format(rva, abs_addr))

        code, error = decompile_function_at_address(decompiler, program, rva, name)

        results.append("")
        results.append("/" + "*" * 68 + "/")
        results.append("/* " + name)
        results.append(" * RVA: 0x{:X}".format(rva))
        results.append(" * Address: 0x{:X}".format(abs_addr))
        results.append(" " + "*" * 67 + "/")
        results.append("")

        if code:
            results.append(code)
            print("  SUCCESS")
            success_count += 1
        else:
            results.append("/* DECOMPILATION FAILED: " + str(error) + " */")
            print("  FAILED: " + str(error))
            fail_count += 1

    # Write output
    print("")
    print("=" * 70)
    try:
        with codecs.open(OUTPUT_PATH, 'w', 'utf-8') as f:
            f.write('\n'.join(results))
        print("Decompilation complete!")
        print("  Success: " + str(success_count))
        print("  Failed:  " + str(fail_count))
        print("  Output:  " + OUTPUT_PATH)
    except Exception as e:
        print("ERROR writing output file: " + str(e))
        try:
            with open(OUTPUT_PATH, 'w') as f:
                f.write('\n'.join(results))
            print("Output written (fallback mode): " + OUTPUT_PATH)
        except Exception as e2:
            print("FALLBACK ALSO FAILED: " + str(e2))

    print("=" * 70)
    decompiler.dispose()

# Run the script
run()
