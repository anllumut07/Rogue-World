#!/usr/bin/env python3
"""
WorldMapV5 Data Export Tool
Extracts country and city data from Natural Earth vector files to lon/lat JSON.

Input:
- ne_50m_admin_0_countries.shp (countries)
- ne_10m_populated_places_simple.shp (cities) - need to download

Output (lon/lat, NO projection):
- Data/map/v5/countries_lonlat.json
- Data/map/v5/cities_lonlat.json

Country ID: ADM0_A3 (or ISO_A3_EH for special cases)
"""

import json
import sys
from pathlib import Path
from typing import Dict, List, Tuple, Optional, Any
import math

try:
    import geopandas as gpd
    from shapely.geometry import Polygon, MultiPolygon, Point, mapping
    from shapely.ops import transform
    from shapely.validation import make_valid
    import numpy as np
except ImportError as e:
    print(f"Missing required package: {e}")
    print("Install with: pip install geopandas shapely fiona pyproj")
    sys.exit(1)

# Paths
BASE_DIR = Path(__file__).parent.parent
DATA_DIR = BASE_DIR / "Data" / "map" / "v5"
VECTOR_DIR = BASE_DIR / "Assets" / "map_data"

COUNTRIES_SHP = VECTOR_DIR / "ne_50m_admin" / "ne_50m_admin_0_countries.shp"
CITIES_SHP = VECTOR_DIR / "ne_10m_populated_places" / "ne_10m_populated_places.shp"
COASTLINE_SHP = VECTOR_DIR / "ne_50m_coastline" / "ne_50m_coastline.shp"

COUNTRIES_OUTPUT = DATA_DIR / "countries_lonlat.json"
CITIES_OUTPUT = DATA_DIR / "cities_lonlat.json"

# Ensure output directory exists
DATA_DIR.mkdir(parents=True, exist_ok=True)

# City type classification thresholds (population)
CITY_TYPE_THRESHOLDS = {
    "capital": None,       # Special: is_admin0_capital flag
    "megacity": 10_000_000,
    "major": 3_000_000,
    "large": 1_000_000,
    "medium": 500_000,
    "minor": 100_000,
    "small": 0,
}

TYPE_PRIORITY = {
    "capital": 100,
    "megacity": 90,
    "major": 80,
    "large": 70,
    "medium": 60,
    "minor": 50,
    "small": 40,
}

TYPE_MIN_ZOOM = {
    "capital": 0.3,
    "megacity": 0.5,
    "major": 0.7,
    "large": 1.0,
    "medium": 1.5,
    "minor": 2.0,
    "small": 3.0,
}

MAX_CITIES_PER_COUNTRY = {
    "capital": 1,
    "megacity": 3,
    "major": 5,
    "large": 10,
    "medium": 15,
    "minor": 20,
    "small": 30,
}

MAX_CITIES_PER_COUNTRY_TOTAL = 80


def get_country_id(row) -> str:
    """Get country ID from row, preferring ADM0_A3, falling back to ISO_A3_EH."""
    # ADM0_A3 is the primary code
    adm0_a3 = row.get('ADM0_A3', '')
    if adm0_a3 and adm0_a3 != '-99':
        return adm0_a3
    # Fallback to ISO_A3_EH
    iso_a3_eh = row.get('ISO_A3_EH', '')
    if iso_a3_eh and iso_a3_eh != '-99':
        return iso_a3_eh
    # Last resort: ISO_A3
    iso_a3 = row.get('ISO_A3', '')
    if iso_a3 and iso_a3 != '-99':
        return iso_a3
    return 'UNK'


def clean_geometry(geom, tolerance=0.02):
    """Clean and simplify geometry."""
    if geom is None or geom.is_empty:
        return None

    # Make valid (fix self-intersections, etc.)
    geom = make_valid(geom)

    # Simplify
    geom = geom.simplify(tolerance, preserve_topology=True)

    # Remove very small polygons (but keep at least the largest for each country)
    if geom.geom_type == 'Polygon':
        if geom.area < 1e-8:  # Very small threshold
            return None
    elif geom.geom_type == 'MultiPolygon':
        # Filter out tiny polygons
        polys = [p for p in geom.geoms if p.area >= 1e-8]
        if not polys:
            return None
        if len(polys) == 1:
            return polys[0]
        return MultiPolygon(polys)

    return geom


