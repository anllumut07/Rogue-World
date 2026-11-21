#!/usr/bin/env python3
"""
HoI4 Map Mechanics Deep Analysis
Parses all map data files and extracts runtime mechanics for Rogue World implementation.
"""

import struct
import csv
import json
import os
import re
from collections import defaultdict, Counter
from dataclasses import dataclass, field
from typing import Dict, List, Tuple, Optional, Set
import math

BASE = r"C:\Proje\Hearts of Iron IV No Compromise No Surrender"
MAP_DIR = os.path.join(BASE, "map")
COMMON_DIR = os.path.join(BASE, "common")

# ============================================================
# HELPER
# ============================================================

def clean_val(val: str) -> str:
    """Strip inline comments and whitespace."""
    if '#' in val:
        val = val.split('#')[0]
    return val.strip().strip('{}')

def clean_tokens(tokens: List[str]) -> List[str]:
    """Filter out commented tokens."""
    return [t for t in tokens if not t.startswith('#')]

# ============================================================
# 1. BMP PARSER
# ============================================================

def read_bmp_header(filepath: str) -> dict:
    with open(filepath, 'rb') as f:
        signature = f.read(2)
        if signature != b'BM':
            raise ValueError("Not a BMP file")
        file_size = struct.unpack('<I', f.read(4))[0]
        reserved = f.read(4)
        data_offset = struct.unpack('<I', f.read(4))[0]
        header_size = struct.unpack('<I', f.read(4))[0]
        width = struct.unpack('<i', f.read(4))[0]
        height = struct.unpack('<i', f.read(4))[0]
        planes = struct.unpack('<H', f.read(2))[0]
        bits_per_pixel = struct.unpack('<H', f.read(2))[0]
        compression = struct.unpack('<I', f.read(4))[0]
        image_size = struct.unpack('<I', f.read(4))[0]
        x_pixels_per_meter = struct.unpack('<i', f.read(4))[0]
        y_pixels_per_meter = struct.unpack('<i', f.read(4))[0]
        colors_used = struct.unpack('<I', f.read(4))[0]
        colors_important = struct.unpack('<I', f.read(4))[0]
        return {
            'file_size': file_size,
            'data_offset': data_offset,
            'width': width,
            'height': abs(height),
            'bits_per_pixel': bits_per_pixel,
            'compression': compression,
            'image_size': image_size,
            'top_down': height < 0
        }

