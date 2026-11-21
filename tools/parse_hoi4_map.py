#!/usr/bin/env python3
"""
HoI4 Map Data Parser
Converts Clausewitz Engine data files (.txt, .csv) to JSON for Rogue World runtime systems.
"""

import json
import csv
import re
import os
from pathlib import Path
from typing import Dict, List, Any, Optional

HOI4_ROOT = Path(r"C:\Proje\Hearts of Iron IV No Compromise No Surrender")
OUTPUT_DIR = Path(r"C:\Proje\Rogue-World\Data\hoi4_imported")

def ensure_dir(path: Path):
    path.mkdir(parents=True, exist_ok=True)

def parse_clausewitz_value(val: str) -> Any:
    """Parse a Clausewitz script value to Python type."""
    val = val.strip()
    if val.lower() == 'yes':
        return True
    if val.lower() == 'no':
        return False
    try:
        if '.' in val:
            return float(val)
        return int(val)
    except ValueError:
        return val

def parse_clausewitz_block(content: str, start: int = 0) -> tuple:
    """Parse a Clausewitz { key = value } block recursively.
    Handles both key-value blocks and array blocks (e.g., provinces={ 1 2 3 })."""
    i = start
    n = len(content)

    # Skip to first {
    while i < n and content[i] != '{':
        i += 1
    if i >= n:
        return {}, i
    i += 1  # Skip {

    # Check if this block is an array (no = signs before next { or })
    # Look ahead to see if there's an = before the next { or }
    temp_i = i
    has_equals = False
    while temp_i < n and content[temp_i] != '}':
        if content[temp_i] == '=':
            has_equals = True
            break
        if content[temp_i] == '{':
            break
        temp_i += 1

    is_array_block = not has_equals

    if is_array_block:
        # Parse as array of values
        arr = []
        while i < n:
            # Skip whitespace
            while i < n and content[i].isspace():
                i += 1
            if i >= n or content[i] == '}':
                break
            if content[i] == '#':
                while i < n and content[i] != '\n':
                    i += 1
                continue

            v_start = i
            while i < n and content[i] not in ' \t\r\n#}':
                i += 1
            v = content[v_start:i].strip()
            if v:
                arr.append(parse_clausewitz_value(v))

        if i < n and content[i] == '}':
            i += 1
        return arr, i

    # Regular key-value block
    result = {}
    while i < n:
        # Skip whitespace
        while i < n and content[i].isspace():
            i += 1
        if i >= n:
            break
        if content[i] == '}':
            return result, i + 1
        if content[i] == '#':  # Comment
            while i < n and content[i] != '\n':
                i += 1
            continue

        # Parse key
        key_start = i
        while i < n and content[i] not in ' =\t\r\n{':
            i += 1
        key = content[key_start:i].strip()

        # Skip whitespace
        while i < n and content[i].isspace():
            i += 1

        # Expect =
        if i < n and content[i] == '=':
            i += 1
        else:
            # No =, might be a nested block without = (rare)
            pass

        # Skip whitespace
        while i < n and content[i].isspace():
            i += 1

        if i < n and content[i] == '{':
            # Nested block
            val, i = parse_clausewitz_block(content, i)
        else:
            # Simple value OR array of values (e.g., provinces={ 1 2 3 })
            vals = []
            while i < n and content[i] not in '\r\n#}':
                # Skip whitespace
                while i < n and content[i].isspace():
                    i += 1
                if i >= n or content[i] in '\r\n#}':
                    break
                # Parse one value
                v_start = i
                while i < n and content[i] not in ' \t\r\n#}':
                    i += 1
                v = content[v_start:i].strip()
                if v:
                    vals.append(parse_clausewitz_value(v))

            if len(vals) == 1:
                val = vals[0]
            else:
                val = vals

        if key:
            if key in result:
                if not isinstance(result[key], list):
                    result[key] = [result[key]]
                result[key].append(val)
            else:
                result[key] = val

    return result, i

