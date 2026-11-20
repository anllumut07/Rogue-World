#!/usr/bin/env python3
"""
Process Natural Earth cities data into cities_runtime.json format.

Source: Data/map/cities.json (Natural Earth Populated Places 1:10m)
Output: Data/map/cities_runtime.json (game-ready format)

Steps:
1. Load cities from Data/map/cities.json (Natural Earth populated places)
2. Load country borders from Data/map/country_borders.json
3. PRIMARY MATCHING: Use city's country_iso3 (ADM0_A3/ISO_A3) as country assignment
3. VALIDATION: Point-in-polygon to verify city is actually in its assigned country
4. LOG CONFLICTS: ISO code vs PIP mismatch
4. Handle microstates (countries without polygons) via ISO code directly
5. Classify cities by type (capital, megacity, major, large, medium, minor, small)
6. Assign priority for rendering
6. Compute label anchor position
7. Output cities_runtime.json
"""

import json
import sys
from pathlib import Path
from typing import Dict, List, Tuple, Optional
import math

# Paths
DATA_DIR = Path(__file__).parent.parent / "Data" / "map"
CITIES_SOURCE = DATA_DIR / "cities.json"
BORDERS_SOURCE = DATA_DIR / "country_borders.json"
OUTPUT_FILE = DATA_DIR / "cities_runtime.json"

# Projection constants (must match MapProjection.cs)
WORLD_WIDTH = 2160.0
WORLD_HEIGHT = 1080.0
TOP_MARGIN = 80.0  # Top margin to match Natural Earth texture polar padding
USABLE_HEIGHT = 1080.0 - 80.0 * 2  # 1120.0

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

# Type priority (higher = rendered first / more visible)
TYPE_PRIORITY = {
    "capital": 100,
    "megacity": 90,
    "major": 80,
    "large": 70,
    "medium": 60,
    "minor": 50,
    "small": 40,
}

# Min zoom for city types (lower = visible at more zoomed out)
TYPE_MIN_ZOOM = {
    "capital": 0.3,
    "megacity": 0.5,
    "major": 0.7,
    "large": 1.0,
    "medium": 1.5,
    "minor": 2.0,
    "small": 3.0,
}

# Label offset for city types (x, y) in world pixels
TYPE_LABEL_OFFSET = {
    "capital": (12, -16),
    "megacity": (10, -14),
    "major": (8, -12),
    "large": (6, -10),
    "medium": (5, -8),
    "minor": (4, -6),
    "small": (3, -5),
}

# Max cities per country by type
MAX_CITIES_PER_COUNTRY = {
    "capital": 1,      # Exactly 1 per country (capital)
    "megacity": 3,
    "major": 5,
    "large": 10,
    "medium": 15,
    "minor": 20,
    "small": 30,
}

# Global max cities per country
MAX_CITIES_PER_COUNTRY_TOTAL = 80

# Countries without polygons in country_borders.json (microstates, territories)
# These will use ISO code directly without PIP validation
MICROSTATES_NO_POLYGON = {
    'ABW', 'ALA', 'AND', 'ATG', 'BHR', 'BRB', 'COM', 'CPV', 'CUW', 'DMA',
    'FSM', 'GGY', 'GRD', 'HKG', 'IMN', 'JEY', 'KIR', 'KNA', 'LCA', 'LIE',
    'LSO', 'MAC', 'MCO', 'MDV', 'MHL', 'MLT', 'MHL', 'NRU', 'PLW', 'SGP',
    'SMR', 'STP', 'SXM', 'SYC', 'TON', 'VCT', 'ATA', 'ALD', 'CYN', 'PSX',
    'TCA', 'VIR', 'VGB', 'VIR', 'UMI', 'ATA', 'ATF', 'IOT', 'SGS', 'BVT',
    'HMD', 'UMI', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA',
    'ATA', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA', 'ATA'
}

# Countries that have no polygon in borders file (microstates/territories)
# We'll detect these dynamically from the borders file


def load_json(path: Path) -> dict:
    with open(path, 'r', encoding='utf-8') as f:
        return json.load(f)


