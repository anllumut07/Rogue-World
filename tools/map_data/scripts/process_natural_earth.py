#!/usr/bin/env python3
"""
Natural Earth Data Processor for Rogue World

Converts Natural Earth shapefiles into compact game-ready format:
- Simplified polygons per LOD (countries, regions)
- Borders as linestrings
- Cities table with min-zoom by population
- Country-region-city hierarchy linking via ISO3

Outputs to: ../../Data/map/
"""

import os
import sys
import json
import shapefile
from shapely.geometry import shape, mapping, LineString, Polygon, MultiPolygon
from shapely.ops import transform, unary_union
from shapely import simplify
import numpy as np
from functools import partial
import pyproj

# ============================================================
# CONFIGURATION
# ============================================================

RAW_DIR = os.path.join(os.path.dirname(__file__), '..', 'raw')
OUT_DIR = os.path.join(os.path.dirname(__file__), '..', '..', '..', 'Data', 'map')

# LOD simplification tolerances (in degrees, WGS84)
# ~0.01° ≈ 1.1 km at equator
LOD_TOLERANCES = {
    'lod0_countries': 0.05,    # ~5.5 km - world view
    'lod1_regions': 0.02,      # ~2.2 km - regional view
    'lod2_provinces': 0.005,   # ~0.55 km - detailed view
}

# City visibility by population rank (Natural Earth POP_RANK 1-14, higher = bigger)
# We invert: rank 14 (10M+) visible at zoom 0.2, rank 1 (>200) visible at zoom 4.0
CITY_MIN_ZOOM_BY_RANK = {
    14: 0.2,   # 10M+
    13: 0.3,   # 5-10M
    12: 0.5,   # 1-5M
    11: 0.7,   # 500k-1M
    10: 1.0,   # 200k-500k
    9:  1.3,   # 100k-200k
    8:  1.6,   # 50k-100k
    7:  2.0,   # 20k-50k
    6:  2.5,   # 10k-20k
    5:  3.0,   # 5k-10k
    4:  3.5,   # 2k-5k
    3:  4.0,   # 1k-2k
    2:  4.5,   # 200-1k
    1:  5.0,   # <200
}

# Projection for area calculations (equal-area)
PROJ_WGS84 = pyproj.CRS('EPSG:4326')
PROJ_EQUAL_AREA = pyproj.CRS('ESRI:54030')  # World Robinson - good equal-area
TRANSFORMER_TO_EQ = pyproj.Transformer.from_crs(PROJ_WGS84, PROJ_EQUAL_AREA, always_xy=True)
TRANSFORMER_TO_WGS = pyproj.Transformer.from_crs(PROJ_EQUAL_AREA, PROJ_WGS84, always_xy=True)

os.makedirs(OUT_DIR, exist_ok=True)

# ============================================================
# UTILITIES
# ============================================================

def log(msg):
    print(f"[{os.path.basename(__file__)}] {msg}", flush=True)

def simplify_geom(geom, tolerance):
    """Simplify geometry preserving topology."""
    if geom is None or geom.is_empty:
        return None
    try:
        simplified = simplify(geom, tolerance, preserve_topology=True)
        if simplified.is_empty:
            return geom  # fallback to original
        return simplified
    except Exception as e:
        log(f"Warning: simplify failed: {e}")
        return geom

def geom_to_lod_dict(geom, tolerances):
    """Convert a geometry to dict of LOD geometries (as coordinate arrays)."""
    if geom is None or geom.is_empty:
        return {}

    result = {}
    for lod_name, tol in tolerances.items():
        simplified = simplify_geom(geom, tol)
        if simplified:
            result[lod_name] = mapping(simplified)['coordinates']
    return result

def polygon_to_linestring_coords(polygon_coords):
    """Extract exterior ring coordinates from polygon for border lines."""
    if not polygon_coords:
        return []
    # polygon_coords = [exterior_ring, hole1, hole2, ...]
    # exterior_ring = [[x,y], [x,y], ...]
    return polygon_coords[0]  # exterior ring only

def multipolygon_to_linestrings(multipoly_coords):
    """Extract all exterior rings from multipolygon as separate linestrings."""
    lines = []
    for poly_coords in multipoly_coords:
        if poly_coords:
            lines.append(poly_coords[0])  # exterior ring
    return lines

def compute_bbox(geom):
    """Compute bounding box [minx, miny, maxx, maxy]."""
    if geom is None or geom.is_empty:
        return None
    return list(geom.bounds)  # (minx, miny, maxx, maxy)