def parse_strategic_region_file(filepath: Path) -> Dict:
    """Parse a single strategic region .txt file."""
    content = filepath.read_text(encoding='utf-8-sig')
    parsed, _ = parse_clausewitz_block(content)
    return parsed.get('strategic_region', parsed)

def parse_all_strategic_regions() -> List[Dict]:
    """Parse all strategic region files and merge by ID."""
    regions_dir = HOI4_ROOT / "map" / "strategicregions"
    regions_by_id = {}

    for filepath in sorted(regions_dir.glob("*.txt")):
        try:
            region = parse_strategic_region_file(filepath)

            # Fix "between" field: convert array [start, end] to object {start, end}
            if 'weather' in region and 'period' in region['weather']:
                for period in region['weather'].get('period', []):
                    if 'between' in period and isinstance(period['between'], list) and len(period['between']) == 2:
                        period['between'] = {'start': period['between'][0], 'end': period['between'][1]}

            region_id = region.get('id')
            if region_id is None:
                print(f"Warning: {filepath.name} has no ID, skipping")
                continue

            if region_id in regions_by_id:
                # Merge provinces and weather periods
                existing = regions_by_id[region_id]
                # Merge provinces (avoid duplicates)
                existing_provinces = set(existing.get('provinces', []))
                new_provinces = set(region.get('provinces', []))
                existing['provinces'] = sorted(existing_provinces | new_provinces)
                # Merge weather periods
                existing_periods = existing.get('weather', {}).get('period', [])
                new_periods = region.get('weather', {}).get('period', [])
                existing['weather']['period'] = existing_periods + new_periods
                print(f"Merged: {filepath.name} into region {region_id} (total provinces: {len(existing['provinces'])})")
            else:
                regions_by_id[region_id] = region
                print(f"Parsed: {filepath.name} - id={region_id}, provinces={len(region.get('provinces', []))}")
        except Exception as e:
            print(f"Error parsing {filepath.name}: {e}")

    # Convert to list sorted by ID
    regions = [regions_by_id[k] for k in sorted(regions_by_id.keys())]
    return regions

def parse_adjacencies_csv(filepath: Path) -> List[Dict]:
    """Parse adjacencies.csv."""
    adjacencies = []
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f, delimiter=';')
        for row in reader:
            # Clean up empty values
            clean_row = {k: v for k, v in row.items() if v}
            # Convert numeric fields
            for key in ['From', 'To', 'Through', 'start_x', 'start_y', 'stop_x', 'stop_y']:
                if key in clean_row and clean_row[key]:
                    try:
                        clean_row[key] = int(clean_row[key])
                    except ValueError:
                        pass
            adjacencies.append(clean_row)
    return adjacencies

def parse_adjacency_rules(filepath: Path) -> List[Dict]:
    """Parse adjacency_rules.txt - more complex nested structure."""
    content = filepath.read_text(encoding='utf-8-sig')
    # Find all adjacency_rule blocks
    rules = []

    # Simple approach: split by 'adjacency_rule = {' and parse each
    # This is a simplified parser for the known structure
    blocks = re.split(r'adjacency_rule\s*=', content)

    for block in blocks[1:]:  # Skip first split (header)
        block = block.strip()
        if not block.startswith('{'):
            continue
        try:
            rule, _ = parse_clausewitz_block(block)
            if rule:
                rules.append(rule)
        except Exception as e:
            print(f"Error parsing rule block: {e}")

    return rules

def parse_railways_txt(filepath: Path) -> List[Dict]:
    """Parse railways.txt format: owner_level num_segments province1 province2 ..."""
    railways = []
    content = filepath.read_text(encoding='utf-8-sig')
    for line in content.strip().split('\n'):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        parts = line.split()
        if len(parts) < 3:
            continue
        try:
            owner_level = int(parts[0])
            num_segments = int(parts[1])
            provinces = [int(p) for p in parts[2:]]
            railways.append({
                'owner_level': owner_level,
                'num_segments': num_segments,
                'provinces': provinces
            })
        except ValueError as e:
            print(f"Error parsing railway line: {line} - {e}")
    return railways

