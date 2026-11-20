#!/usr/bin/env python3
"""Check if test cities exist in cities_runtime.json"""

import json

with open('../Data/map/cities_runtime.json', 'r', encoding='utf-8') as f:
    data = json.load(f)
cities = data['Cities']

test_cities = [
    ("Ankara", "TUR"),
    ("Tokyo", "JPN"),
    ("Rio de Janeiro", "BRA"),
    ("Cairo", "EGY"),
    ("Sydney", "AUS"),
    ("New York", "USA"),
    ("London", "GBR"),
    ("Moscow", "RUS"),
    ("Cape Town", "ZAF"),
]

print("=== TEST CITIES IN cities_runtime.json ===")
for name, cid in test_cities:
    city = next((c for c in cities if c['Name'] == name and c['Country'] == cid), None)
    if city:
        print(f"{name} ({cid}): X={city['X']:.1f} Y={city['Y']:.1f} Type={city['Type']} Cap={city['Cap']}")
    else:
        print(f"{name} ({cid}): NOT FOUND")