def point_in_polygon(point: Tuple[float, float], polygon: List[Tuple[float, float]]) -> bool:
    """Ray casting algorithm for point-in-polygon test."""
    x, y = point
    inside = False
    n = len(polygon)
    for i in range(n):
        x1, y1 = polygon[i]
        x2, y2 = polygon[(i + 1) % n]
        if ((y1 > y) != (y2 > y)) and (x < (x2 - x1) * (y - y1) / (y2 - y1) + x1):
            inside = not inside
    return inside


def point_in_multi_polygon(point: Tuple[float, float], polygons: List[List[Tuple[float, float]]]) -> bool:
    """Check if point is in any of the polygons (for countries with multiple parts)."""
    for poly in polygons:
        if point_in_polygon(point, poly):
            return True
    return False


def polygon_area(polygon: List[Tuple[float, float]]) -> float:
    """Calculate signed area of polygon (positive for CCW)."""
    area = 0.0
    n = len(polygon)
    for i in range(n):
        x1, y1 = polygon[i]
        x2, y2 = polygon[(i + 1) % n]
        area += x1 * y2 - x2 * y1
    return area / 2.0


def polygon_bbox(polygon: List[Tuple[float, float]]) -> Tuple[float, float, float, float]:
    """Get bounding box of polygon."""
    xs = [p[0] for p in polygon]
    ys = [p[1] for p in polygon]
    return min(xs), min(ys), max(xs), max(ys)


def distance_to_polygon(point: Tuple[float, float], polygons: List[List[Tuple[float, float]]]) -> float:
    """Minimum distance from point to any polygon edge."""
    min_dist = float('inf')
    px, py = point
    for poly in polygons:
        for i in range(len(poly)):
            x1, y1 = poly[i]
            x2, y2 = poly[(i + 1) % len(poly)]
            dx = x2 - x1
            dy = y2 - y1
            if dx == 0 and dy == 0:
                dist = math.hypot(px - x1, py - y1)
            else:
                t = max(0, min(1, ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy)))
                proj_x = x1 + t * dx
                proj_y = y1 + t * dy
                dist = math.hypot(px - proj_x, py - proj_y)
            min_dist = min(min_dist, dist)
    return min_dist


def classify_city_type(population: int, is_capital: bool, is_admin0_capital: bool) -> str:
    """Classify city by population and capital status.
    Use is_admin0_capital for national capitals (admin-0 level).
    is_capital includes admin-1 (state/province) capitals too.
    """
    if is_admin0_capital:
        return "capital"
    for type_name, threshold in CITY_TYPE_THRESHOLDS.items():
        if type_name != "capital" and population >= threshold:
            return type_name
    return "small"


def assign_type(city: dict, city_type: str) -> dict:
    """Add type metadata to city."""
    city["city_type"] = city_type
    city["priority"] = TYPE_PRIORITY.get(city_type, 40)
    city["min_zoom"] = TYPE_MIN_ZOOM.get(city_type, 3.0)
    offset = TYPE_LABEL_OFFSET.get(city_type, (4, -6))
    city["label_offset"] = offset
    return city


def compute_label_anchor(city: dict, world_pos: Tuple[float, float]) -> Tuple[float, float]:
    """Compute label anchor position (slightly offset from marker)."""
    offset = city.get("label_offset", (4, -6))
    return (world_pos[0] + offset[0], world_pos[1] + offset[1])


def point_in_polygon(point: Tuple[float, float], polygon: List[Tuple[float, float]]) -> bool:
    """Ray casting algorithm for point-in-polygon test."""
    x, y = point
    inside = False
    n = len(polygon)
    for i in range(n):
        x1, y1 = polygon[i]
        x2, y2 = polygon[(i + 1) % n]
        if ((y1 > y) != (y2 > y)) and (x < (x2 - x1) * (y - y1) / (y2 - y1) + x1):
            inside = not inside
    return inside


def point_in_multi_polygon(point: Tuple[float, float], polygons: List[List[Tuple[float, float]]]) -> bool:
    """Check if point is in any of the polygons (for countries with multiple parts)."""
    for poly in polygons:
        if point_in_polygon(point, poly):
            return True
    return False


def polygon_area(polygon: List[Tuple[float, float]]) -> float:
    """Calculate signed area of polygon (positive for CCW)."""
    area = 0.0
    n = len(polygon)
    for i in range(n):
        x1, y1 = polygon[i]
        x2, y2 = polygon[(i + 1) % n]
        area += x1 * y2 - x2 * y1
    return area / 2.0


