#!/usr/bin/env python3
"""
WorldMapV5 Alignment Measurement Tool
Measures IoU between Natural Earth land polygons and texture land/ocean separation
to find the optimal latitude bounds (LatTop, LatBottom) for the texture.

The Natural Earth II texture (natural_earth_2.png) is 10800x5400 and covers the full world,
but the exact latitude coverage may not be exactly 90..-90 due to polar padding or projection nuances.

This script:
1. Loads NE land polygons (ne_50m_land or ne_50m_admin_0_countries)
2. Rasterizes them to texture resolution (10800x5400) for candidate latitude ranges
3. Loads texture and separates land/ocean by color (ocean = blue dominant)
4. Computes IoU for each candidate range
5. Reports best match and recommended LatTop/LatBottom constants
"""

import json
import sys
from pathlib import Path
from typing import Dict, List, Tuple, Optional
import numpy as np

try:
    import geopandas as gpd
    from shapely.geometry import Polygon, MultiPolygon, box
    from shapely.ops import transform
    import rasterio
    from rasterio.features import rasterize
    from PIL import Image
except ImportError as e:
    print(f"Missing required package: {e}")
    print("Install with: pip install geopandas shapely rasterio pillow")
    sys.exit(1)

BASE_DIR = Path(__file__).parent.parent
VECTOR_DIR = BASE_DIR / "Assets" / "map_data"
TEXTURE_DIR = BASE_DIR / "Assets" / "map_textures"

# Input files
LAND_SHP = VECTOR_DIR / "ne_50m_admin" / "ne_50m_admin_0_countries.shp"
# Alternative: COASTLINE_SHP = VECTOR_DIR / "ne_50m_coastline" / "ne_50m_coastline.shp"
TEXTURE_PATH = TEXTURE_DIR / "natural_earth_2.png"  # 10800x5400 PNG

# Texture dimensions
TEX_W = 10800
TEX_H = 5400

# Candidate latitude ranges to test (LatTop, LatBottom)
# Natural Earth textures may have polar padding
CANDIDATES = [
    (90.0, -90.0),    # Full world (default assumption)
    (85.0, -85.0),    # Common Web Mercator clamp
    (83.6, -83.6),    # Another common bound
    (80.0, -80.0),    # More conservative
    (88.0, -88.0),    # Slightly less than 90
    (86.0, -86.0),
    (84.0, -84.0),
]

def load_texture_land_mask(texture_path: Path, tex_w: int, tex_h: int) -> np.ndarray:
    """Load texture and create land mask (True=land, False=ocean).

    Ocean in Natural Earth II is blue-dominant. Land has varied colors.
    We use a simple heuristic: ocean pixels have blue > red and blue > green.
    """
    print(f"Loading texture from {texture_path}...")
    with Image.open(texture_path) as img:
        # Resize if needed
        if img.size != (tex_w, tex_h):
            print(f"  Resizing from {img.size} to ({tex_w}, {tex_h})")
            img = img.resize((tex_w, tex_h), Image.Resampling.LANCZOS)

        arr = np.array(img)
        if arr.shape[2] == 4:
            # RGBA - ignore alpha for color analysis
            rgb = arr[:, :, :3]
        else:
            rgb = arr

        # Ocean detection: blue channel dominant
        # Blue > Red and Blue > Green
        blue = rgb[:, :, 2].astype(np.float32)
        red = rgb[:, :, 0].astype(np.float32)
        green = rgb[:, :, 1].astype(np.float32)

        # Ocean mask: blue is significantly higher than red and green
        # Threshold: blue > red + 20 and blue > green + 20 (tunable)
        ocean_mask = (blue > red + 20) & (blue > green + 20)

        # Land mask is inverse
        land_mask = ~ocean_mask

        print(f"  Texture land pixels: {land_mask.sum():,} / {land_mask.size:,} ({100*land_mask.mean():.1f}%)")
        return land_mask

