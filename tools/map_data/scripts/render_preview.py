#!/usr/bin/env python3
"""
Render preview images of the generated map data using matplotlib.
Outputs to test-logs/map/preview_world.png and test-logs/map/preview_europe.png
"""

import json
import os
import matplotlib
matplotlib.use('Agg')  # Non-interactive backend
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon as MplPolygon
from matplotlib.collections import PatchCollection
import numpy as np

IN_DIR = os.path.join(os.path.dirname(__file__), '..', '..', '..', 'Data', 'map')
OUT_DIR = os.path.join(os.path.dirname(__file__), '..', '..', '..', 'test-logs', 'map')

os.makedirs(OUT_DIR, exist_ok=True)

# Game color palette (from WorldMap.cs CountryPalette)
GAME_PALETTE = [
    "#8F8259", "#7B8471", "#9C8862", "#7D5F49",
    "#6E7F8D", "#8C6E83", "#5F8F7A", "#8A9550",
    "#6B7B9C", "#9C7C5B", "#7A8F6E", "#8F6E5F",
    "#5C7A8F", "#99886B", "#6E6B8F", "#8F7A6B",
    "#4F6E5A", "#7A6E8F", "#8E8A7A", "#6E8F8A",
    "#9C6B5A", "#5C8F6B", "#8F8A6E", "#6B5E52",
]

def get_color_for_iso3(iso3):
    """Deterministic color from palette based on ISO3 hash."""
    if not iso3:
        return "#444444"
    h = 0
    for ch in iso3:
        h = h * 31 + ord(ch)
    return GAME_PALETTE[abs(h) % len(GAME_PALETTE)]

def load_geometries(json_file, lod_key=None):
    """Load geometries from optimized JSON."""
    with open(os.path.join(IN_DIR, json_file), 'r', encoding='utf-8') as f:
        data = json.load(f)

    geoms = []
    for item in data:
        if lod_key and 'lod' in item and lod_key in item['lod']:
            coords = item['lod'][lod_key]
        elif 'lod' in item and not lod_key:
            # Get first available LOD
            coords = list(item['lod'].values())[0] if item['lod'] else None
        else:
            coords = None

        if coords:
            geoms.append({
                'id': item.get('iso3') or item.get('id') or item.get('country_iso3', ''),
                'name': item.get('name', ''),
                'coords': coords,
                'color': get_color_for_iso3(item.get('iso3') or item.get('country_iso3', ''))
            })
    return geoms

def plot_geometries(ax, geoms, face_alpha=0.7, edge_color='#0E141B', edge_width=0.3):
    """Plot geometries on matplotlib axis."""
    patches = []
    colors = []

    for g in geoms:
        coords = g['coords']
        color = g['color']

        if not coords:
            continue

        # Handle both Polygon and MultiPolygon coordinate structures
        # Polygon: [exterior_ring, hole1, hole2, ...]
        # MultiPolygon: [[exterior_ring, ...], [exterior_ring, ...], ...]

        if isinstance(coords[0][0], list) and isinstance(coords[0][0][0], (int, float)):
            # Single polygon: coords = [exterior_ring, hole1, ...]
            polygons = [coords]
        else:
            # MultiPolygon: coords = [[exterior_ring, ...], [exterior_ring, ...]]
            polygons = coords

        for poly_coords in polygons:
            if not poly_coords:
                continue
            exterior = poly_coords[0]  # First ring is exterior
            if len(exterior) < 3:
                continue
            # Convert to numpy array
            pts = np.array(exterior)
            patches.append(MplPolygon(pts, closed=True))
            colors.append(color)

    if patches:
        collection = PatchCollection(patches, facecolors=colors, edgecolors=edge_color,
                                     linewidths=edge_width, alpha=face_alpha)
        ax.add_collection(collection)

def render_world():
    print("Rendering world preview...")
    fig, ax = plt.subplots(1, 1, figsize=(16, 9), dpi=150)

    # Load country borders (for outlines)
    with open(os.path.join(IN_DIR, 'country_borders.json'), 'r', encoding='utf-8') as f:
        border_data = json.load(f)

    # Plot country borders as lines
    for iso3, rings in border_data.items():
        color = get_color_for_iso3(iso3)
        for ring in rings:
            if len(ring) < 2:
                continue
            pts = np.array(ring)
            ax.plot(pts[:, 0], pts[:, 1], color='#0E141B', linewidth=0.3, alpha=0.8)

    # Load countries with lod0 (world view)
    countries = load_geometries('countries.json', 'lod0')
    plot_geometries(ax, countries, face_alpha=0.6, edge_color='#0E141B', edge_width=0.2)

    ax.set_xlim(-180, 180)
    ax.set_ylim(-90, 90)
    ax.set_aspect('equal')
    ax.set_title('Rogue World - Map Data Preview (World View)', fontsize=14)
    ax.set_xlabel('Longitude')
    ax.set_ylabel('Latitude')

    # Add ocean background
    ax.set_facecolor('#0A2B3A')

    plt.tight_layout()
    out_path = os.path.join(OUT_DIR, 'preview_world.png')
    plt.savefig(out_path, dpi=150, facecolor='#0A2B3A', edgecolor='none')
    plt.close()
    print(f"Saved: {out_path}")

