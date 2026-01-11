# Ghidra headless script to decompile FF2 pathfinding and map functions
# Compatible with Jython 2.7 (Ghidra's Python interpreter)
# Parses il2cpp.h for type information before decompiling

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
    # MapRouteSearcher - Core pathfinding (TypeDefIndex: 4912)
    # ============================================================
    0xA9AEE0: "MapRouteSearcher$$Search",
    0xA99040: "MapRouteSearcher$$SearchShortestRoute",
    0xA9A0C0: "MapRouteSearcher$$SearchSimple",
    0xA9AE50: "MapRouteSearcher$$SearchSpriteEntityToCellPosition",
    0xA9AD30: "MapRouteSearcher$$SearchSpriteEntityDirection",
    0xA98650: "MapRouteSearcher$$EntityWorldPositionToCellPosition",
    0xA987B0: "MapRouteSearcher$$MakeRouteMapWithCollision",
    0xA98930: "MapRouteSearcher$$MakeRouteMapWithoutCollision",
    0xA98A30: "MapRouteSearcher$$SearchAroundCellWithCollision",
    0xA98ED0: "MapRouteSearcher$$SearchAroundCellWithoutCollision",
    0xA9BDD0: "MapRouteSearcher$$UpdateRouteMapCellStep",
    0xA985E0: "MapRouteSearcher$$CanIgnore",

    # ============================================================
    # MapModel - Map data model (TypeDefIndex: 6446)
    # ============================================================
    0x304340: "MapModel$$GetMapName",
    0x305070: "MapModel$$get_IsAreaTypeWorld",
    0x305080: "MapModel$$get_IsFreeSaveArea",
    0x3042D0: "MapModel$$GetIsLoop",

    # ============================================================
    # Map - Master map data (TypeDefIndex: 6838)
    # ============================================================
    0xA22000: "Map$$get_MapName",
    0xA22040: "Map$$set_MapName",
    0xA24710: "Map$$get_MapTitle",
    0xA24750: "Map$$set_MapTitle",
    0xA23270: "Map$$get_AssetName",
    0xA17D10: "Map$$get_Id",

    # ============================================================
    # FieldController - Field map controller (TypeDefIndex: 6182)
    # ============================================================
    0x7A2FE0: "FieldController$$ConvertCellPositionToWorldPosition",
    0x7A3220: "FieldController$$ConvertWorldPositionToCellPosition",
    0x7A40B0: "FieldController$$GetFieldEntity",
    0x7A4240: "FieldController$$GetMapArea",
    0x7A47E0: "FieldController$$MapInitialize",
    0x7A4820: "FieldController$$MapSetup",
}

# Paths
OUTPUT_PATH = "D:\\Games\\Dev\\Unity\\FFPR\\ff2\\ff2-screen-reader\\docs\\scripts\\decompiled_pathfinding.c"
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
            # CParser.parse() takes the source code as a string
            parsed_dtm = parser.parse(header_content)

            if parsed_dtm is not None:
                print("Parsing completed, applying types to program...")

                # The parser adds types directly to the DTM passed in constructor
                # But we can also iterate and count
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

            # Check if it's a syntax error we can report
            if "line" in error_str.lower():
                print("This may be a syntax error in the header file.")
                print("Consider editing il2cpp_ghidra.h to fix or comment out the problematic section.")

            # Try alternative method using CParserUtils
            print("Attempting alternative parsing method...")
            try:
                from ghidra.app.util.cparser.C import CParserUtils
                from java.io import File

                # CParserUtils.parseHeaderFiles signature:
                # parseHeaderFiles(Program program, List<String> filenames, List<String> includePaths,
                #                  DataTypeManager dtm, MessageLog log, TaskMonitor monitor)
                from ghidra.app.util import MessageLog
                log = MessageLog()

                file_list = [IL2CPP_HEADER_PATH]
                include_paths = []  # Empty list for include paths

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
    print("FF2 Pathfinding & Map Name Decompiler")
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
    results.append(" * FF2 Decompiled Functions - Pathfinding & Map Names")
    results.append(" * Generated by Ghidra headless analysis")
    results.append(" * Program: " + program.getName())
    results.append(" * Image Base: 0x{:X}".format(image_base))
    results.append(" * IL2CPP types applied: " + str(types_parsed))
    results.append(" *")
    results.append(" * Purpose: Understanding pathfinding and map name resolution for screen reader accessibility")
    results.append(" *")
    results.append(" * Key classes:")
    results.append(" *   - MapRouteSearcher: A* pathfinding for field navigation")
    results.append(" *   - MapModel: Map data model with name/title resolution")
    results.append(" *   - Map: Master data for map properties")
    results.append(" *   - FieldController: Field map controller and coordinate conversion")
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