def rasterize_land_polygons(lat_top: float, lat_bottom: float, tex_w: int, tex_h: int) -> np.ndarray:
    """Rasterize NE land polygons to texture resolution for given lat bounds."""
    print(f"Rasterizing land polygons for Lat={lat_top:.1f}..{lat_bottom:.1f}...")

    # Load land polygons
    gdf = gpd.read_file(LAND_SHP)
    print(f"  Loaded {len(gdf)} country polygons")

    # Transform to texture pixel coordinates
    # lon [-180, 180] -> x [0, tex_w]
    # lat [lat_top, lat_bottom] -> y [0, tex_h]
    def transform_coords(x, y):
        px = (x + 180.0) / 360.0 * tex_w
        py = (lat_top - y) / (lat_top - lat_bottom) * tex_h
        return px, py

    # Create transform function
    from shapely.ops import transform as shp_transform
    import pyproj

    # Simple affine transform
    def affine_transform(x, y, z=None):
        px = (x + 180.0) / 360.0 * tex_w
        py = (lat_top - y) / (lat_top - lat_bottom) * tex_h
        if z is not None:
            return (px, py, z)
        return (px, py)

    # Rasterize all polygons
    shapes = []
    for geom in gdf.geometry:
        if geom is None or geom.is_empty:
            continue
        if geom.geom_type == 'Polygon':
            shapes.append((shp_transform(affine_transform, geom), 1))
        elif geom.geom_type == 'MultiPolygon':
            for poly in geom.geoms:
                shapes.append((shp_transform(affine_transform, poly), 1))

    print(f"  Rasterizing {len(shapes)} polygon shapes...")

    # Rasterize
    land_mask = rasterize(
        shapes,
        out_shape=(tex_h, tex_w),
        fill=0,
        dtype=np.uint8,
        all_touched=True  # Include all pixels touched by polygons
    )

    land_mask = land_mask.astype(bool)
    print(f"  Rasterized land pixels: {land_mask.sum():,} / {land_mask.size:,} ({100*land_mask.mean():.1f}%)")
    return land_mask

def compute_iou(mask1: np.ndarray, mask2: np.ndarray) -> float:
    """Compute Intersection over Union between two boolean masks."""
    intersection = np.logical_and(mask1, mask2).sum()
    union = np.logical_or(mask1, mask2).sum()
    if union == 0:
        return 0.0
    return intersection / union

def main():
    print("=" * 60)
    print("WorldMapV5 Texture Alignment Measurement")
    print("=" * 60)

    # Load texture land mask once
    if not TEXTURE_PATH.exists():
        print(f"ERROR: Texture not found at {TEXTURE_PATH}")
        sys.exit(1)

    texture_land_mask = load_texture_land_mask(TEXTURE_PATH, TEX_W, TEX_H)

    # Test each candidate
    results = []

    for lat_top, lat_bottom in CANDIDATES:
        print(f"\n--- Testing Lat={lat_top:.1f}..{lat_bottom:.1f} ---")

        try:
            poly_land_mask = rasterize_land_polygons(lat_top, lat_bottom, TEX_W, TEX_H)
            iou = compute_iou(texture_land_mask, poly_land_mask)

            # Also compute precision/recall
            intersection = np.logical_and(texture_land_mask, poly_land_mask).sum()
            precision = intersection / poly_land_mask.sum() if poly_land_mask.sum() > 0 else 0
            recall = intersection / texture_land_mask.sum() if texture_land_mask.sum() > 0 else 0
            f1 = 2 * precision * recall / (precision + recall) if (precision + recall) > 0 else 0

            results.append({
                'lat_top': lat_top,
                'lat_bottom': lat_bottom,
                'iou': iou,
                'precision': precision,
                'recall': recall,
                'f1': f1,
            })

            print(f"  IoU: {iou:.4f}, Precision: {precision:.4f}, Recall: {recall:.4f}, F1: {f1:.4f}")

        except Exception as e:
            print(f"  ERROR: {e}")
            import traceback
            traceback.print_exc()

    # Find best
    if results:
        best = max(results, key=lambda r: r['iou'])
        print("\n" + "=" * 60)
        print("BEST MATCH:")
        print(f"  LatTop: {best['lat_top']:.4f}")
        print(f"  LatBottom: {best['lat_bottom']:.4f}")
        print(f"  IoU: {best['iou']:.4f}")
        print(f"  Precision: {best['precision']:.4f}")
        print(f"  Recall: {best['recall']:.4f}")
        print(f"  F1: {best['f1']:.4f}")

        print("\nALL RESULTS:")
        print(f"{'LatTop':>8} {'LatBottom':>10} {'IoU':>8} {'Precision':>10} {'Recall':>10} {'F1':>8}")
        for r in sorted(results, key=lambda x: x['iou'], reverse=True):
            print(f"{r['lat_top']:>8.1f} {r['lat_bottom']:>10.1f} {r['iou']:>8.4f} {r['precision']:>10.4f} {r['recall']:>10.4f} {r['f1']:>8.4f}")

        # Save results
        output = {
            'texture': str(TEXTURE_PATH),
            'texture_size': [TEX_W, TEX_H],
            'best': best,
            'all_results': results,
        }

        output_path = BASE_DIR / "Data" / "map" / "v5" / "alignment_report.json"
        with open(output_path, 'w') as f:
            json.dump(output, f, indent=2)

        print(f"\nReport saved to {output_path}")

        # Print recommended constants for MapProjectionV5
        print(f"\n>>> RECOMMENDED MapProjectionV5 CONSTANTS:")
        print(f"    LatTop = {best['lat_top']:.4f};")
        print(f"    LatBottom = {best['lat_bottom']:.4f};")
        print(f"    LonLeft = -180.0;")
        print(f"    LonRight = 180.0;")
    else:
        print("No successful results!")

if __name__ == "__main__":
    main()