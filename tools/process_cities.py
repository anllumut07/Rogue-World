#!/usr/bin/env python3
"""
Process City Data for HOI4-Style World Map
Merges Natural Earth populated places with GeoNames cities15000,
assigns cities to countries, classifies by importance, and generates
label collision avoidance data.

Output: Data/map/cities_processed.json
"""

import json
import csv
import os
import sys
from pathlib import Path
from collections import defaultdict

# Try to import optional dependencies
try:
    import geopandas as gpd
    from shapely.geometry import Point, Polygon, shape
    from shapely.strtree import STRtree
    HAS_GEO = True
except ImportError:
    HAS_GEO = False
    print("⚠️  geopandas/shapely not installed. Using fallback (no point-in-polygon).")
    print("   Install with: pip install geopandas shapely")

# Country ISO3 to name mapping (from countries.json)
COUNTRY_NAMES = {
    "USA": "United States", "CAN": "Canada", "MEX": "Mexico", "RUS": "Russia",
    "CHN": "China", "IND": "India", "BRA": "Brazil", "AUS": "Australia",
    "DEU": "Germany", "FRA": "France", "GBR": "United Kingdom", "JPN": "Japan",
    "ITA": "Italy", "KOR": "South Korea", "ESP": "Spain", "TUR": "Turkey",
    "SAU": "Saudi Arabia", "IRN": "Iran", "EGY": "Egypt", "NGA": "Nigeria",
    "ZAF": "South Africa", "ARG": "Argentina", "POL": "Poland", "PAK": "Pakistan",
    "BGD": "Bangladesh", "VNM": "Vietnam", "IDN": "Indonesia", "PHL": "Philippines",
    "THA": "Thailand", "MYS": "Malaysia", "COL": "Colombia", "VEN": "Venezuela",
    "PER": "Peru", "CHL": "Chile", "ECU": "Ecuador", "BOL": "Bolivia",
    "PRY": "Paraguay", "URY": "Uruguay", "CRI": "Costa Rica", "PAN": "Panama",
    "CUB": "Cuba", "DOM": "Dominican Republic", "HTI": "Haiti", "JAM": "Jamaica",
    "TTO": "Trinidad and Tobago", "BRB": "Barbados", "BHS": "Bahamas",
    "SLV": "El Salvador", "GTM": "Guatemala", "HND": "Honduras", "NIC": "Nicaragua",
    "CRI": "Costa Rica", "PAN": "Panama", "BLZ": "Belize", "AFG": "Afghanistan",
    "IRQ": "Iraq", "SYR": "Syria", "JOR": "Jordan", "LBN": "Lebanon", "ISR": "Israel",
    "PSE": "Palestine", "YEM": "Yemen", "OMN": "Oman", "ARE": "UAE", "QAT": "Qatar",
    "BHR": "Bahrain", "KWT": "Kuwait", "KAZ": "Kazakhstan", "UZB": "Uzbekistan",
    "TKM": "Turkmenistan", "KGZ": "Kyrgyzstan", "TJK": "Tajikistan", "MNG": "Mongolia",
    "PRK": "North Korea", "TWN": "Taiwan", "HKG": "Hong Kong", "MAC": "Macao",
    "SGP": "Singapore", "LAO": "Laos", "KHM": "Cambodia", "MMR": "Myanmar",
    "LKA": "Sri Lanka", "NPL": "Nepal", "BTN": "Bhutan", "MDV": "Maldives",
    "EGY": "Egypt", "LBY": "Libya", "TUN": "Tunisia", "DZA": "Algeria",
    "MAR": "Morocco", "MRT": "Mauritania", "SEN": "Senegal", "GMB": "Gambia",
    "GNB": "Guinea-Bissau", "GIN": "Guinea", "SLE": "Sierra Leone", "LBR": "Liberia",
    "CIV": "Ivory Coast", "GHA": "Ghana", "TGO": "Togo", "BEN": "Benin",
    "NGA": "Nigeria", "CMR": "Cameroon", "CAF": "Central African Republic",
    "TCD": "Chad", "SDN": "Sudan", "SSD": "South Sudan", "ETH": "Ethiopia",
    "ERI": "Eritrea", "DJI": "Djibouti", "SOM": "Somalia", "KEN": "Kenya",
    "TZA": "Tanzania", "UGA": "Uganda", "RWA": "Rwanda", "BDI": "Burundi",
    "COD": "DR Congo", "COG": "Congo", "GAB": "Gabon", "GNQ": "Equatorial Guinea",
    "STP": "Sao Tome and Principe", "AGO": "Angola", "ZMB": "Zambia",
    "MWI": "Malawi", "MOZ": "Mozambique", "ZWE": "Zimbabwe", "BWA": "Botswana",
    "NAM": "Namibia", "SWZ": "Eswatini", "LSO": "Lesotho", "MDG": "Madagascar",
    "MUS": "Mauritius", "SYC": "Seychelles", "COM": "Comoros", "CPV": "Cabo Verde",
    "POL": "Poland", "CZE": "Czechia", "SVK": "Slovakia", "HUN": "Hungary",
    "ROU": "Romania", "BGR": "Bulgaria", "HRV": "Croatia", "SVN": "Slovenia",
    "AUT": "Austria", "CHE": "Switzerland", "LIE": "Liechtenstein", "LUX": "Luxembourg",
    "BEL": "Belgium", "NLD": "Netherlands", "DNK": "Denmark", "FIN": "Finland",
    "SWE": "Sweden", "NOR": "Norway", "ISL": "Iceland", "IRL": "Ireland",
    "EST": "Estonia", "LVA": "Latvia", "LTU": "Lithuania", "BLR": "Belarus",
    "MDA": "Moldova", "UKR": "Ukraine", "RUS": "Russia", "GEO": "Georgia",
    "ARM": "Armenia", "AZE": "Azerbaijan", "KAZ": "Kazakhstan", "UZB": "Uzbekistan",
    "TKM": "Turkmenistan", "KGZ": "Kyrgyzstan", "TJK": "Tajikistan",
    "NZL": "New Zealand", "FJI": "Fiji", "PNG": "Papua New Guinea", "SLB": "Solomon Islands",
    "VUT": "Vanuatu", "WSM": "Samoa", "TON": "Tonga", "KIR": "Kiribati",
    "TUV": "Tuvalu", "NRU": "Nauru", "FSM": "Micronesia", "PLW": "Palau",
    "MHL": "Marshall Islands", "CUB": "Cuba", "JAM": "Jamaica", "HTI": "Haiti",
    "DOM": "Dominican Republic", "PRI": "Puerto Rico", "ABW": "Aruba",
    "CUW": "Curacao", "SXM": "Sint Maarten", "BES": "Caribbean Netherlands",
    "VGB": "British Virgin Islands", "VIR": "US Virgin Islands", "AIA": "Anguilla",
    "MSR": "Montserrat", "BMU": "Bermuda", "CYM": "Cayman Islands",
    "TCA": "Turks and Caicos", "BLM": "Saint Barthelemy", "MAF": "Saint Martin",
    "SMR": "San Marino", "VAT": "Vatican", "MCO": "Monaco", "AND": "Andorra",
    "IMN": "Isle of Man", "JEY": "Jersey", "GGY": "Guernsey", "ALA": "Aland Islands",
    "FRO": "Faroe Islands", "GRL": "Greenland", "SJM": "Svalbard and Jan Mayen",
    "ATA": "Antarctica", "BVT": "Bouvet Island", "HMD": "Heard Island",
    "IOT": "British Indian Ocean Territory", "ATF": "French Southern Territories",
    "SPM": "Saint Pierre and Miquelon", "WLF": "Wallis and Futuna",
    "PYF": "French Polynesia", "NCL": "New Caledonia", "NFK": "Norfolk Island",
    "PCN": "Pitcairn", "TKL": "Tokelau", "COK": "Cook Islands", "NIU": "Niue",
    "ASM": "American Samoa", "GUM": "Guam", "MNP": "Northern Mariana Islands",
    "UMI": "US Minor Outlying Islands", "CCK": "Cocos Islands", "CXR": "Christmas Island",
    "FLK": "Falkland Islands", "SHN": "Saint Helena", "SGS": "South Georgia",
}