def polygon_to_lonlat(poly: Polygon) -> List[List[float]]:
    """Convert shapely Polygon exterior to list of [lon, lat] pairs."""
    coords = list(poly.exterior.coords)
    # Remove duplicate last point (shapely closes the ring)
    if len(coords) > 1 and coords[0] == coords[-1]:
        coords = coords[:-1]
    return [[float(x), float(y)] for x, y in coords]


def multipolygon_to_lonlat(mp: MultiPolygon) -> List[List[List[float]]]:
    """Convert MultiPolygon to list of exterior rings."""
    return [polygon_to_lonlat(p) for p in mp.geoms]


def get_polygon_holes(poly: Polygon) -> List[List[List[float]]]:
    """Get interior rings (holes) as list of [lon, lat] pairs."""
    holes = []
    for interior in poly.interiors:
        coords = list(interior.coords)
        if len(coords) > 1 and coords[0] == coords[-1]:
            coords = coords[:-1]
        holes.append([[float(x), float(y)] for x, y in coords])
    return holes


def get_label_point(geom) -> Tuple[float, float]:
    """Get a good label point for the geometry (representative point)."""
    # Use representative_point which is guaranteed to be within the polygon
    pt = geom.representative_point()
    return (float(pt.x), float(pt.y))


def classify_city_type(population: int, is_admin0_capital: bool) -> str:
    """Classify city by population and capital status."""
    if is_admin0_capital:
        return "capital"
    for type_name, threshold in CITY_TYPE_THRESHOLDS.items():
        if type_name != "capital" and population >= threshold:
            return type_name
    return "small"


def export_countries():
    """Export countries to lon/lat JSON."""
    print(f"Loading countries from {COUNTRIES_SHP}...")
    gdf = gpd.read_file(COUNTRIES_SHP)
    print(f"Loaded {len(gdf)} countries")

    countries = []
    stats = {
        "total": len(gdf),
        "valid_geoms": 0,
        "invalid_geoms": 0,
        "unmatched_codes": [],
        "microstates": [],
    }

    for idx, row in gdf.iterrows():
        country_id = get_country_id(row)
        name = row.get('NAME', row.get('NAME_EN', f'Country_{idx}'))

        # Get geometry
        geom = row.geometry
        if geom is None or geom.is_empty:
            stats["invalid_geoms"] += 1
            stats["unmatched_codes"].append({"id": country_id, "name": name, "reason": "empty geometry"})
            continue

        # Clean geometry
        geom = clean_geometry(geom)
        if geom is None:
            stats["invalid_geoms"] += 1
            stats["unmatched_codes"].append({"id": country_id, "name": name, "reason": "geometry too small after cleaning"})
            continue

        stats["valid_geoms"] += 1

        # Calculate bbox
        minx, miny, maxx, maxy = geom.bounds

        # Convert to lon/lat format
        if geom.geom_type == 'Polygon':
            exterior = polygon_to_lonlat(geom)
            holes = get_polygon_holes(geom)
            polygons = [{"exterior": exterior, "holes": holes}]
            label_point = get_label_point(geom)
            area = geom.area
        elif geom.geom_type == 'MultiPolygon':
            # Sort by area descending, keep largest pieces
            polys = sorted(list(geom.geoms), key=lambda p: p.area, reverse=True)
            polygons = []
            for p in polys:
                exterior = polygon_to_lonlat(p)
                holes = get_polygon_holes(p)
                polygons.append({"exterior": exterior, "holes": holes})
            # Label point from largest polygon
            label_point = get_label_point(polys[0])
            area = sum(p.area for p in polys)
        else:
            stats["invalid_geoms"] += 1
            continue

        # Check for microstates (very small countries)
        if area < 0.1:  # Rough threshold for microstates in degree^2
            stats["microstates"].append(country_id)

        countries.append({
            "id": country_id,
            "name": name,
            "bbox": [float(minx), float(miny), float(maxx), float(maxy)],
            "polygons": polygons,
            "label_point": list(label_point),
            "area": float(area),
        })

    print(f"Valid geometries: {stats['valid_geoms']}")
    print(f"Invalid/empty: {stats['invalid_geoms']}")
    print(f"Microstates: {len(stats['microstates'])} - {stats['microstates']}")

    # Write output
    with open(COUNTRIES_OUTPUT, 'w', encoding='utf-8') as f:
        json.dump({"countries": countries}, f, ensure_ascii=False, separators=(',', ':'))

    print(f"Written {len(countries)} countries to {COUNTRIES_OUTPUT}")
    return stats


