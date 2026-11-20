#!/usr/bin/env python3
"""
Process Natural Earth 50m data into clean geometry for WorldMapV4.
- Filters tiny islands (area threshold)
- Simplifies geometries (Douglas-Peucker)
- Handles antimeridian wrapping
- Outputs: countries.json (metadata) + country_borders.json (rings)
"""

import geopandas as gpd
from shapely.geometry import Polygon, MultiPolygon, box
from shapely.ops import transform, unary_union
from shapely.validation import make_valid
import json
import math
from pathlib import Path

# Configuration
MAP_SIZE = (2160, 1280)  # WorldMapV4 map size in pixels
MIN_AREA_KM2 = 10.0      # Filter islands smaller than this (km²)
MIN_POLY_AREA_DEG2 = 0.0008  # Minimum polygon area in degrees² (~10 km²)
SIMPLIFY_TOLERANCE = 0.01  # Degrees (~1km at equator)
MAX_COORDS_PER_RING = 1500  # Cap ring complexity
MIN_RING_POINTS = 6  # Minimum points per ring (filter tiny rings)
MIN_RING_PIXEL_AREA = 4.0  # Minimum ring area in pixels² (filter visually tiny rings)

# Projection: WGS84 lat/lon
def project_to_map(lon, lat):
    """Convert lon/lat to map pixel coordinates."""
    x = (lon + 180.0) / 360.0 * MAP_SIZE[0]
    top_margin = 80.0
    usable_h = MAP_SIZE[1] - top_margin * 2
    y = top_margin + (90.0 - lat) / 180.0 * usable_h
    return (x, y)

def split_antimeridian(geom):
    """Split geometry at antimeridian (180/-180) to avoid wrapping artifacts."""
    # Create a box for the western hemisphere
    west_box = box(-180, -90, 0, 90)
    east_box = box(0, -90, 180, 90)

    parts = []
    polys_to_process = []
    if geom.geom_type == 'Polygon':
        polys_to_process = [geom]
    elif geom.geom_type == 'MultiPolygon':
        polys_to_process = list(geom.geoms)
    else:
        return geom

    for poly in polys_to_process:
        # Split by antimeridian
        west_part = poly.intersection(west_box)
        east_part = poly.intersection(east_box)

        if not west_part.is_empty:
            if west_part.geom_type == 'Polygon':
                parts.append(west_part)
            elif west_part.geom_type == 'MultiPolygon':
                parts.extend(list(west_part.geoms))

        if not east_part.is_empty:
            if east_part.geom_type == 'Polygon':
                parts.append(east_part)
            elif east_part.geom_type == 'MultiPolygon':
                parts.extend(list(east_part.geoms))

    if len(parts) > 1:
        return MultiPolygon(parts)
    elif len(parts) == 1:
        return parts[0]
    else:
        return Polygon()

def clean_polygon(poly, min_area_deg2=None):
    """Clean a single polygon: make valid, remove holes smaller than threshold, simplify."""
    if poly.is_empty:
        return None

    # Make valid (fixes self-intersections, etc.)
    poly = make_valid(poly)
    if poly.is_empty:
        return None

    if poly.geom_type != 'Polygon':
        return None

    # Filter by area - reject tiny polygons
    if min_area_deg2 is not None and poly.area < min_area_deg2:
        return None

    # Filter tiny holes (interior rings)
    if poly.interiors:
        new_interiors = []
        for interior in poly.interiors:
            hole_poly = Polygon(interior)
            # Area in degrees² (rough approximation)
            if min_area_deg2 is None or hole_poly.area >= min_area_deg2:
                new_interiors.append(interior)
        if new_interiors != list(poly.interiors):
            poly = Polygon(poly.exterior, new_interiors)

    # Simplify
    poly = poly.simplify(SIMPLIFY_TOLERANCE, preserve_topology=True)

    if poly.is_empty or not poly.is_valid:
        return None

    # Check again after simplification
    if min_area_deg2 is not None and poly.area < min_area_deg2:
        return None

    return poly

def polygon_to_rings(poly):
    """Convert Shapely polygon to list of rings (list of [lon, lat] pairs)."""
    rings = []

    # Exterior ring
    ext_coords = list(poly.exterior.coords)
    # Remove duplicate closing point
    if ext_coords[0] == ext_coords[-1]:
        ext_coords = ext_coords[:-1]

    # Cap ring size
    if len(ext_coords) > MAX_COORDS_PER_RING:
        # Downsample
        step = len(ext_coords) / MAX_COORDS_PER_RING
        ext_coords = [ext_coords[int(i * step)] for i in range(MAX_COORDS_PER_RING)]

    rings.append(ext_coords)

    # Interior rings (holes)
    for interior in poly.interiors:
        int_coords = list(interior.coords)
        if int_coords[0] == int_coords[-1]:
            int_coords = int_coords[:-1]
        if len(int_coords) >= MIN_RING_POINTS:  # At least MIN_RING_POINTS points
            if len(int_coords) > MAX_COORDS_PER_RING:
                step = len(int_coords) / MAX_COORDS_PER_RING
                int_coords = [int_coords[int(i * step)] for i in range(MAX_COORDS_PER_RING)]
            rings.append(int_coords)

    return rings