def parse_supply_nodes_txt(filepath: Path) -> List[Dict]:
    """Parse supply_nodes.txt format: level province_id"""
    nodes = []
    content = filepath.read_text(encoding='utf-8-sig')
    for line in content.strip().split('\n'):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        parts = line.split()
        if len(parts) >= 2:
            try:
                level = int(parts[0])
                province_id = int(parts[1])
                nodes.append({'level': level, 'province_id': province_id})
            except ValueError as e:
                print(f"Error parsing supply node: {line} - {e}")
    return nodes

def parse_buildings_txt(filepath: Path) -> List[Dict]:
    """Parse buildings.txt format: province_id;building_type;x;y;z;rotation;level"""
    buildings = []
    content = filepath.read_text(encoding='utf-8-sig')
    for line in content.strip().split('\n'):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        parts = line.split(';')
        if len(parts) >= 7:
            try:
                buildings.append({
                    'province_id': int(parts[0]),
                    'building_type': parts[1],
                    'x': float(parts[2]),
                    'y': float(parts[3]),
                    'z': float(parts[4]),
                    'rotation': float(parts[5]),
                    'level': int(parts[6])
                })
            except ValueError as e:
                print(f"Error parsing building: {line} - {e}")
    return buildings

def main():
    ensure_dir(OUTPUT_DIR)

    print("=" * 60)
    print("Parsing HoI4 Strategic Regions...")
    print("=" * 60)
    regions = parse_all_strategic_regions()
    output_file = OUTPUT_DIR / "strategic_regions.json"
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(regions, f, indent=2, ensure_ascii=False)
    print(f"Saved {len(regions)} regions to {output_file}")

    print("\n" + "=" * 60)
    print("Parsing Adjacencies...")
    print("=" * 60)
    adjacencies = parse_adjacencies_csv(HOI4_ROOT / "map" / "adjacencies.csv")
    output_file = OUTPUT_DIR / "province_adjacencies.json"
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(adjacencies, f, indent=2, ensure_ascii=False)
    print(f"Saved {len(adjacencies)} adjacencies to {output_file}")

    print("\n" + "=" * 60)
    print("Parsing Adjacency Rules (Straits/Canals)...")
    print("=" * 60)
    rules = parse_adjacency_rules(HOI4_ROOT / "map" / "adjacency_rules.txt")
    output_file = OUTPUT_DIR / "strait_canal_rules.json"
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(rules, f, indent=2, ensure_ascii=False)
    print(f"Saved {len(rules)} rules to {output_file}")

    print("\n" + "=" * 60)
    print("Parsing Railways...")
    print("=" * 60)
    railways = parse_railways_txt(HOI4_ROOT / "map" / "railways.txt")
    output_file = OUTPUT_DIR / "railways.json"
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(railways, f, indent=2, ensure_ascii=False)
    print(f"Saved {len(railways)} railways to {output_file}")

    print("\n" + "=" * 60)
    print("Parsing Supply Nodes...")
    print("=" * 60)
    supply_nodes = parse_supply_nodes_txt(HOI4_ROOT / "map" / "supply_nodes.txt")
    output_file = OUTPUT_DIR / "supply_nodes.json"
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(supply_nodes, f, indent=2, ensure_ascii=False)
    print(f"Saved {len(supply_nodes)} supply nodes to {output_file}")

    print("\n" + "=" * 60)
    print("Parsing Buildings...")
    print("=" * 60)
    buildings = parse_buildings_txt(HOI4_ROOT / "map" / "buildings.txt")
    output_file = OUTPUT_DIR / "buildings.json"
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(buildings, f, indent=2, ensure_ascii=False)
    print(f"Saved {len(buildings)} buildings to {output_file}")

    print("\n" + "=" * 60)
    print("ALL PARSING COMPLETE!")
    print("=" * 60)

if __name__ == "__main__":
    main()