# ============================================================
# PROCESS ADMIN-0 COUNTRIES
# ============================================================

def process_countries():
    log("Processing Admin-0 Countries...")
    shp_path = os.path.join(RAW_DIR, 'ne_10m_admin_0_countries.shp')

    if not os.path.exists(shp_path):
        # Try unzipping
        import zipfile
        zip_path = os.path.join(RAW_DIR, 'ne_10m_admin_0_countries.zip')
        with zipfile.ZipFile(zip_path, 'r') as z:
            z.extractall(RAW_DIR)

    sf = shapefile.Reader(shp_path)
    fields = [f[0] for f in sf.fields[1:]]  # skip deletion flag

    countries = []
    country_borders = {}  # iso3 -> list of linestrings (for border rendering)

    for i, sr in enumerate(sf.shapeRecords()):
        rec = dict(zip(fields, sr.record))
        geom = shape(sr.shape.__geo_interface__)

        iso3 = rec.get('ADM0_A3', rec.get('ISO_A3', f'UNK{i:03d}'))
        name = rec.get('NAME', iso3)
        sovereign = rec.get('SOVEREIGNT', iso3)
        economy = rec.get('ECONOMY', '')
        income_grp = rec.get('INCOME_GRP', '')
        wikidata_id = rec.get('WIKIDATAID', '')

        # Compute area in km² (equal-area projection)
        geom_eq = transform(TRANSFORMER_TO_EQ.transform, geom)
        area_km2 = geom_eq.area / 1e6

        # LOD geometries
        lod_geoms = geom_to_lod_dict(geom, LOD_TOLERANCES)

        # Borders: exterior rings as linestrings
        border_lines = []
        if geom.geom_type == 'Polygon':
            border_lines.append(polygon_to_linestring_coords(mapping(geom)['coordinates']))
        elif geom.geom_type == 'MultiPolygon':
            border_lines.extend(multipolygon_to_linestrings(mapping(geom)['coordinates']))

        country_data = {
            'iso3': iso3,
            'name': name,
            'sovereign': sovereign,
            'economy': economy,
            'income_group': income_grp,
            'wikidata_id': wikidata_id,
            'area_km2': round(area_km2, 1),
            'bbox': compute_bbox(geom),
            'lod': lod_geoms,
        }
        countries.append(country_data)
        country_borders[iso3] = border_lines

        if (i + 1) % 50 == 0:
            log(f"  Processed {i + 1} countries...")

    log(f"Total countries: {len(countries)}")

    # Save
    with open(os.path.join(OUT_DIR, 'countries.json'), 'w', encoding='utf-8') as f:
        json.dump(countries, f, ensure_ascii=False, separators=(',', ':'))

    with open(os.path.join(OUT_DIR, 'country_borders.json'), 'w', encoding='utf-8') as f:
        json.dump(country_borders, f, ensure_ascii=False, separators=(',', ':'))

    return countries, country_borders

# ============================================================
# PROCESS ADMIN-1 REGIONS (States/Provinces)
# ============================================================