def polygon_bbox(polygon: List[Tuple[float, float]]) -> Tuple[float, float, float, float]:
    """Get bounding box of polygon."""
    xs = [p[0] for p in polygon]
    ys = [p[1] for p in polygon]
    return min(xs), min(ys), max(xs), max(ys)


def distance_to_polygon(point: Tuple[float, float], polygons: List[List[Tuple[float, float]]]) -> float:
    """Minimum distance from point to any polygon edge."""
    min_dist = float('inf')
    px, py = point
    for poly in polygons:
        for i in range(len(poly)):
            x1, y1 = poly[i]
            x2, y2 = poly[(i + 1) % len(poly)]
            dx = x2 - x1
            dy = y2 - y1
            if dx == 0 and dy == 0:
                dist = math.hypot(px - x1, py - y1)
            else:
                t = max(0, min(1, ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy)))
                proj_x = x1 + t * dx
                proj_y = y1 + t * dy
                dist = math.hypot(px - proj_x, py - proj_y)
            min_dist = min(min_dist, dist)
    return min_dist


def classify_city_type(population: int, is_capital: bool, is_admin0_capital: bool) -> str:
    """Classify city by population and capital status.
    Use is_admin0_capital for national capitals (admin-0 level).
    is_capital includes admin-1 (state/province) capitals too.
    """
    if is_admin0_capital:
        return "capital"
    for type_name, threshold in CITY_TYPE_THRESHOLDS.items():
        if type_name != "capital" and population >= threshold:
            return type_name
    return "small"


def assign_type(city: dict, city_type: str) -> dict:
    """Add type metadata to city."""
    city["city_type"] = city_type
    city["priority"] = TYPE_PRIORITY.get(city_type, 40)
    city["min_zoom"] = TYPE_MIN_ZOOM.get(city_type, 3.0)
    offset = TYPE_LABEL_OFFSET.get(city_type, (4, -6))
    city["label_offset"] = offset
    return city


def compute_label_anchor(city: dict, world_pos: Tuple[float, float]) -> Tuple[float, float]:
    """Compute label anchor position (slightly offset from marker)."""
    offset = city.get("label_offset", (4, -6))
    return (world_pos[0] + offset[0], world_pos[1] + offset[1])