# ISO2 to ISO3 mapping (GeoNames uses ISO2)
ISO2_TO_ISO3 = {
    "US": "USA", "CA": "CAN", "MX": "MEX", "RU": "RUS", "CN": "CHN", "IN": "IND",
    "BR": "BRA", "AU": "AUS", "DE": "DEU", "FR": "FRA", "GB": "GBR", "JP": "JPN",
    "IT": "ITA", "KR": "KOR", "ES": "ESP", "TR": "TUR", "SA": "SAU", "IR": "IRN",
    "EG": "EGY", "NG": "NGA", "ZA": "ZAF", "AR": "ARG", "PL": "POL", "PK": "PAK",
    "BD": "BGD", "VN": "VNM", "ID": "IDN", "PH": "PHL", "TH": "THA", "MY": "MYS",
    "CO": "COL", "VE": "VEN", "PE": "PER", "CL": "CHL", "EC": "ECU", "BO": "BOL",
    "PY": "PRY", "UY": "URY", "CR": "CRI", "PA": "PAN", "CU": "CUB", "DO": "DOM",
    "HT": "HTI", "JM": "JAM", "TT": "TTO", "BB": "BRB", "BS": "BHS",
    "SV": "SLV", "GT": "GTM", "HN": "HND", "NI": "NIC", "BZ": "BLZ",
    "AF": "AFG", "IQ": "IRQ", "SY": "SYR", "JO": "JOR", "LB": "LBN", "IL": "ISR",
    "PS": "PSE", "YE": "YEM", "OM": "OMN", "AE": "ARE", "QA": "QAT", "BH": "BHR",
    "KW": "KWT", "KZ": "KAZ", "UZ": "UZB", "TM": "TKM", "KG": "KGZ", "TJ": "TJK",
    "MN": "MNG", "KP": "PRK", "TW": "TWN", "HK": "HKG", "MO": "MAC", "SG": "SGP",
    "LA": "LAO", "KH": "KHM", "MM": "MMR", "LK": "LKA", "NP": "NPL", "BT": "BTN",
    "MV": "MDV", "LY": "LBY", "TN": "TUN", "DZ": "DZA", "MA": "MAR", "MR": "MRT",
    "SN": "SEN", "GM": "GMB", "GW": "GNB", "GN": "GIN", "SL": "SLE", "LR": "LBR",
    "CI": "CIV", "GH": "GHA", "TG": "TGO", "BJ": "BEN", "CM": "CMR", "CF": "CAF",
    "TD": "TCD", "SD": "SDN", "SS": "SSD", "ET": "ETH", "ER": "ERI", "DJ": "DJI",
    "SO": "SOM", "KE": "KEN", "TZ": "TZA", "UG": "UGA", "RW": "RWA", "BI": "BDI",
    "CD": "COD", "CG": "COG", "GA": "GAB", "GQ": "GNQ", "ST": "STP", "AO": "AGO",
    "ZM": "ZMB", "MW": "MWI", "MZ": "MOZ", "ZW": "ZWE", "BW": "BWA", "NA": "NAM",
    "SZ": "SWZ", "LS": "LSO", "MG": "MDG", "MU": "MUS", "SC": "SYC", "KM": "COM",
    "CV": "CPV", "CZ": "CZE", "SK": "SVK", "HU": "HUN", "RO": "ROU", "BG": "BGR",
    "HR": "HRV", "SI": "SVN", "AT": "AUT", "CH": "CHE", "LI": "LIE", "LU": "LUX",
    "BE": "BEL", "NL": "NLD", "DK": "DNK", "FI": "FIN", "SE": "SWE", "NO": "NOR",
    "IS": "ISL", "IE": "IRL", "EE": "EST", "LV": "LVA", "LT": "LTU", "BY": "BLR",
    "MD": "MDA", "UA": "UKR", "GE": "GEO", "AM": "ARM", "AZ": "AZE",
    "NZ": "NZL", "FJ": "FJI", "PG": "PNG", "SB": "SLB", "VU": "VUT", "WS": "WSM",
    "TO": "TON", "KI": "KIR", "TV": "TUV", "NR": "NRU", "FM": "FSM", "PW": "PLW",
    "MH": "MHL", "PR": "PRI", "AW": "ABW", "CW": "CUW", "SX": "SXM", "BQ": "BES",
    "VG": "VGB", "VI": "VIR", "AI": "AIA", "MS": "MSR", "BM": "BMU", "KY": "CYM",
    "TC": "TCA", "BL": "BLM", "MF": "MAF", "SM": "SMR", "VA": "VAT", "MC": "MCO",
    "AD": "AND", "IM": "IMN", "JE": "JEY", "GG": "GGY", "AX": "ALA", "FO": "FRO",
    "GL": "GRL", "SJ": "SJM", "AQ": "ATA", "BV": "BVT", "HM": "HMD", "IO": "IOT",
    "TF": "ATF", "PM": "SPM", "WF": "WLF", "PF": "PYF", "NC": "NCL", "NF": "NFK",
    "PN": "PCN", "TK": "TKL", "CK": "COK", "NU": "NIU", "AS": "ASM", "GU": "GUM",
    "MP": "MNP", "UM": "UMI", "CC": "CCK", "CX": "CXR", "FK": "FLK", "SH": "SHN",
    "GS": "SGS", "EH": "ESH", "TL": "TLS", "ME": "MNE", "RS": "SRB", "XK": "XKX",
}