def process_countries():
    print("Reading Natural Earth 50m admin_0 countries...")
    countries = gpd.read_file('Assets/map_data/ne_50m_admin/ne_50m_admin_0_countries.shp')
    print(f"Loaded {len(countries)} countries")

    # Filter: only sovereign countries (exclude dependencies, etc.)
    # TYPE: 'Sovereign country', 'Country', etc.
    countries = countries[countries['TYPE'].isin(['Sovereign country', 'Country', 'Indeterminate'])]
    print(f"After sovereign filter: {len(countries)}")

    # Prepare output data
    countries_meta = []
    country_borders = {}

    # Min area in degrees² (roughly 25 km² at equator = 0.002 deg²)
    min_area_deg2 = MIN_POLY_AREA_DEG2

    for idx, row in countries.iterrows():
        iso3 = row.get('ISO_A3', row.get('ADM0_A3', 'UNK'))
        name = row.get('NAME', row.get('ADMIN', iso3))

        # Skip if no valid ISO3
        if iso3 == '-99' or iso3 == 'UNK' or pd.isna(iso3):
            continue

        geom = row['geometry']
        if geom is None or geom.is_empty:
            print(f"  {iso3} ({name}): SKIP - empty geometry")
            continue

        # Handle antimeridian
        geom = split_antimeridian(geom)

        # Process each polygon
        polys_to_process = []
        if geom.geom_type == 'Polygon':
            polys_to_process = [geom]
        elif geom.geom_type == 'MultiPolygon':
            polys_to_process = list(geom.geoms)
        else:
            continue

        all_rings = []
        total_area_deg2 = 0

        for poly in polys_to_process:
            # Clean polygon
            clean_poly = clean_polygon(poly, min_area_deg2)
            if clean_poly is None:
                continue

            # Calculate area (rough degrees²)
            total_area_deg2 += clean_poly.area

            # Convert to rings
            rings = polygon_to_rings(clean_poly)
            all_rings.extend(rings)

        if not all_rings:
            print(f"  {iso3} ({name}): SKIP - no valid rings after cleaning")
            continue

        # Convert rings to map coordinates
        map_rings = []
        for ring in all_rings:
            map_ring = [project_to_map(lon, lat) for lon, lat in ring]

            # Filter: skip rings with too few points
            if len(map_ring) < MIN_RING_POINTS:
                continue

            # Filter: skip rings with tiny pixel area
            # Calculate polygon area using shoelace formula
            area = 0.0
            for i in range(len(map_ring)):
                x1, y1 = map_ring[i]
                x2, y2 = map_ring[(i + 1) % len(map_ring)]
                area += x1 * y2 - x2 * y1
            area = abs(area) / 2.0

            if area < MIN_RING_PIXEL_AREA:
                continue

            map_rings.append(map_ring)

        # Store
        country_borders[iso3] = map_rings

        # Metadata
        # Convert area to km² (rough)
        area_km2 = total_area_deg2 * 111.0 * 111.0 * math.cos(math.radians(row.get('LABEL_Y', 0)))

        # Bbox in lat/lon
        bounds = geom.bounds  # (minx, miny, maxx, maxy)

        countries_meta.append({
            "iso3": iso3,
            "name": name,
            "sovereign": row.get('SOVEREIGNT', name),
            "area_km2": round(area_km2, 1),
            "bbox": [round(bounds[0], 3), round(bounds[1], 3), round(bounds[2], 3), round(bounds[3], 3)],
            "lod": {}  # Empty for now, can add LOD later
        })

        print(f"  {iso3} ({name}): {len(map_rings)} rings, {sum(len(r) for r in map_rings)} pts, {area_km2:.0f} km²")

    # Write output files
    output_dir = Path('Data/map')
    output_dir.mkdir(parents=True, exist_ok=True)

    with open(output_dir / 'countries.json', 'w', encoding='utf-8') as f:
        json.dump(countries_meta, f, ensure_ascii=False, indent=2)

    with open(output_dir / 'country_borders.json', 'w', encoding='utf-8') as f:
        json.dump(country_borders, f, ensure_ascii=False, separators=(',', ':'))

    print(f"\nDone! Written {len(countries_meta)} countries to:")
    print(f"  {output_dir / 'countries.json'}")
    print(f"  {output_dir / 'country_borders.json'}")

    # Validation
    validate_output(countries_meta, country_borders)

def validate_output(countries_meta, country_borders):
    print("\n=== VALIDATION ===")

    # Check for duplicate consecutive points
    dupe_count = 0
    tiny_edge_count = 0

    for iso3, rings in country_borders.items():
        for ring in rings:
            for i in range(len(ring)):
                p1 = ring[i]
                p2 = ring[(i + 1) % len(ring)]

                if p1 == p2:
                    dupe_count += 1

                dx = p2[0] - p1[0]
                dy = p2[1] - p1[1]
                dist = math.hypot(dx, dy)
                if dist < 0.5:  # Less than 0.5 pixels
                    tiny_edge_count += 1

    print(f"Countries: {len(countries_meta)}")
    print(f"Countries with borders: {len(country_borders)}")
    print(f"Duplicate consecutive points: {dupe_count}")
    print(f"Tiny edges (<0.5px): {tiny_edge_count}")

    # Ring count stats
    ring_counts = [len(rings) for rings in country_borders.values()]
    pt_counts = [sum(len(r) for r in rings) for rings in country_borders.values()]

    print(f"Ring counts: min={min(ring_counts)}, max={max(ring_counts)}, avg={sum(ring_counts)/len(ring_counts):.1f}")
    print(f"Point counts: min={min(pt_counts)}, max={max(pt_counts)}, avg={sum(pt_counts)/len(pt_counts):.1f}")

    # Countries with many rings
    print("\nCountries with >20 rings:")
    for iso3, rings in country_borders.items():
        if len(rings) > 20:
            meta = next((c for c in countries_meta if c['iso3'] == iso3), None)
            name = meta['name'] if meta else 'Unknown'
            print(f"  {iso3} ({name}): {len(rings)} rings, {sum(len(r) for r in rings)} pts")

if __name__ == '__main__':
    import pandas as pd
    process_countries()