def main():
    print("Loading data...")
    cities_data = load_json(CITIES_SOURCE)
    borders_data = load_json(BORDERS_SOURCE)

    print(f"Loaded {len(cities_data)} cities from source")
    print(f"Loaded {len(borders_data)} country borders")

    # Build country polygons lookup
    country_polys = {}
    countries_with_polygons = set()
    for country_id, polygons in borders_data.items():
        if isinstance(polygons, list) and polygons:
            # Convert to list of tuples
            country_polys[country_id] = [
                [(float(p[0]), float(p[1])) for p in poly] for poly in polygons
            ]
            countries_with_polygons.add(country_id)

    print(f"Built polygon index for {len(country_polys)} countries with polygons")
    print(f"Countries without polygons (microstates): {len(set(borders_data.keys()) - set(country_polys.keys()))}")

    # Statistics
    stats = {
        "total_cities": 0,
        "matched_by_iso": 0,
        "validated_by_pip": 0,
        "conflicts_iso_vs_pip": 0,
        "assigned_via_iso_fallback": 0,
        "conflicts_detail": [],
        "unmatched": [],
    }

    # Process cities
    print("Processing cities...")
    city_candidates = []

    for city in cities_data:
        try:
            lon = float(city["longitude"])
            lat = float(city["latitude"])
            pop = int(city.get("population_max", 0))
            is_capital = bool(city.get("is_capital", False))
            is_admin0 = bool(city.get("is_admin0_capital", False))
            name = city["name"]
            name_ascii = city.get("name_ascii", name)
            iso3 = city.get("country_iso3", "").upper()
            city_id = city.get("id", f"CITY_{len(cities_data)}")

            # Convert to world coordinates (MUST match the existing country_borders.json projection)
            # The country_borders.json uses the OLD projection with top_margin=80
            world_x = (lon + 180.0) / 360.0 * 2160.0
            top_margin = 80.0
            usable_h = 1080.0 - 80.0 * 2
            world_y = top_margin + (90.0 - lat) / 180.0 * usable_h
            world_pos = (world_x, world_y)

            city_type = classify_city_type(pop, is_capital, is_admin0)

            city_info = {
                "source": city,
                "id": city.get("id", f"CITY_{len(cities_data)}"),
                "name": city["name"],
                "name_ascii": city.get("name_ascii", city["name"]),
                "country_iso3": iso3,
                "lon": lon,
                "lat": lat,
                "world_pos": world_pos,
                "pop": pop,
                "is_capital": is_capital,
                "is_admin0": is_admin0,
                "city_type": classify_city_type(pop, is_capital, is_admin0),
            }
            city_candidates.append(city_info)
        except Exception as e:
            print(f"Warning: Failed to process city {city.get('name', 'unknown')}: {e}")

    # Match countries: ISO code is PRIMARY and AUTHORITATIVE
    # PIP is only used for validation/warning, never to override ISO
    print("Matching cities to countries (ISO primary, PIP validation only)...")
    countries_with_polygons_set = set(country_polys.keys())

    for city in city_candidates:
        iso3 = city["country_iso3"]
        world_pos = city["world_pos"]

        # PRIMARY: Use ISO code as authoritative assignment
        # The ISO code from Natural Earth is the authoritative source
        if iso3 in country_polys:
            # Country has polygon - validate with PIP (for logging only)
            stats["matched_by_iso"] += 1
            in_polygon = point_in_multi_polygon(city["world_pos"], country_polys[iso3])

            if in_polygon:
                city["matched_country"] = iso3
                city["match_method"] = "iso_validated_pip"
                stats["validated_by_pip"] += 1
            else:
                # PIP validation failed for ISO country
                # Log warning but KEEP the ISO country (don't override)
                stats["conflicts_iso_vs_pip"] += 1
                stats["conflicts_detail"].append({
                    "city": city["name"],
                    "iso3": iso3,
                    "lon": city["lon"],
                    "lat": city["lat"],
                })
                print(f"  WARNING: City {city['name']} ISO={iso3} NOT in its polygon at ({city['lon']:.4f}, {city['lat']:.4f})")

                # ALWAYS use ISO country as authoritative
                city["matched_country"] = iso3
                city["match_method"] = "iso_authoritative"
                stats["assigned_via_iso_fallback"] += 1
        else:
            # No polygon for this ISO - use ISO directly (microstates/territories)
            city["matched_country"] = iso3
            city["match_method"] = "iso_only"
            stats["assigned_via_iso_fallback"] += 1

    # Statistics
    stats["total_cities"] = len(city_candidates)
    method_counts = {}
    for city in city_candidates:
        method = city.get("match_method", "unknown")
        stats[method] = stats.get(method, 0) + 1

    # Report
    print(f"\n=== COUNTRY MATCHING STATISTICS ===")
    print(f"Total cities: {stats['total_cities']}")
    print(f"  ISO + PIP validated: {stats.get('iso_validated_pip', 0)}")
    print(f"  ISO + PIP conflict (PIP overrode): {stats.get('pip_override', 0)}")
    print(f"  ISO fallback (no PIP match): {stats.get('iso_fallback', 0)}")
    print(f"  ISO only (no polygon): {stats.get('iso_only', 0)}")

    if stats["conflicts_detail"]:
        print(f"\n=== CONFLICTS (ISO vs PIP) ===")
        for c in stats["conflicts_detail"][:30]:
            print(f"  {c['city']:30s} ISO={c['iso3']} PIP={c['pip_country']} at ({c['lon']:.4f}, {c['lat']:.4f})")
        if len(stats["conflicts_detail"]) > 30:
            print(f"  ... and {len(stats['conflicts_detail']) - 30} more")

    # Now enforce per-country limits and select best cities
    print("\nEnforcing per-country limits...")

    # Sort by priority (capital first, then by population)
    type_order = {"capital": 0, "megacity": 1, "major": 2, "large": 3, "medium": 4, "minor": 5, "small": 6}
    city_candidates.sort(key=lambda c: (type_order.get(c["city_type"], 7), -c["pop"]))

    selected_cities = []
    country_type_counts: Dict[str, Dict[str, int]] = {}
    country_totals: Dict[str, int] = {}

    for city in city_candidates:
        country_id = city["matched_country"]
        ctype = city["city_type"]

        if country_id not in country_type_counts:
            country_type_counts[country_id] = {}
            country_totals[country_id] = 0

        type_count = country_type_counts[country_id].get(ctype, 0)
        total_count = country_totals[country_id]

        # Check limits
        max_type = MAX_CITIES_PER_COUNTRY.get(ctype, 10)
        max_total = MAX_CITIES_PER_COUNTRY_TOTAL

        if type_count >= max_type:
            continue
        if total_count >= max_total:
            continue

        # Special handling for capitals: ensure exactly one per country
        if ctype == "capital":
            if type_count >= 1:
                continue

        # Accept this city
        selected_cities.append(city)
        country_type_counts[country_id][ctype] = type_count + 1
        country_totals[country_id] = total_count + 1

    print(f"Selected {len(selected_cities)} cities out of {len(city_candidates)} candidates")

    # Build output
    print("Building output...")
    output_cities = []

    for city in selected_cities:
        world_pos = city["world_pos"]
        ctype = city["city_type"]
        offset = TYPE_LABEL_OFFSET.get(ctype, (4, -6))

        output_cities.append({
            "Id": city["id"],
            "Name": city["name"],
            "Country": city["matched_country"],
            "X": city["world_pos"][0],
            "Y": city["world_pos"][1],
            "Pop": city["pop"],
            "Type": ctype,
            "Cap": city["city_type"] == "capital",
            "Pri": TYPE_PRIORITY.get(ctype, 40),
            "Lanchor": [
                city["world_pos"][0] + TYPE_LABEL_OFFSET.get(ctype, (4, -6))[0],
                city["world_pos"][1] + TYPE_LABEL_OFFSET.get(ctype, (4, -6))[1],
            ],
        })

    # Wrap in CityRuntimeData format
    output = {
        "Cities": output_cities
    }

    # Write output
    with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
        json.dump(output, f, ensure_ascii=False, separators=(',', ':'))

    print(f"Written {len(output_cities)} cities to {OUTPUT_FILE}")

    # Validation report
    print(f"\n=== VALIDATION REPORT ===")
    print(f"Total cities selected: {len(selected_cities)}")
    print(f"Countries represented: {len(country_totals)}")

    type_summary = {}
    for country_id, types in country_type_counts.items():
        for ctype, count in types.items():
            type_summary[ctype] = type_summary.get(ctype, 0) + count

    print("\nBy type:")
    for ctype in ["capital", "megacity", "major", "large", "medium", "minor", "small"]:
        count = type_summary.get(ctype, 0)
        print(f"  {ctype}: {count}")

    print("\nTop 10 countries by city count:")
    for country_id, count in sorted(country_totals.items(), key=lambda x: x[1], reverse=True)[:10]:
        print(f"  {country_id}: {count}")

    # Specific countries validation
    validation_countries = ["TUR", "DEU", "BRA", "JPN", "EGY", "USA", "CHN", "IND", "RUS", "GBR", "SGP", "MDV", "MCO", "SMR", "VCT", "LSO", "MAC", "BHR", "CPV", "TLS", "BRB"]
    print("\nValidation for key countries:")
    for cid in validation_countries:
        if cid in country_type_counts:
            types = country_type_counts[cid]
            total = sum(types.values())
            cap = "capital" in types
            print(f"  {cid}: total={total}, capital={cap}, types={dict(types)}")
            for city in selected_cities:
                if city["matched_country"] == cid and city["city_type"] == "capital":
                    print(f"    Capital: {city['name']} (pop={city['pop']:,})")
        else:
            print(f"  {cid}: NO CITIES SELECTED!")

    # Distance-based assignments
    fallback_count = sum(1 for c in selected_cities if c.get("match_method") in ("iso_fallback", "iso_only"))
    if fallback_count:
        print(f"\nCities assigned via ISO fallback (no PIP validation): {fallback_count}")
        for city in selected_cities:
            if city.get("match_method") in ("iso_fallback", "iso_only"):
                dist = distance_to_polygon(city["world_pos"], country_polys.get(city["matched_country"], []))
                if dist > 10:  # Only log significant distances
                    print(f"  {city['name']:30s} -> {city['matched_country']} (dist={dist:.1f})")


if __name__ == "__main__":
    import math
    main()