def export_cities():
    """Export cities to lon/lat JSON."""
    print(f"\nLoading cities from {CITIES_SHP}...")

    if not CITIES_SHP.exists():
        print(f"ERROR: Cities shapefile not found at {CITIES_SHP}")
        print("Download from: https://naciscdn.org/naturalearth/10m/cultural/ne_10m_populated_places_simple.zip")
        return None

    gdf = gpd.read_file(CITIES_SHP)
    print(f"Loaded {len(gdf)} cities")

    # Load countries for validation
    countries_gdf = gpd.read_file(COUNTRIES_SHP)
    country_polys = {}
    for _, row in countries_gdf.iterrows():
        cid = get_country_id(row)
        if row.geometry and not row.geometry.is_empty:
            geom = clean_geometry(row.geometry)
            if geom:
                country_polys[cid] = geom

    print(f"Built polygon index for {len(country_polys)} countries")

    # Process cities
    city_candidates = []

    for idx, row in gdf.iterrows():
        try:
            name = row.get('NAME', row.get('NAMEASCII', f'City_{idx}'))
            name_ascii = row.get('NAMEASCII', name)
            pop = int(row.get('POP_MAX', 0))
            is_admin0_capital = row.get('ADM0CAP', 0) == 1
            # Also check FEATURECLA for capital
            featurecla = row.get('FEATURECLA', '')
            if featurecla == 'Admin-0 capital':
                is_admin0_capital = True

            adm0_a3 = row.get('ADM0_A3', '').upper()
            if not adm0_a3 or adm0_a3 == '-99':
                adm0_a3 = row.get('ISO_A3', '').upper()
            if not adm0_a3 or adm0_a3 == '-99':
                adm0_a3 = row.get('ISO_A3_EH', '').upper()

            geom = row.geometry
            if geom is None or geom.is_empty:
                continue

            lon = float(geom.x)
            lat = float(geom.y)

            city_type = classify_city_type(pop, is_admin0_capital)

            city_candidates.append({
                "id": f"CITY_{idx}",
                "name": name,
                "name_ascii": name_ascii,
                "adm0_a3": adm0_a3,
                "lon": lon,
                "lat": lat,
                "pop": pop,
                "is_capital": is_admin0_capital,
                "city_type": city_type,
                "priority": TYPE_PRIORITY.get(city_type, 40),
                "min_zoom": TYPE_MIN_ZOOM.get(city_type, 3.0),
            })
        except Exception as e:
            print(f"Warning: Failed to process city {idx}: {e}")

    print(f"Processed {len(city_candidates)} city candidates")

    # Match cities to countries (ADM0_A3 is primary)
    # Validate with point-in-polygon
    matched = 0
    unmatched = 0
    pip_conflicts = 0

    for city in city_candidates:
        country_id = city["adm0_a3"]
        point = Point(city["lon"], city["lat"])

        if country_id in country_polys:
            if country_polys[country_id].contains(point):
                city["matched_country"] = country_id
                city["match_method"] = "iso_validated_pip"
                matched += 1
            else:
                # PIP failed - log but keep ISO assignment
                city["matched_country"] = country_id
                city["match_method"] = "iso_authoritative"
                pip_conflicts += 1
                if pip_conflicts <= 10:
                    print(f"  PIP conflict: {city['name']} ({city['lon']:.4f}, {city['lat']:.4f}) not in {country_id}")
        else:
            # No polygon for this country (microstate/territory)
            city["matched_country"] = country_id
            city["match_method"] = "iso_only"
            unmatched += 1

    print(f"Matched by ISO+PIP: {matched}")
    print(f"ISO authoritative (PIP conflict): {pip_conflicts}")
    print(f"ISO only (no polygon): {unmatched}")

    # Enforce per-country limits
    type_order = {"capital": 0, "megacity": 1, "major": 2, "large": 3, "medium": 4, "minor": 5, "small": 6}
    city_candidates.sort(key=lambda c: (type_order.get(c["city_type"], 7), -c["pop"]))

    selected = []
    country_type_counts: Dict[str, Dict[str, int]] = {}
    country_totals: Dict[str, int] = {}

    for city in city_candidates:
        cid = city["matched_country"]
        ctype = city["city_type"]

        if cid not in country_type_counts:
            country_type_counts[cid] = {}
            country_totals[cid] = 0

        type_count = country_type_counts[cid].get(ctype, 0)
        total_count = country_totals[cid]

        max_type = MAX_CITIES_PER_COUNTRY.get(ctype, 10)
        max_total = MAX_CITIES_PER_COUNTRY_TOTAL

        if type_count >= max_type:
            continue
        if total_count >= max_total:
            continue

        if ctype == "capital" and type_count >= 1:
            continue

        selected.append(city)
        country_type_counts[cid][ctype] = type_count + 1
        country_totals[cid] = total_count + 1

    print(f"Selected {len(selected)} cities out of {len(city_candidates)} candidates")

    # Build output
    output_cities = []
    for city in selected:
        output_cities.append({
            "id": city["id"],
            "name": city["name"],
            "name_ascii": city["name_ascii"],
            "country": city["matched_country"],
            "lon": city["lon"],
            "lat": city["lat"],
            "pop": city["pop"],
            "type": city["city_type"],
            "capital": city["is_capital"],
            "priority": city["priority"],
            "min_zoom": city["min_zoom"],
        })

    # Write output
    with open(CITIES_OUTPUT, 'w', encoding='utf-8') as f:
        json.dump({"cities": output_cities}, f, ensure_ascii=False, separators=(',', ':'))

    print(f"Written {len(output_cities)} cities to {CITIES_OUTPUT}")

    # Validation report
    print("\n=== VALIDATION REPORT ===")
    print(f"Total cities selected: {len(selected)}")
    print(f"Countries represented: {len(country_totals)}")

    type_summary = {}
    for cid, types in country_type_counts.items():
        for ctype, count in types.items():
            type_summary[ctype] = type_summary.get(ctype, 0) + count

    print("\nBy type:")
    for ctype in ["capital", "megacity", "major", "large", "medium", "minor", "small"]:
        count = type_summary.get(ctype, 0)
        print(f"  {ctype}: {count}")

    print("\nTop 15 countries by city count:")
    for cid, count in sorted(country_totals.items(), key=lambda x: x[1], reverse=True)[:15]:
        print(f"  {cid}: {count}")

    # Check key countries
    validation_countries = ["TUR", "DEU", "BRA", "JPN", "EGY", "USA", "CHN", "IND", "RUS", "GBR",
                            "SGP", "MDV", "MCO", "SMR", "VCT", "LSO", "MAC", "BHR", "CPV", "TLS", "BRB"]
    print("\nValidation for key countries:")
    for cid in validation_countries:
        if cid in country_type_counts:
            types = country_type_counts[cid]
            total = sum(types.values())
            cap = "capital" in types
            print(f"  {cid}: total={total}, capital={cap}, types={dict(types)}")
            for city in selected:
                if city["matched_country"] == cid and city["city_type"] == "capital":
                    print(f"    Capital: {city['name']} (pop={city['pop']:,})")
        else:
            print(f"  {cid}: NO CITIES SELECTED!")

    return {
        "total_selected": len(selected),
        "countries": len(country_totals),
        "by_type": type_summary,
    }


def main():
    print("=" * 60)
    print("WorldMapV5 Data Export")
    print("=" * 60)

    # Export countries
    country_stats = export_countries()

    # Export cities (if shapefile exists)
    city_stats = export_cities()

    print("\n" + "=" * 60)
    print("EXPORT COMPLETE")
    print("=" * 60)
    print(f"Countries: {COUNTRIES_OUTPUT}")
    print(f"Cities: {CITIES_OUTPUT}")

    if city_stats:
        print(f"\nSummary:")
        print(f"  Countries exported: {country_stats['valid_geoms']}")
        print(f"  Cities exported: {city_stats['total_selected']}")
        print(f"  Countries with cities: {city_stats['countries']}")


if __name__ == "__main__":
    main()