def render_europe():
    print("Rendering Europe preview...")
    fig, ax = plt.subplots(1, 1, figsize=(16, 12), dpi=150)

    # Europe bounds approx
    ax.set_xlim(-15, 35)
    ax.set_ylim(35, 65)

    # Load country borders
    with open(os.path.join(IN_DIR, 'country_borders.json'), 'r', encoding='utf-8') as f:
        border_data = json.load(f)

    # Filter to Europe-ish countries
    europe_countries = {'FRA', 'DEU', 'ITA', 'ESP', 'POL', 'GBR', 'NLD', 'BEL', 'AUT', 'CHE',
                        'CZE', 'SVK', 'HUN', 'ROU', 'BGR', 'HRV', 'SVN', 'SRB', 'BIH',
                        'MNE', 'ALB', 'MKD', 'GRC', 'PRT', 'IRL', 'DNK', 'NOR', 'SWE',
                        'FIN', 'EST', 'LVA', 'LTU', 'BLR', 'UKR', 'MDA', 'LUX', 'LIE',
                        'AND', 'MCO', 'SMR', 'VAT', 'MLT', 'CYP', 'ISL', 'ROU', 'BLR'}

    for iso3, rings in border_data.items():
        if iso3 not in europe_countries:
            continue
        for ring in rings:
            if len(ring) < 2:
                continue
            pts = np.array(ring)
            ax.plot(pts[:, 0], pts[:, 1], color='#0E141B', linewidth=0.5, alpha=0.9)

    # Load countries with lod1 (regional view)
    countries = load_geometries('countries.json', 'lod1')
    eu_countries = [c for c in countries if c['id'] in europe_countries]
    plot_geometries(ax, eu_countries, face_alpha=0.7, edge_color='#0E141B', edge_width=0.3)

    # Load regions with lod1 for European countries
    regions = load_geometries('regions.json', 'lod1')
    eu_regions = [r for r in regions if r.get('id', '').split('_')[0] in europe_countries]
    plot_geometries(ax, eu_regions, face_alpha=0.4, edge_color='#444444', edge_width=0.2)

    # Load cities and label major ones
    with open(os.path.join(IN_DIR, 'cities.json'), 'r', encoding='utf-8') as f:
        cities = json.load(f)

    for city in cities:
        lon = city.get('longitude', 0)
        lat = city.get('latitude', 0)
        pop = city.get('population_max', 0)
        name = city.get('name', '')
        is_cap = city.get('is_admin0_capital', False)

        if not (-15 <= lon <= 35 and 35 <= lat <= 65):
            continue

        if pop > 1000000 or is_cap:
            size = 30 if pop > 5000000 else (20 if pop > 1000000 else 15)
            color = 'gold' if is_cap else 'white'
            ax.plot(lon, lat, 'o', markersize=size/10, color=color,
                    markeredgecolor='black', markeredgewidth=0.5, alpha=0.9)
            ax.text(lon + 0.15, lat + 0.1, name, fontsize=6, color='white',
                    fontweight='bold' if is_cap else 'normal',
                    bbox=dict(boxstyle='round,pad=0.2', facecolor='black', alpha=0.5, edgecolor='none'))

    ax.set_aspect('equal')
    ax.set_title('Rogue World - Map Data Preview (Central Europe, Regions + Cities)', fontsize=14)
    ax.set_xlabel('Longitude')
    ax.set_ylabel('Latitude')
    ax.set_facecolor('#0A2B3A')

    plt.tight_layout()
    out_path = os.path.join(OUT_DIR, 'preview_europe.png')
    plt.savefig(out_path, dpi=150, facecolor='#0A2B3A', edgecolor='none')
    plt.close()
    print(f"Saved: {out_path}")

if __name__ == '__main__':
    print("=" * 50)
    print("Rendering map previews...")
    print("=" * 50)

    render_world()
    render_europe()

    print("=" * 50)
    print("Done!")
    print("=" * 50)