def iso2_to_iso3(iso2):
    return ISO2_TO_ISO3.get(iso2.upper(), iso2.upper())

def classify_city(population, feature_code, is_capital):
    """Classify city by importance."""
    if is_capital or feature_code == "PPLC":
        return "capital"
    if population >= 5_000_000:
        return "megacity"
    if population >= 1_000_000:
        return "major"
    if population >= 500_000:
        return "large"
    if population >= 100_000:
        return "medium"
    if population >= 50_000:
        return "minor"
    return "small"

def get_city_priority(city_type, population):
    """Get label priority for collision avoidance."""
    priorities = {
        "capital": 100,
        "megacity": 90,
        "major": 70,
        "large": 50,
        "medium": 30,
        "minor": 20,
        "small": 10,
    }
    return priorities.get(city_type, 10)

def project_lonlat(lon, lat, map_size=(2160, 1280)):
    """Project lon/lat to map coordinates (same as WorldMap.Project)."""
    x = (float(lon) + 180.0) / 360.0 * map_size[0]
    top_margin = 80.0
    usable_h = map_size[1] - top_margin * 2
    y = top_margin + (90.0 - float(lat)) / 180.0 * usable_h
    return (x, y)

def load_country_borders():
    """Load country borders for point-in-polygon."""
    borders_path = Path("Data/map/country_borders.json")
    if not borders_path.exists():
        print(f"❌ {borders_path} not found!")
        return {}

    with open(borders_path) as f:
        data = json.load(f)

    country_polys = {}
    for iso3, rings in data.items():
        if not rings:
            continue
        # Take the largest ring (exterior)
        max_area = 0
        best_ring = None
        for ring in rings:
            if len(ring) < 3:
                continue
            # Calculate area using shoelace formula
            area = 0
            for i in range(len(ring)):
                j = (i + 1) % len(ring)
                area += ring[i][0] * ring[j][1] - ring[j][0] * ring[i][1]
            area = abs(area) / 2
            if area > max_area:
                max_area = area
                best_ring = ring

        if best_ring:
            # Project to map coordinates
            projected = [project_lonlat(p[0], p[1]) for p in best_ring]
            if HAS_GEO:
                country_polys[iso3] = Polygon(projected)
            else:
                country_polys[iso3] = projected  # Store raw for fallback

    return country_polys

