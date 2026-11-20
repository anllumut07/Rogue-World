#!/usr/bin/env python3
"""Recommend zoom thresholds for city type visibility based on city counts and distribution."""

import json

with open('../Data/map/cities_runtime.json', 'r', encoding='utf-8') as f:
    data = json.load(f)
cities = data['Cities']

from collections import Counter
type_counts = Counter(c['Type'] for c in cities)

print("=== ZOOM THRESHOLD RECOMMENDATIONS ===")
print(f"Total cities: {len(cities)}")
print()

# Current type_min_zoom in process_cities.py
current = {
    "capital": 0.3,
    "megacity": 0.5,
    "major": 0.7,
    "large": 1.0,
    "medium": 1.5,
    "minor": 2.0,
    "small": 3.0,
}

print("Current thresholds:")
for t, z in current.items():
    count = sum(1 for c in cities if c['Type'] == t)
    print(f"  {t:10s}: zoom >= {z:.1f} ({count} cities)")
print()

# Recommended: progressive visibility
# At zoom 0.3 (world view): only capitals (163)
# At zoom 0.5 (continental): + megacities (11)
# At zoom 0.7 (regional): + major (42)
# At zoom 1.0 (country): + large (189)
# At zoom 1.5 (province): + medium (323)
# At zoom 2.0 (local): + minor (1232)
# At zoom 3.0 (detail): + small (2234)
# At zoom 4.0+: all

print("RECOMMENDED thresholds (progressive):")
print("  capital:    zoom >= 0.3  (163 cities)     - world view")
print("  megacity:   zoom >= 0.5  (11 cities)      - continental")
print("  major:      zoom >= 0.7  (42 cities)      - regional")
print("  large:      zoom >= 1.0  (189 cities)     - country")
print("  medium:     zoom >= 1.5  (323 cities)     - province")
print("  minor:      zoom >= 2.0  (1232 cities)    - local")
print("  small:      zoom >= 3.0  (2234 cities)    - detail")
print()

# Alpha/fade transitions
print("RECOMMENDED alpha fade (smooth transitions):")
print("  Each type fades in over 0.2 zoom range:")
for t in ["capital", "megacity", "major", "large", "medium", "minor", "small"]:
    z = current[t]
    print(f"  {t}: visible at zoom >= {z}, alpha = clamp((zoom - {z - 0.2}) / 0.2, 0, 1)")
print()

# Performance estimate
print("=== PERFORMANCE ESTIMATE ===")
for zoom in [0.3, 0.5, 0.7, 1.0, 1.5, 2.0, 3.0, 4.0]:
    visible = sum(1 for c in cities if current[c['Type']] <= zoom)
    print(f"  zoom {zoom:3.1f}: {visible:4d} cities visible ({(visible/len(cities)*100):.1f}%)")

print()
print("At default zoom 0.48: only capitals visible (163 cities) = excellent performance")
print("At zoom 1.0: 163+11+42+189 = 405 cities = good performance")
print("At zoom 2.0: 1638 cities = moderate (consider spatial grid culling)")
print("At zoom 3.0+: all 4194 cities = needs spatial grid + frustum culling")