def read_bmp_pixels(filepath: str, max_pixels: int = None) -> Tuple[List[Tuple[int,int,int]], dict]:
    header = read_bmp_header(filepath)
    width = header['width']
    height = header['height']
    bits = header['bits_per_pixel']
    offset = header['data_offset']
    top_down = header['top_down']

    if bits == 24:
        row_size = ((width * 3 + 3) // 4) * 4
        pixels = []
        with open(filepath, 'rb') as f:
            f.seek(offset)
            for y in range(height):
                row = f.read(row_size)
                for x in range(width):
                    b, g, r = row[x*3], row[x*3+1], row[x*3+2]
                    pixels.append((r, g, b))
                if max_pixels and len(pixels) >= max_pixels:
                    break
    elif bits == 8:
        row_size = ((width + 3) // 4) * 4
        pixels = []
        with open(filepath, 'rb') as f:
            f.seek(offset)
            for y in range(height):
                row = f.read(row_size)
                for x in range(width):
                    idx = row[x]
                    pixels.append((idx, idx, idx))
                if max_pixels and len(pixels) >= max_pixels:
                    break
    else:
        raise ValueError(f"Unsupported BMP bit depth: {bits}-bit")

    if not top_down:
        rows = [pixels[i*width:(i+1)*width] for i in range(height)]
        rows.reverse()
        pixels = [p for row in rows for p in row]

    return pixels, header

# ============================================================
# 2. DEFINITION.CSV PARSER
# ============================================================

@dataclass
class ProvinceDef:
    id: int
    r: int; g: int; b: int
    type: str
    is_water: bool
    terrain_category: str
    unknown: int

def parse_definition_csv(filepath: str):
    provinces = {}
    rgb_to_id = {}
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        reader = csv.reader(f, delimiter=';')
        for row in reader:
            if not row or row[0].startswith('#'):
                continue
            if len(row) < 7:
                continue
            pid = int(row[0])
            r, g, b = int(row[1]), int(row[2]), int(row[3])
            ptype = row[4]
            is_water = row[5].lower() == 'true'
            terrain_cat = row[6]
            unknown = int(row[7]) if len(row) > 7 else 0
            prov = ProvinceDef(pid, r, g, b, ptype, is_water, terrain_cat, unknown)
            provinces[pid] = prov
            rgb_to_id[(r, g, b)] = pid
    return provinces, rgb_to_id

# ============================================================
# 3. TERRAIN DEFINITIONS PARSER
# ============================================================

@dataclass
class TerrainType:
    name: str
    color: Tuple[int,int,int] = None
    movement_cost: float = 1.0
    combat_width: int = 144
    combat_support_width: int = 72
    attack_mod: float = 0.0
    defence_mod: float = 0.0
    movement_mod: float = 0.0
    supply_penalty: float = 0.0
    truck_attrition: float = 0.0
    attrition: float = 0.0
    sickness_chance: float = 0.0
    ai_importance: float = 1.0
    match_value: float = 0.0
    sound_type: str = "plains"
    buildings_max: Dict[str, int] = field(default_factory=dict)
    naval_terrain: bool = False
    is_water: bool = False
    min_seazone_dominance: float = 0.0
    ship_modifiers: Dict = field(default_factory=dict)
    texture_index: int = 0
    perm_snow: bool = False
    spawn_city: bool = False

LAND_TERRAINS = {'unknown', 'lakes', 'forest', 'hills', 'mountain', 'plains',
                 'urban', 'supercity', 'jungle', 'marsh', 'desert'}
NAVAL_TERRAINS = {'ocean', 'water_fjords', 'water_littoral', 'water_shallow_sea', 'water_deep_ocean'}
ALL_TERRAINS = LAND_TERRAINS | NAVAL_TERRAINS

def parse_terrain_txt(filepath: str):
    categories = {}
    color_to_category = {}

    with open(filepath, 'r', encoding='utf-8-sig') as f:
        content = f.read()

    # Parse categories block
    cat_match = re.search(r'categories\s*=\s*\{([\s\S]*?)^\}', content, re.MULTILINE)
    if cat_match:
        cat_content = cat_match.group(1)
        terrain_pattern = r'(' + '|'.join(ALL_TERRAINS) + r')\s*=\s*\{([\s\S]*?)^\s*\}'
        for m in re.finditer(terrain_pattern, cat_content, re.MULTILINE):
            cat_name = m.group(1)
            cat_body = m.group(2)
            terrain = TerrainType(name=cat_name)

            cat_body_clean = '\n'.join(line for line in cat_body.split('\n') if not line.strip().startswith('#'))

            for km in re.finditer(r'(\w+)\s*=\s*([^\n{}]+)', cat_body_clean):
                key, val = km.group(1), clean_val(km.group(2))
                if not val:
                    continue
                if key == 'color':
                    terrain.color = tuple(map(int, val.split()))
                elif key == 'movement_cost':
                    terrain.movement_cost = float(val)
                elif key == 'combat_width':
                    terrain.combat_width = int(val)
                elif key == 'combat_support_width':
                    terrain.combat_support_width = int(val)
                elif key == 'attack':
                    terrain.attack_mod = float(val)
                elif key == 'defence':
                    terrain.defence_mod = float(val)
                elif key == 'movement':
                    terrain.movement_mod = float(val)
                elif key == 'supply_flow_penalty_factor':
                    terrain.supply_penalty = float(val)
                elif key == 'truck_attrition_factor':
                    terrain.truck_attrition = float(val)
                elif key == 'attrition':
                    terrain.attrition = float(val)
                elif key == 'sickness_chance':
                    terrain.sickness_chance = float(val)
                elif key == 'ai_terrain_importance_factor':
                    terrain.ai_importance = float(val)
                elif key == 'match_value':
                    terrain.match_value = float(val)
                elif key == 'sound_type':
                    terrain.sound_type = val
                elif key == 'naval_terrain':
                    terrain.naval_terrain = val.lower() == 'yes'
                elif key == 'is_water':
                    terrain.is_water = val.lower() == 'yes'
                elif key == 'minimum_seazone_dominance':
                    terrain.min_seazone_dominance = float(val)

            bld_match = re.search(r'buildings_max_level\s*=\s*\{([^}]+)\}', cat_body_clean)
            if bld_match:
                for bm in re.finditer(r'(\w+)\s*=\s*(\d+)', bld_match.group(1)):
                    terrain.buildings_max[bm.group(1)] = int(bm.group(2))

            units_match = re.search(r'units\s*=\s*\{([^}]+)\}', cat_body_clean)
            if units_match:
                for um in re.finditer(r'(\w+)\s*=\s*([-\d.]+)', units_match.group(1)):
                    if um.group(1) == 'attack':
                        terrain.attack_mod = float(um.group(2))
                    elif um.group(1) == 'defence':
                        terrain.defence_mod = float(um.group(2))
                    elif um.group(1) == 'movement':
                        terrain.movement_mod = float(um.group(2))

            categories[cat_name] = terrain

    # Parse terrain graphical mappings - handle multi-line
    terrain_match = re.search(r'terrain\s*=\s*\{([\s\S]*?)\n\}', content)
    if terrain_match:
        terrain_content = terrain_match.group(1)
        for m in re.finditer(r'(\w+)\s*=\s*\{([\s\S]*?)\n\s*\}', terrain_content):
            terrain_name = m.group(1)
            body = m.group(2)
            ttype = None
            color_idx = None
            texture = 0
            perm_snow = False
            spawn_city = False

            for km in re.finditer(r'(\w+)\s*=\s*([^\n{}]+)', body):
                key, val = km.group(1), clean_val(km.group(2))
                if key == 'type':
                    ttype = val
                elif key == 'color':
                    nums = list(map(int, val.strip('{}').split()))
                    color_idx = nums[0] if nums else 0
                elif key == 'texture':
                    texture = int(val)
                elif key == 'perm_snow':
                    perm_snow = val.lower() == 'yes'
                elif key == 'spawn_city':
                    spawn_city = val.lower() == 'yes'

            if ttype and color_idx is not None:
                color_to_category[color_idx] = ttype
                if ttype in categories:
                    categories[ttype].texture_index = texture
                    categories[ttype].perm_snow = perm_snow
                    categories[ttype].spawn_city = spawn_city

    return categories, color_to_category

# ============================================================
# 4. STRATEGIC REGIONS PARSER
# ============================================================

@dataclass
class WeatherPeriod:
    start_day: float
    end_day: float
    temp_min: float
    temp_max: float
    probabilities: Dict[str, float]

@dataclass
class StrategicRegion:
    id: int
    name: str
    provinces: List[int]
    weather_periods: List[WeatherPeriod] = field(default_factory=list)

def parse_strategic_region(filepath: str):
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        content = f.read()

    id_match = re.search(r'id\s*=\s*(\d+)', content)
    if not id_match:
        return None
    rid = int(id_match.group(1))

    name_match = re.search(r'name\s*=\s*"([^"]+)"', content)
    name = name_match.group(1) if name_match else f"REGION_{rid}"

    prov_match = re.search(r'provinces\s*=\s*\{([^}]+)\}', content)
    provinces = []
    if prov_match:
        prov_str = prov_match.group(1)
        prov_nums = []
        for token in prov_str.split():
            if not token.startswith('#'):
                try:
                    prov_nums.append(int(token))
                except ValueError:
                    pass
        provinces = prov_nums

    periods = []
    weather_match = re.search(r'weather\s*=\s*\{([\s\S]*?)^\s*\}', content, re.MULTILINE)
    if weather_match:
        weather_content = weather_match.group(1)
        for pm in re.finditer(r'period\s*=\s*\{([\s\S]*?)^\s*\}', weather_content, re.MULTILINE):
            pcontent = pm.group(1)
            between_match = re.search(r'between\s*=\s*\{([^}]+)\}', pcontent)
            temp_match = re.search(r'temperature\s*=\s*\{([^}]+)\}', pcontent)

            if between_match and temp_match:
                start, end = map(float, between_match.group(1).split())
                tmin, tmax = map(float, temp_match.group(1).split())

                probs = {}
                for phen in ['no_phenomenon', 'rain_light', 'rain_heavy', 'snow', 'blizzard',
                            'arctic_water', 'mud', 'sandstorm', 'min_snow_level']:
                    phen_match = re.search(rf'{phen}\s*=\s*([\d.]+)', pcontent)
                    if phen_match:
                        probs[phen] = float(phen_match.group(1))

                periods.append(WeatherPeriod(start, end, tmin, tmax, probs))

    return StrategicRegion(rid, name, provinces, periods)

# ============================================================
# 5. ADJACENCIES PARSER
# ============================================================

@dataclass
class Adjacency:
    from_prov: int
    to_prov: int
    type: str
    through: int
    rule_name: str = ""

def parse_adjacencies(filepath: str):
    adjacencies = []
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f, delimiter=';')
        for row in reader:
            adj = Adjacency(
                from_prov=int(row['From']),
                to_prov=int(row['To']),
                type=row['Type'],
                through=int(row['Through']) if row['Through'] else -1,
                rule_name=row['adjacency_rule_name'] or ""
            )
            adjacencies.append(adj)
    return adjacencies

# ============================================================
# 6. ADJACENCY RULES PARSER
# ============================================================

@dataclass
class AdjacencyRule:
    name: str
    contested: Dict[str, bool]
    enemy: Dict[str, bool]
    friend: Dict[str, bool]
    neutral: Dict[str, bool]
    required_provinces: List[int]
    disabled_conditions: List[str] = field(default_factory=list)

def parse_adjacency_rules(filepath: str):
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        content = f.read()

    rules = {}
    rule_blocks = re.findall(r'adjacency_rule\s*=\s*\{([\s\S]*?)\n\}', content)
    for body in rule_blocks:
        rule = AdjacencyRule(name="", contested={}, enemy={}, friend={}, neutral={}, required_provinces=[])

        name_match = re.search(r'name\s*=\s*"([^"]+)"', body)
        if name_match:
            rule.name = name_match.group(1)

        for perm_type in ['contested', 'enemy', 'friend', 'neutral']:
            pattern = perm_type + r'\s*=\s*\{([\s\S]*?)\n\s*\}'
            perm_match = re.search(pattern, body)
            if perm_match:
                perm_body = perm_match.group(1)
                for km in re.finditer(r'(\w+)\s*=\s*(yes|no)', perm_body):
                    getattr(rule, perm_type)[km.group(1)] = km.group(2) == 'yes'

        req_match = re.search(r'required_provinces\s*=\s*\{([\s\S]*?)\n\s*\}', body)
        if req_match:
            prov_str = req_match.group(1)
            prov_nums = []
            for token in prov_str.split():
                if not token.startswith('#'):
                    try:
                        prov_nums.append(int(token))
                    except ValueError:
                        pass
            rule.required_provinces = prov_nums

        dis_match = re.search(r'is_disabled\s*=\s*\{([\s\S]*?)\n\s*\}', body)
        if dis_match:
            rule.disabled_conditions.append(dis_match.group(1)[:200])

        if rule.name:
            rules[rule.name] = rule

    return rules

# ============================================================
# 7. SUPPLY NODES PARSER
# ============================================================

def parse_supply_nodes(filepath: str):
    nodes = defaultdict(list)
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        for line in f:
            parts = line.strip().split()
            if len(parts) >= 2:
                level = int(parts[0])
                prov = int(parts[1])
                nodes[level].append(prov)
    return nodes

# ============================================================
# 8. RAILWAYS PARSER
# ============================================================

@dataclass
class RailwayLine:
    owner_level: int
    provinces: List[int]

def parse_railways(filepath: str):
    lines = []
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        for line in f:
            parts = line.strip().split()
            if len(parts) >= 3:
                owner = int(parts[0])
                num_segments = int(parts[1])
                provinces = list(map(int, parts[2:2+num_segments]))
                lines.append(RailwayLine(owner, provinces))
    return lines

# ============================================================
# 9. BUILDINGS PARSER
# ============================================================

@dataclass
class Building:
    province: int
    type: str
    x: float; y: float; z: float
    rotation: float
    level: int

def parse_buildings(filepath: str):
    buildings = []
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        for line in f:
            parts = line.strip().split(';')
            if len(parts) >= 7:
                b = Building(
                    province=int(parts[0]),
                    type=parts[1],
                    x=float(parts[2]),
                    y=float(parts[3]),
                    z=float(parts[4]),
                    rotation=float(parts[5]),
                    level=int(parts[6])
                )
                buildings.append(b)
    return buildings

# ============================================================
# 10. WEATHER.TXT PARSER - FIXED
# ============================================================

@dataclass
class WeatherSettings:
    temperature: Dict = field(default_factory=dict)
    mud: Dict = field(default_factory=dict)
    snowing: Dict = field(default_factory=dict)
    arctic_water: Dict = field(default_factory=dict)
    chance_increase: Dict = field(default_factory=dict)
    duration: Dict = field(default_factory=dict)
    performance: Dict = field(default_factory=dict)

@dataclass
class WeatherEffects:
    rain_light: List = field(default_factory=list)
    rain_heavy: List = field(default_factory=list)
    snow: List = field(default_factory=list)
    blizzard: List = field(default_factory=list)
    sandstorm: List = field(default_factory=list)

def parse_weather_txt(filepath: str):
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        content = f.read()

    terrain_modifiers = {}
    settings = WeatherSettings()
    effects = WeatherEffects()

    # Parse terrain_modifiers
    tm_match = re.search(r'terrain_modifiers\s*=\s*\{([\s\S]*?)\n\}', content)
    if tm_match:
        tm_content = tm_match.group(1)
        for phen_match in re.finditer(r'(\w+)\s*=\s*\{([\s\S]*?)\n\s*\}', tm_content):
            phen = phen_match.group(1)
            phen_body = phen_match.group(2)
            terrain_modifiers[phen] = {}
            for t_match in re.finditer(r'terrain\s*=\s*\{([^}]+)\}', phen_body):
                tbody = t_match.group(1)
                ttype = None
                value = 0
                for kv in re.finditer(r'(\w+)\s*=\s*([-\d.]+)', tbody):
                    if kv.group(1) == 'type':
                        ttype = kv.group(2).strip('"')
                    elif kv.group(1) == 'value':
                        value = float(kv.group(2))
                if ttype:
                    terrain_modifiers[phen][ttype] = value

    # Parse settings
    s_match = re.search(r'settings\s*=\s*\{([\s\S]*?)\n\}', content)
    if s_match:
        s_content = s_match.group(1)
        for sub in ['temperature', 'mud', 'snowing', 'arctic_water', 'chance_increase', 'duration', 'performance']:
            pattern = sub + r'\s*=\s*\{([\s\S]*?)\n\s*\}'
            sub_match = re.search(pattern, s_content)
            if sub_match:
                sub_body = sub_match.group(1)
                parsed = {}
                for kv in re.finditer(r'(\w+)\s*=\s*([^\n{}]+)', sub_body):
                    key, val = kv.group(1), clean_val(kv.group(2))
                    if val:
                        try:
                            parsed[key] = float(val)
                        except ValueError:
                            parsed[key] = val
                setattr(settings, sub, parsed)

    # Parse weather_effects
    we_match = re.search(r'weather_effects\s*=\s*\{([\s\S]*?)\n\}', content)
    if we_match:
        we_content = we_match.group(1)
        for eff in ['rain_light', 'rain_heavy', 'snow', 'blizzard', 'sandstorm']:
            pattern = eff + r'\s*=\s*\{([\s\S]*?)\n\s*\}'
            eff_match = re.search(pattern, we_content)
            if eff_match:
                setattr(effects, eff, eff_match.group(1))

    return terrain_modifiers, settings, effects

# ============================================================
# MAIN ANALYSIS
# ============================================================

def main():
    print("=" * 80)
    print("HoI4 MAP MECHANICS DEEP ANALYSIS")
    print("=" * 80)

    # 1. Parse Definition.csv
    print("\n[1/10] Parsing definition.csv...")
    provinces, rgb_to_id = parse_definition_csv(os.path.join(MAP_DIR, "definition.csv"))
    print(f"    Total provinces: {len(provinces)}")
    land = sum(1 for p in provinces.values() if p.type == 'land')
    sea = sum(1 for p in provinces.values() if p.type == 'sea')
    lake = sum(1 for p in provinces.values() if p.type == 'lake')
    print(f"    Land: {land}, Sea: {sea}, Lake: {lake}")

    terrain_cats = Counter(p.terrain_category for p in provinces.values())
    print(f"    Terrain categories: {dict(terrain_cats.most_common())}")

    # 2. Parse Terrain Definitions
    print("\n[2/10] Parsing terrain definitions...")
    terrain_types, color_to_category = parse_terrain_txt(os.path.join(COMMON_DIR, "terrain", "00_terrain.txt"))
    print(f"    Terrain categories defined: {len(terrain_types)}")
    for name in sorted(terrain_types.keys()):
        t = terrain_types[name]
        print(f"      {name}: move_cost={t.movement_cost}, combat_w={t.combat_width}, "
              f"atk={t.attack_mod}, def={t.defence_mod}, supply_pen={t.supply_penalty}, "
              f"truck_attr={t.truck_attrition}, attrition={t.attrition}")
    print(f"    Color index mappings: {len(color_to_category)}")
    for idx in sorted(color_to_category.keys())[:20]:
        print(f"      Index {idx} -> {color_to_category[idx]}")

    # 3. Parse Strategic Regions
    print("\n[3/10] Parsing strategic regions...")
    regions = {}
    strat_dir = os.path.join(MAP_DIR, "strategicregions")
    for fname in os.listdir(strat_dir):
        if fname.endswith('.txt'):
            r = parse_strategic_region(os.path.join(strat_dir, fname))
            if r:
                regions[r.id] = r
    print(f"    Total strategic regions: {len(regions)}")
    total_provs_in_regions = sum(len(r.provinces) for r in regions.values())
    print(f"    Total province references: {total_provs_in_regions}")
    periods_per_region = [len(r.weather_periods) for r in regions.values() if r.weather_periods]
    if periods_per_region:
        print(f"    Weather periods per region: min={min(periods_per_region)}, max={max(periods_per_region)}, avg={sum(periods_per_region)/len(periods_per_region):.1f}")

    r1 = regions.get(1)
    if r1:
        print(f"    Region 1 ({r1.name}) provinces: {len(r1.provinces)}")
        for p in r1.weather_periods[:3]:
            print(f"      Days {p.start_day:.1f}-{p.end_day:.1f}: T={p.temp_min:.1f}-{p.temp_max:.1f}, "
                  f"phenomena={list(p.probabilities.keys())}")

    # 4. Parse Adjacencies
    print("\n[4/10] Parsing adjacencies...")
    adjacencies = parse_adjacencies(os.path.join(MAP_DIR, "adjacencies.csv"))
    print(f"    Total adjacencies: {len(adjacencies)}")
    by_type = Counter(a.type for a in adjacencies)
    print(f"    By type: {dict(by_type)}")

    adj_graph = defaultdict(list)
    for a in adjacencies:
        adj_graph[a.from_prov].append((a.to_prov, a.type, a.rule_name))
    print(f"    Provinces with connections: {len(adj_graph)}")

    # 5. Parse Adjacency Rules
    print("\n[5/10] Parsing adjacency rules...")
    adj_rules = parse_adjacency_rules(os.path.join(MAP_DIR, "adjacency_rules.txt"))
    print(f"    Total rules: {len(adj_rules)}")
    for name, rule in adj_rules.items():
        print(f"      {name}: req_provs={len(rule.required_provinces)}, "
              f"enemy_navy={rule.enemy.get('navy')}, friend_navy={rule.friend.get('navy')}")

    # 6. Parse Supply Nodes
    print("\n[6/10] Parsing supply nodes...")
    supply_nodes = parse_supply_nodes(os.path.join(MAP_DIR, "supply_nodes.txt"))
    print(f"    Supply nodes by level: {{level: count}} = {{ {', '.join(f'{k}:{len(v)}' for k,v in sorted(supply_nodes.items()))} }}")

    # 7. Parse Railways
    print("\n[7/10] Parsing railways...")
    railways = parse_railways(os.path.join(MAP_DIR, "railways.txt"))
    print(f"    Total railway lines: {len(railways)}")
    gauge_dist = Counter(r.owner_level for r in railways)
    print(f"    Gauge distribution: {{1=standard,2=broad,3=narrow}} = {dict(gauge_dist)}")
    total_rail_provs = sum(len(r.provinces) for r in railways)
    print(f"    Total province connections: {total_rail_provs}")

    # 8. Parse Buildings
    print("\n[8/10] Parsing buildings...")
    buildings = parse_buildings(os.path.join(MAP_DIR, "buildings.txt"))
    print(f"    Total buildings: {len(buildings)}")
    btype_dist = Counter(b.type for b in buildings)
    print(f"    Building types: {dict(btype_dist.most_common(15))}")
    prov_bcount = Counter(b.province for b in buildings)
    print(f"    Provinces with buildings: {len(prov_bcount)}, max per province: {max(prov_bcount.values()) if prov_bcount else 0}")

    # 9. Parse Weather System
    print("\n[9/10] Parsing weather system...")
    terrain_mods, weather_settings, weather_effects = parse_weather_txt(os.path.join(COMMON_DIR, "weather.txt"))
    print(f"    Terrain modifiers on weather: {list(terrain_mods.keys())}")
    for phen, mods in terrain_mods.items():
        print(f"      {phen}: {mods}")
    print(f"    Temperature thresholds: {weather_settings.temperature}")
    print(f"    Mud settings: {weather_settings.mud}")
    print(f"    Snow settings: {weather_settings.snowing}")
    print(f"    Duration ranges: {weather_settings.duration}")
    print(f"    Performance: {weather_settings.performance}")

    # 10. BMP Analysis
    print("\n[10/10] Analyzing BMP files (sampling)...")
    for bmp_name in ["provinces.bmp", "terrain.bmp", "heightmap.bmp", "rivers.bmp"]:
        path = os.path.join(MAP_DIR, bmp_name)
        if os.path.exists(path):
            header = read_bmp_header(path)
            print(f"    {bmp_name}: {header['width']}x{header['height']}, {header['bits_per_pixel']}-bit, "
                  f"top_down={header['top_down']}, size={header['file_size']/1024/1024:.1f}MB")

    print("\n    Sampling provinces.bmp (first 1000 pixels)...")
    pixels, _ = read_bmp_pixels(os.path.join(MAP_DIR, "provinces.bmp"), max_pixels=10000)
    matched = 0
    for i, (r,g,b) in enumerate(pixels[:1000]):
        if (r,g,b) in rgb_to_id:
            matched += 1
    print(f"    RGB->ID matches in sample: {matched}/1000")

    print("    Sampling terrain.bmp (first 1000 pixels)...")
    terrain_pixels, _ = read_bmp_pixels(os.path.join(MAP_DIR, "terrain.bmp"), max_pixels=10000)
    terrain_colors = Counter(terrain_pixels[:1000])
    print(f"    Unique color indices in sample: {len(terrain_colors)}")
    for (r,g,b), count in terrain_colors.most_common(10):
        idx = r
        terrain_type = color_to_category.get(idx, "unknown")
        print(f"      Index {idx} (RGB={r},{g},{b}): {terrain_type} ({count} pixels)")

    print("\n" + "=" * 80)
    print("ANALYSIS COMPLETE")
    print("=" * 80)

if __name__ == "__main__":
    main()