def point_in_polygon_fallback(point, poly):
    """Fallback point-in-polygon without shapely."""
    x, y = point
    inside = False
    n = len(poly)
    for i in range(n):
        j = (i + 1) % n
        xi, yi = poly[i]
        xj, yj = poly[j]
        if ((yi > y) != (yj > y)) and (x < (xj - xi) * (y - yi) / (yj - yi) + xi):
            inside = not inside
    return inside

def assign_country_to_city(lon, lat, country_polys):
    """Find which country a city belongs to."""
    point = (lon, lat)
    projected_point = project_lonlat(lon, lat)

    if HAS_GEO:
        pt = Point(projected_point)
        for iso3, poly in country_polys.items():
            if poly.contains(pt):
                return iso3
    else:
        for iso3, poly in country_polys.items():
            if point_in_polygon_fallback(projected_point, poly):
                return iso3

    return None

def process_natural_earth_cities(country_polys):
    """Process Natural Earth populated places."""
    ne_path = Path("Assets/map_data/ne_10m_populated_places/ne_10m_populated_places.shp")
    if not ne_path.exists():
        print("⚠️  Natural Earth cities not found, skipping...")
        return []

    print("📍 Processing Natural Earth populated places...")
    gdf = gpd.read_file(ne_path) if HAS_GEO else None

    cities = []
    if HAS_GEO and gdf is not None:
        for idx, row in gdf.iterrows():
            pop = row.get("POP_MAX", 0) or row.get("POP_MIN", 0) or 0
            name = row.get("NAME", "") or row.get("NAMEASC", "")
            lat = row.geometry.y
            lon = row.geometry.x
            feature_code = row.get("FEATURECLA", "")
            admin0 = row.get("ADM0_A3", "")  # ISO3
            is_capital = feature_code == "Admin-0 capital"

            if pop < 15000:
                continue

            # Try to assign country
            iso3 = admin0 if admin0 else assign_country_to_city(lon, lat, country_polys)
            if not iso3:
                continue

            city_type = classify_city(pop, feature_code, is_capital)
            priority = get_city_priority(city_type, pop)
            x, y = project_lonlat(lon, lat)

            cities.append({
                "id": f"NE_{row.get('GEONAMEID', idx)}",
                "name": name,
                "name_ascii": row.get("NAMEASC", name),
                "country": iso3,
                "lat": lat,
                "lon": lon,
                "x": x,
                "y": y,
                "population": int(pop),
                "type": city_type,
                "is_capital": is_capital,
                "priority": priority,
                "source": "natural_earth"
            })

    print(f"   ✅ Processed {len(cities)} Natural Earth cities")
    return cities

