#!/usr/bin/env python3
"""
Optimize map data for game runtime:
- Quantize coordinates to 3 decimal places (~110m at equator)
- Keep only necessary LOD levels
- Remove unnecessary fields
- Output compact JSON (separators=(',', ':'))
"""

import json
import os

IN_DIR = os.path.join(os.path.dirname(__file__), '..', '..', '..', 'Data', 'map')
OUT_DIR = IN_DIR  # overwrite in place

def quantize_coords(obj, precision=2):
    """Recursively quantize float coordinates in geometry."""
    if isinstance(obj, float):
        return round(obj, precision)
    elif isinstance(obj, list):
        return [quantize_coords(x, precision) for x in obj]
    elif isinstance(obj, dict):
        return {k: quantize_coords(v, precision) for k, v in obj.items()}
    return obj

def optimize_countries():
    print("Optimizing countries.json...")
    with open(os.path.join(IN_DIR, 'countries.json'), 'r') as f:
        data = json.load(f)

    for c in data:
        # Keep only essential fields
        keep = ['iso3', 'name', 'sovereign', 'area_km2', 'bbox', 'lod']
        c_new = {k: c[k] for k in keep if k in c}

        # Keep only lod0 (world view) and lod1 (regional view)
        # lod2 is too detailed for country level
        if 'lod' in c_new:
            c_new['lod'] = {
                'lod0': c_new['lod'].get('lod0_countries'),
                'lod1': c_new['lod'].get('lod1_regions'),
            }
            # Remove if None
            c_new['lod'] = {k: v for k, v in c_new['lod'].items() if v is not None}

        c.clear()
        c.update(c_new)

    # Quantize all coordinates
    data = quantize_coords(data, 3)

    with open(os.path.join(OUT_DIR, 'countries.json'), 'w') as f:
        json.dump(data, f, separators=(',', ':'))

    size = os.path.getsize(os.path.join(OUT_DIR, 'countries.json'))
    print(f"  countries.json: {size/1024/1024:.2f} MB")

def optimize_regions():
    print("Optimizing regions.json...")
    with open(os.path.join(IN_DIR, 'regions.json'), 'r') as f:
        data = json.load(f)

    for r in data:
        # Keep only essential fields
        keep = ['id', 'country_iso3', 'name', 'iso_3166_2', 'area_km2', 'bbox', 'lod']
        r_new = {k: r[k] for k in keep if k in r}

        # Keep only lod1 (regional view) - lod2 is for province-level detail
        if 'lod' in r_new:
            r_new['lod'] = {
                'lod1': r_new['lod'].get('lod1_regions'),
            }
            r_new['lod'] = {k: v for k, v in r_new['lod'].items() if v is not None}

        r.clear()
        r.update(r_new)

    data = quantize_coords(data, 3)

    with open(os.path.join(OUT_DIR, 'regions.json'), 'w') as f:
        json.dump(data, f, separators=(',', ':'))

    size = os.path.getsize(os.path.join(OUT_DIR, 'regions.json'))
    print(f"  regions.json: {size/1024/1024:.2f} MB")

def optimize_borders(input_file, output_file, precision=1, filter_countries=None):
    print(f"Optimizing {input_file}...")
    with open(os.path.join(IN_DIR, input_file), 'r', encoding='utf-8') as f:
        data = json.load(f)

    # Filter to only game countries if provided
    if filter_countries:
        data = {k: v for k, v in data.items() if k in filter_countries}
        print(f"  Filtered to {len(data)} countries")

    # Quantize coordinates
    data = quantize_coords(data, precision)

    with open(os.path.join(OUT_DIR, output_file), 'w') as f:
        json.dump(data, f, separators=(',', ':'))

    size = os.path.getsize(os.path.join(OUT_DIR, output_file))
    print(f"  {output_file}: {size/1024/1024:.2f} MB")

def optimize_cities():
    print("Optimizing cities.json...")
    with open(os.path.join(IN_DIR, 'cities.json'), 'r', encoding='utf-8') as f:
        data = json.load(f)

    for c in data:
        # Keep only essential fields
        keep = ['id', 'name', 'name_ascii', 'country_iso3', 'population_max',
                'pop_rank', 'is_capital', 'is_admin0_capital', 'latitude', 'longitude', 'min_zoom']
        c_new = {k: c[k] for k in keep if k in c}
        c.clear()
        c.update(c_new)

    data = quantize_coords(data, 4)  # higher precision for city points

    with open(os.path.join(OUT_DIR, 'cities.json'), 'w') as f:
        json.dump(data, f, separators=(',', ':'))

    size = os.path.getsize(os.path.join(OUT_DIR, 'cities.json'))
    print(f"  cities.json: {size/1024/1024:.2f} MB")

def optimize_small_files():
    for fname in ['regions_by_country.json', 'country_mapping_report.json']:
        print(f"Optimizing {fname}...")
        with open(os.path.join(IN_DIR, fname), 'r', encoding='utf-8') as f:
            data = json.load(f)
        with open(os.path.join(OUT_DIR, fname), 'w') as f:
            json.dump(data, f, separators=(',', ':'))
        size = os.path.getsize(os.path.join(OUT_DIR, fname))
        print(f"  {fname}: {size/1024:.1f} KB")

def load_game_countries():
    """Load game country ISO3 codes from countries.geo.json"""
    game_path = os.path.join(os.path.dirname(__file__), '..', '..', '..', 'Data', 'countries.geo.json')
    with open(game_path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    game_countries = set()
    for feat in data.get('features', []):
        props = feat.get('properties', {})
        for key in ['id', 'ISO_A3', 'ADM0_A3']:
            val = feat.get(key) or props.get(key)
            if val:
                game_countries.add(val.upper())
                break
    return game_countries

if __name__ == '__main__':
    print("=" * 50)
    print("Optimizing map data...")
    print("=" * 50)

    game_countries = load_game_countries()
    print(f"Game countries: {len(game_countries)}")

    optimize_countries()
    optimize_regions()
    optimize_borders('country_borders.json', 'country_borders.json', filter_countries=game_countries)
    # region_borders.json not needed - borders can be derived from region polygons at runtime
    optimize_cities()
    optimize_small_files()

    # Total size
    total = sum(os.path.getsize(os.path.join(OUT_DIR, f)) for f in os.listdir(OUT_DIR) if f.endswith('.json'))
    print("=" * 50)
    print(f"Total Data/map size: {total/1024/1024:.2f} MB")
    print("=" * 50)