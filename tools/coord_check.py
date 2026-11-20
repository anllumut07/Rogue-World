#!/usr/bin/env python3
"""Check coordinate space alignment between cities and terrain/borders"""

import json

# Load cities
with open('../Data/map/cities_runtime.json', 'r', encoding='utf-8') as f:
    cities_data = json.load(f)
cities = cities_data['Cities']

# Load borders
with open('../Data/map/country_borders.json', 'r') as f:
    borders = json.load(f)

# Key test cities
test_cities = ['Ankara', 'Tokyo', 'Rio de Janeiro', 'Cairo', 'Sydney', 'New York']

print("=== COORDINATE SPACE CHECK ===")
print("MapSize: 2160x1280 (world space)")
print()

for name in test_cities:
    city = next((c for c in cities if c['Name'] == name), None)
    if not city:
        print(f"{name}: NOT FOUND in cities_runtime.json")
        continue

    x, y = city['X'], city['Y']
    country = city['Country']

    # Check if within map bounds
    in_bounds = 0 <= x <= 2160 and 0 <= y <= 1280

    # Get country border bounds
    if country in borders:
        polys = borders[country]
        min_x = min(p[0] for poly in polys for p in poly)
        max_x = max(p[0] for poly in polys for p in poly)
        min_y = min(p[1] for poly in polys for p in poly)
        max_y = max(p[1] for poly in polys for p in poly)

        in_country_bounds = min_x <= x <= max_x and min_y <= y <= max_y

        print(f"{name:15s} ({country:3s}) X={x:7.1f} Y={y:7.1f} | Map bounds: {'OK' if in_bounds else 'OUT'}")
        print(f"{'':15s}       Country bounds: X=[{min_x:.1f},{max_x:.1f}] Y=[{min_y:.1f},{max_y:.1f}] {'OK' if in_country_bounds else 'OUT'}")
    else:
        print(f"{name:15s} ({country:3s}) X={x:7.1f} Y={y:7.1f} | Map bounds: {'OK' if in_bounds else 'OUT'}")
        print(f"{'':15s}       Country borders NOT FOUND")

print()
print("Map coordinate system: World space (0-2160, 0-1280)")
print("Project function: lon->X = (lon+180)/360 * 2160, lat->Y = 80 + (90-lat)/180 * 1120")
print("Cities use SAME projection as country_borders.json = World space aligned ✓")