def process_geonames_cities(country_polys):
    """Process GeoNames cities15000."""
    gn_path = Path("Assets/map_data/cities15000/cities15000.txt")
    if not gn_path.exists():
        print("⚠️  GeoNames cities15000 not found, skipping...")
        return []

    print("📍 Processing GeoNames cities15000...")
    cities = []
    seen = set()  # Deduplicate by name+country

    with open(gn_path, encoding="utf-8") as f:
        reader = csv.reader(f, delimiter="\t")
        for row in reader:
            if len(row) < 15:
                continue

            try:
                geonameid = row[0]
                name = row[1]
                asciiname = row[2]
                lat = float(row[4])
                lon = float(row[5])
                feature_class = row[6]
                feature_code = row[7]
                country_code = row[8]
                population = int(row[14]) if row[14] else 0

                if population < 15000:
                    continue
                if feature_class != "P":  # Only populated places
                    continue

                iso3 = iso2_to_iso3(country_code)
                if iso3 not in COUNTRY_NAMES:
                    # Try point-in-polygon as fallback
                    iso3 = assign_country_to_city(lon, lat, country_polys)
                    if not iso3:
                        continue

                is_capital = feature_code == "PPLC"
                city_type = classify_city(population, feature_code, is_capital)
                priority = get_city_priority(city_type, population)
                x, y = project_lonlat(lon, lat)

                # Deduplicate
                key = (name.lower(), iso3)
                if key in seen:
                    continue
                seen.add(key)

                cities.append({
                    "id": f"GN_{geonameid}",
                    "name": name,
                    "name_ascii": asciiname,
                    "country": iso3,
                    "lat": lat,
                    "lon": lon,
                    "x": x,
                    "y": y,
                    "population": population,
                    "type": city_type,
                    "is_capital": is_capital,
                    "priority": priority,
                    "source": "geonames"
                })
            except Exception as e:
                continue

    print(f"   ✅ Processed {len(cities)} GeoNames cities")
    return cities

def merge_cities(ne_cities, gn_cities):
    """Merge and deduplicate city lists."""
    merged = {}

    # Priority: capital > Natural Earth > GeoNames > higher population
    for city in ne_cities + gn_cities:
        key = (city["name"].lower(), city["country"])
        if key not in merged:
            merged[key] = city
        else:
            existing = merged[key]
            # Prefer capitals
            if city["is_capital"] and not existing["is_capital"]:
                merged[key] = city
            elif city["is_capital"] == existing["is_capital"]:
                # Prefer higher population
                if city["population"] > existing["population"]:
                    merged[key] = city

    return list(merged.values())

def generate_label_anchors(cities, country_polys):
    """Generate label anchors with collision avoidance priorities."""
    # Group by country
    by_country = defaultdict(list)
    for city in cities:
        by_country[city["country"]].append(city)

    # Sort cities in each country by priority
    for iso3, clist in by_country.items():
        clist.sort(key=lambda c: (-c["priority"], -c["population"]))

    # Generate anchors (simplified - real implementation would use force-directed)
    for city in cities:
        # Basic anchor: slightly offset from city position
        city["label_anchor"] = [city["x"] + 20, city["y"] - 10]
        city["label_angle"] = 0

    return cities