def process_regions():
    log("Processing Admin-1 Regions...")
    shp_path = os.path.join(RAW_DIR, 'ne_10m_admin_1_states_provinces.shp')

    if not os.path.exists(shp_path):
        import zipfile
        zip_path = os.path.join(RAW_DIR, 'ne_10m_admin_1_states_provinces.zip')
        with zipfile.ZipFile(zip_path, 'r') as z:
            z.extractall(RAW_DIR)

    sf = shapefile.Reader(shp_path)
    fields = [f[0] for f in sf.fields[1:]]

    regions = []
    region_borders = {}  # iso3 -> list of linestrings
    regions_by_country = {}

    for i, sr in enumerate(sf.shapeRecords()):
        rec = dict(zip(fields, sr.record))
        geom = shape(sr.shape.__geo_interface__)

        adm0_a3 = rec.get('adm0_a3', rec.get('ADM0_A3', '')).upper()
        name = rec.get('NAME', rec.get('NAME_ALT', f'REGION_{i}'))
        name_alt = rec.get('NAME_ALT', '')
        type_en = rec.get('TYPE_EN', '')
        iso_3166_2 = rec.get('ISO_3166_2', '')
        hasc = rec.get('HASC', '')
        fips = rec.get('FIPS', '')
        wikidata_id = rec.get('WIKIDATAID', '')

        if not adm0_a3:
            continue

        # Compute area
        geom_eq = transform(TRANSFORMER_TO_EQ.transform, geom)
        area_km2 = geom_eq.area / 1e6

        # LOD geometries (only lod1 and lod2 for regions)
        lod_geoms = geom_to_lod_dict(geom, {k: v for k, v in LOD_TOLERANCES.items() if k != 'lod0_countries'})

        # Borders
        border_lines = []
        if geom.geom_type == 'Polygon':
            border_lines.append(polygon_to_linestring_coords(mapping(geom)['coordinates']))
        elif geom.geom_type == 'MultiPolygon':
            border_lines.extend(multipolygon_to_linestrings(mapping(geom)['coordinates']))

        region_data = {
            'id': f"{adm0_a3}_{i:04d}",  # unique ID: country + index
            'country_iso3': adm0_a3,
            'name': name,
            'name_alt': name_alt,
            'type': type_en,
            'iso_3166_2': iso_3166_2,
            'hasc': hasc,
            'fips': fips,
            'wikidata_id': wikidata_id,
            'area_km2': round(area_km2, 1),
            'bbox': compute_bbox(geom),
            'lod': lod_geoms,
        }
        regions.append(region_data)

        if adm0_a3 not in region_borders:
            region_borders[adm0_a3] = []
        region_borders[adm0_a3].extend(border_lines)

        if adm0_a3 not in regions_by_country:
            regions_by_country[adm0_a3] = []
        regions_by_country[adm0_a3].append(region_data['id'])

        if (i + 1) % 500 == 0:
            log(f"  Processed {i + 1} regions...")

    log(f"Total regions: {len(regions)}")
    log(f"Countries with regions: {len(regions_by_country)}")

    # Save
    with open(os.path.join(OUT_DIR, 'regions.json'), 'w', encoding='utf-8') as f:
        json.dump(regions, f, ensure_ascii=False, separators=(',', ':'))

    with open(os.path.join(OUT_DIR, 'region_borders.json'), 'w', encoding='utf-8') as f:
        json.dump(region_borders, f, ensure_ascii=False, separators=(',', ':'))

    with open(os.path.join(OUT_DIR, 'regions_by_country.json'), 'w', encoding='utf-8') as f:
        json.dump(regions_by_country, f, ensure_ascii=False, separators=(',', ':'))

    return regions, regions_by_country

# ============================================================
# PROCESS POPULATED PLACES (Cities)
# ============================================================

def process_cities():
    log("Processing Populated Places (Cities)...")
    shp_path = os.path.join(RAW_DIR, 'ne_10m_populated_places.shp')

    if not os.path.exists(shp_path):
        import zipfile
        zip_path = os.path.join(RAW_DIR, 'ne_10m_populated_places.zip')
        with zipfile.ZipFile(zip_path, 'r') as z:
            z.extractall(RAW_DIR)

    sf = shapefile.Reader(shp_path)
    fields = [f[0] for f in sf.fields[1:]]

    cities = []

    for i, sr in enumerate(sf.shapeRecords()):
        rec = dict(zip(fields, sr.record))
        geom = shape(sr.shape.__geo_interface__)

        if geom.geom_type != 'Point':
            continue

        name = rec.get('NAME', rec.get('NAMEASCII', f'CITY_{i}'))
        name_ascii = rec.get('NAMEASCII', name)
        adm0_a3 = rec.get('adm0_a3', rec.get('ADM0_A3', '')).upper()
        adm1 = rec.get('ADM1', '')  # admin-1 name
        pop_max = rec.get('POP_MAX', 0)
        pop_min = rec.get('POP_MIN', 0)
        pop_rank = rec.get('POP_RANK', 0)
        feature_class = rec.get('FEATURECLA', '')
        latitude = rec.get('LATITUDE', geom.y)
        longitude = rec.get('LONGITUDE', geom.x)
        wikidata_id = rec.get('WIKIDATAID', '')

        if not adm0_a3:
            continue

        # Determine min zoom from pop_rank
        min_zoom = CITY_MIN_ZOOM_BY_RANK.get(pop_rank, 5.0)

        # Is capital?
        is_capital = feature_class in ('Admin-0 capital', 'Admin-1 capital')
        is_admin0_capital = feature_class == 'Admin-0 capital'
        is_admin1_capital = feature_class == 'Admin-1 capital'

        city_data = {
            'id': f"{adm0_a3}_CITY_{i:05d}",
            'name': name,
            'name_ascii': name_ascii,
            'country_iso3': adm0_a3,
            'admin1_name': adm1,
            'population_max': int(pop_max) if pop_max else 0,
            'population_min': int(pop_min) if pop_min else 0,
            'pop_rank': int(pop_rank) if pop_rank else 0,
            'is_capital': is_capital,
            'is_admin0_capital': is_admin0_capital,
            'is_admin1_capital': is_admin1_capital,
            'feature_class': feature_class,
            'latitude': float(latitude),
            'longitude': float(longitude),
            'wikidata_id': wikidata_id,
            'min_zoom': min_zoom,
        }
        cities.append(city_data)

        if (i + 1) % 1000 == 0:
            log(f"  Processed {i + 1} cities...")

    log(f"Total cities: {len(cities)}")

    # Sort by population (largest first) for rendering priority
    cities.sort(key=lambda c: c['population_max'], reverse=True)

    # Save
    with open(os.path.join(OUT_DIR, 'cities.json'), 'w', encoding='utf-8') as f:
        json.dump(cities, f, ensure_ascii=False, separators=(',', ':'))

    return cities