def generate_country_label_data(country_polys):
    """Generate label data for countries."""
    labels = {}
    for iso3, poly in country_polys.items():
        if HAS_GEO:
            centroid = poly.centroid
            x, y = centroid.x, centroid.y
            # Get bounding box
            minx, miny, maxx, maxy = poly.bounds
            width = maxx - minx
            height = maxy - miny
        else:
            # Fallback: average of polygon points
            points = poly
            x = sum(p[0] for p in points) / len(points)
            y = sum(p[1] for p in points) / len(points)
            xs = [p[0] for p in points]
            ys = [p[1] for p in points]
            width = max(xs) - min(xs)
            height = max(ys) - min(ys)

        name = COUNTRY_NAMES.get(iso3, iso3)
        priority = 100 if width > 100 else 50

        labels[iso3] = {
            "name": name,
            "anchor": [x, y],
            "bbox": [width, height],
            "priority": priority,
            "font_size": min(24, max(10, width / 20)),
            "max_chars": max(5, int(width / 15))
        }

    return labels

def generate_sea_labels():
    """Load sea labels from existing sea_labels.json."""
    sea_path = Path("Data/sea_labels.json")
    if not sea_path.exists():
        return {}

    with open(sea_path) as f:
        data = json.load(f)

    labels = {}
    for item in data:
        name = item["Name"]
        x, y = project_lonlat(item["Lon"], item["Lat"])
        labels[name] = {
            "name": name,
            "anchor": [x, y],
            "type": item["Type"],  # ocean or sea
            "min_zoom": item.get("MinZoom", 0),
            "max_zoom": item.get("MaxZoom", 999),
            "font_size": 18 if item["Type"] == "ocean" else 14,
            "italic": True
        }
    return labels

def main():
    print("=" * 60)
    print("🏙️  Processing City Data for HOI4-Style Map")
    print("=" * 60)

    # Load country polygons
    print("\n🗺️  Loading country borders...")
    country_polys = load_country_borders()
    print(f"   ✅ Loaded {len(country_polys)} country polygons")

    # Process cities
    ne_cities = process_natural_earth_cities(country_polys)
    gn_cities = process_geonames_cities(country_polys)

    # Merge
    print("\n🔀 Merging city data...")
    all_cities = merge_cities(ne_cities, gn_cities)
    print(f"   ✅ Total unique cities: {len(all_cities)}")

    # Generate label data
    print("\n🏷️  Generating label data...")
    all_cities = generate_label_anchors(all_cities, country_polys)
    country_labels = generate_country_label_data(country_polys)
    sea_labels = generate_sea_labels()

    # Statistics
    by_type = defaultdict(int)
    by_country = defaultdict(int)
    for city in all_cities:
        by_type[city["type"]] += 1
        by_country[city["country"]] += 1

    print(f"\n📊 City Statistics:")
    for ctype, count in sorted(by_type.items(), key=lambda x: -x[1]):
        print(f"   {ctype:10s}: {count}")
    print(f"   Countries with cities: {len(by_country)}")

    # Output
    output = {
        "version": "1.0",
        "generated": "2026-11-19",
        "map_size": [2160, 1280],
        "projection": "equirectangular",
        "countries": len(country_polys),
        "cities": all_cities,
        "country_labels": country_labels,
        "sea_labels": sea_labels,
        "stats": {
            "total_cities": len(all_cities),
            "by_type": dict(by_type),
            "countries_with_cities": len(by_country)
        }
    }

    output_path = Path("Data/map/cities_processed.json")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(output, f, ensure_ascii=False, indent=2)

    print(f"\n✅ Output written to: {output_path}")
    print(f"   File size: {output_path.stat().st_size / 1024:.1f} KB")

    # Also create a minimal version for runtime (without metadata)
    runtime_output = {
        "cities": [
            {
                "id": c["id"],
                "name": c["name"],
                "country": c["country"],
                "x": c["x"],
                "y": c["y"],
                "pop": c["population"],
                "type": c["type"],
                "cap": c["is_capital"],
                "pri": c["priority"],
                "lanchor": c.get("label_anchor", [c["x"] + 20, c["y"] - 10])
            }
            for c in all_cities
        ],
        "country_labels": country_labels,
        "sea_labels": sea_labels
    }

    runtime_path = Path("Data/map/cities_runtime.json")
    with open(runtime_path, "w", encoding="utf-8") as f:
        json.dump(runtime_output, f, separators=(",", ":"))

    print(f"✅ Runtime version: {runtime_path} ({runtime_path.stat().st_size / 1024:.1f} KB)")
    print("\n🎉 City processing complete!")

if __name__ == "__main__":
    if not HAS_GEO:
        print("\n⚠️  For best results, install geopandas:")
        print("   pip install geopandas shapely pyproj")
        print("   (Continuing with fallback...)\n")
    main()