# ============================================================
# BUILD COUNTRY MAPPING REPORT
# ============================================================

def build_mapping_report(countries, regions_by_country, cities):
    log("Building country mapping report...")

    # Load game countries from existing data
    game_countries_path = os.path.join(os.path.dirname(__file__), '..', '..', '..', 'Data', 'countries.geo.json')
    game_countries = set()
    if os.path.exists(game_countries_path):
        with open(game_countries_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
            for feat in data.get('features', []):
                props = feat.get('properties', {})
                # Try various ID fields
                for key in ['id', 'ISO_A3', 'ADM0_A3', 'ADM0_A3_IS', 'ADM0_A3_US', 'ADM0_A3_UN', 'ADM0_A3_WB']:
                    val = feat.get(key) or props.get(key)
                    if val:
                        game_countries.add(val.upper())
                        break

    ne_countries = {c['iso3'] for c in countries}
    ne_countries_with_regions = set(regions_by_country.keys())
    ne_countries_with_cities = {c['country_iso3'] for c in cities}

    # Game countries not in NE
    game_not_in_ne = sorted(game_countries - ne_countries)
    # NE countries not in game
    ne_not_in_game = sorted(ne_countries - game_countries)
    # Game countries without NE regions
    game_no_regions = sorted([c for c in game_countries if c not in ne_countries_with_regions])
    # Game countries without NE cities
    game_no_cities = sorted([c for c in game_countries if c not in ne_countries_with_cities])

    report = {
        'summary': {
            'game_countries': len(game_countries),
            'ne_countries': len(ne_countries),
            'ne_countries_with_regions': len(ne_countries_with_regions),
            'ne_countries_with_cities': len(ne_countries_with_cities),
        },
        'game_countries_missing_in_ne': game_not_in_ne,
        'ne_countries_not_in_game': ne_not_in_game,
        'game_countries_without_regions': game_no_regions,
        'game_countries_without_cities': game_no_cities,
    }

    with open(os.path.join(OUT_DIR, 'country_mapping_report.json'), 'w', encoding='utf-8') as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    log("=== COUNTRY MAPPING REPORT ===")
    log(f"Game countries: {len(game_countries)}")
    log(f"NE countries: {len(ne_countries)}")
    log(f"NE countries with regions: {len(ne_countries_with_regions)}")
    log(f"NE countries with cities: {len(ne_countries_with_cities)}")
    log(f"Game countries MISSING in NE: {len(game_not_in_ne)} -> {game_not_in_ne[:20]}{'...' if len(game_not_in_ne) > 20 else ''}")
    log(f"NE countries NOT in game: {len(ne_not_in_game)} -> {ne_not_in_game[:20]}{'...' if len(ne_not_in_game) > 20 else ''}")
    log(f"Game countries WITHOUT regions: {len(game_no_regions)}")
    log(f"Game countries WITHOUT cities: {len(game_no_cities)}")

    return report

# ============================================================
# MAIN
# ============================================================

def main():
    log("=" * 60)
    log("Natural Earth Data Processor - Rogue World")
    log("=" * 60)

    # Process all layers
    countries, country_borders = process_countries()
    regions, regions_by_country = process_regions()
    cities = process_cities()

    # Build mapping report
    build_mapping_report(countries, regions_by_country, cities)

    # Summary stats
    log("=" * 60)
    log("STAGE A COMPLETE - Generated files:")
    for fname in sorted(os.listdir(OUT_DIR)):
        fpath = os.path.join(OUT_DIR, fname)
        size = os.path.getsize(fpath)
        log(f"  {fname}: {size:,} bytes ({size/1024:.1f} KB)")
    log("=" * 60)

if __name__ == '__main__':
    main()