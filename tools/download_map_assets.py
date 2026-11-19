#!/usr/bin/env python3
"""
Download Map Assets for HOI4-Style World Map
Downloads all required assets from Natural Earth, GeoNames, and Google Fonts.

Usage:
    python tools/download_map_assets.py --all
    python tools/download_map_assets.py --rasters
    python tools/download_map_assets.py --vectors
    python tools/download_map_assets.py --cities
    python tools/download_map_assets.py --fonts
"""

import argparse
import os
import sys
import urllib.request
import zipfile
import shutil
from pathlib import Path
from tqdm import tqdm

# Asset URLs (Public Domain / CC0 / OFL)
ASSETS = {
    "rasters": [
        {
            "name": "SR_HR.tif (Shaded Relief, grayscale)",
            "url": "https://naciscdn.org/naturalearth/10m/raster/SR_HR.tif",
            "dest": "Assets/map_textures/SR_HR.tif",
            "size_mb": 150
        },
        {
            "name": "NE1_HR_LC_SR_W_DR.zip (Natural Earth I - Color + Shaded Relief + Water + Drainages)",
            "url": "https://naciscdn.org/naturalearth/10m/raster/NE1_HR_LC_SR_W_DR.zip",
            "dest": "Assets/map_textures/NE1_HR_LC_SR_W_DR.zip",
            "size_mb": 400
        },
        {
            "name": "NE2_HR_LC_SR_W_DR.zip (Natural Earth II - Idealized)",
            "url": "https://naciscdn.org/naturalearth/10m/raster/NE2_HR_LC_SR_W_DR.zip",
            "dest": "Assets/map_textures/NE2_HR_LC_SR_W_DR.zip",
            "size_mb": 400
        },
        {
            "name": "OB_HR.tif (Ocean Bathymetry - CleanTOPO2)",
            "url": "https://naciscdn.org/naturalearth/10m/raster/OB_HR.tif",
            "dest": "Assets/map_textures/OB_HR.tif",
            "size_mb": 200
        },
    ],
    "vectors": [
        {
            "name": "ne_10m_populated_places.zip (Cities)",
            "url": "https://naciscdn.org/naturalearth/10m/cultural/ne_10m_populated_places.zip",
            "dest": "Assets/map_data/ne_10m_populated_places.zip",
            "extract_to": "Assets/map_data/ne_10m_populated_places/",
            "size_mb": 5
        },
        {
            "name": "ne_10m_urban_areas.zip (Urban Areas)",
            "url": "https://naciscdn.org/naturalearth/10m/cultural/ne_10m_urban_areas.zip",
            "dest": "Assets/map_data/ne_10m_urban_areas.zip",
            "extract_to": "Assets/map_data/ne_10m_urban_areas/",
            "size_mb": 10
        },
        {
            "name": "ne_10m_rivers_lake_centerlines.zip (Rivers & Lakes)",
            "url": "https://naciscdn.org/naturalearth/10m/physical/ne_10m_rivers_lake_centerlines.zip",
            "dest": "Assets/map_data/ne_10m_rivers.zip",
            "extract_to": "Assets/map_data/ne_10m_rivers/",
            "size_mb": 15
        },
        {
            "name": "ne_10m_coastline.zip (Coastline)",
            "url": "https://naciscdn.org/naturalearth/10m/physical/ne_10m_coastline.zip",
            "dest": "Assets/map_data/ne_10m_coastline.zip",
            "extract_to": "Assets/map_data/ne_10m_coastline/",
            "size_mb": 10
        },
    ],
    "cities": [
        {
            "name": "cities15000.zip (GeoNames cities pop > 15k)",
            "url": "http://download.geonames.org/export/dump/cities15000.zip",
            "dest": "Assets/map_data/cities15000.zip",
            "extract_to": "Assets/map_data/cities15000/",
            "size_mb": 3
        },
    ],
    "fonts": [
        {
            "name": "Cinzel (Country labels)",
            "url": "https://github.com/google/fonts/raw/main/ofl/cinzel/Cinzel%5Bwght%5D.ttf",
            "dest": "Assets/fonts/Cinzel-Variable.ttf",
            "size_mb": 1
        },
        {
            "name": "Oswald (Province/UI labels)",
            "url": "https://github.com/google/fonts/raw/main/ofl/oswald/Oswald%5Bwght%5D.ttf",
            "dest": "Assets/fonts/Oswald-Variable.ttf",
            "size_mb": 1
        },
        {
            "name": "Cormorant (Sea labels)",
            "url": "https://github.com/google/fonts/raw/main/ofl/cormorant/Cormorant%5Bwght%5D.ttf",
            "dest": "Assets/fonts/Cormorant-Variable.ttf",
            "size_mb": 1
        },
    ]
}

class DownloadProgressBar(tqdm):
    def update_to(self, b=1, bsize=1, tsize=None):
        if tsize is not None:
            self.total = tsize
        self.update(b * bsize - self.n)

def download_file(url, dest_path, description=""):
    """Download a file with progress bar."""
    dest_path = Path(dest_path)
    dest_path.parent.mkdir(parents=True, exist_ok=True)

    print(f"\n📥 Downloading: {description or dest_path.name}")
    print(f"   URL: {url}")
    print(f"   Destination: {dest_path}")

    try:
        with DownloadProgressBar(unit='B', unit_scale=True, unit_divisor=1024, miniters=1) as t:
            urllib.request.urlretrieve(url, dest_path, reporthook=t.update_to)
        print(f"   ✅ Done: {dest_path} ({dest_path.stat().st_size / 1024 / 1024:.1f} MB)")
        return True
    except Exception as e:
        print(f"   ❌ Failed: {e}")
        return False

def extract_zip(zip_path, extract_to):
    """Extract zip file."""
    zip_path = Path(zip_path)
    extract_to = Path(extract_to)
    extract_to.mkdir(parents=True, exist_ok=True)

    print(f"   📦 Extracting to {extract_to}...")
    try:
        with zipfile.ZipFile(zip_path, 'r') as zip_ref:
            zip_ref.extractall(extract_to)
        print(f"   ✅ Extracted {len(zip_ref.namelist())} files")
        return True
    except Exception as e:
        print(f"   ❌ Extract failed: {e}")
        return False

def download_category(category, assets_dict):
    """Download all assets in a category."""
    if category not in assets_dict:
        print(f"❌ Unknown category: {category}")
        return False

    assets = assets_dict[category]
    print(f"\n{'='*60}")
    print(f"📂 Downloading {category.upper()} ({len(assets)} files)")
    print(f"{'='*60}")

    total_size = sum(a.get("size_mb", 0) for a in assets)
    print(f"📊 Estimated total size: ~{total_size} MB")

    success = 0
    for asset in assets:
        if download_file(asset["url"], asset["dest"], asset["name"]):
            if "extract_to" in asset:
                if extract_zip(asset["dest"], asset["extract_to"]):
                    success += 1
                else:
                    print(f"   ⚠️ Download succeeded but extract failed")
            else:
                success += 1
        print()

    print(f"\n📈 {category}: {success}/{len(assets)} successful")
    return success == len(assets)

def main():
    parser = argparse.ArgumentParser(description="Download HOI4-style map assets")
    parser.add_argument("--all", action="store_true", help="Download all assets")
    parser.add_argument("--rasters", action="store_true", help="Download raster data (large files)")
    parser.add_argument("--vectors", action="store_true", help="Download vector data")
    parser.add_argument("--cities", action="store_true", help="Download city data")
    parser.add_argument("--fonts", action="store_true", help="Download fonts")
    parser.add_argument("--list", action="store_true", help="List all available assets")

    args = parser.parse_args()

    if args.list:
        print("\n📋 Available Asset Categories:")
        for cat, assets in ASSETS.items():
            total_mb = sum(a.get("size_mb", 0) for a in assets)
            print(f"  {cat:10s} - {len(assets)} files, ~{total_mb} MB")
            for a in assets:
                print(f"    - {a['name']}")
        return

    # Create base directories
    Path("Assets/map_textures").mkdir(parents=True, exist_ok=True)
    Path("Assets/map_data").mkdir(parents=True, exist_ok=True)
    Path("Assets/fonts").mkdir(parents=True, exist_ok=True)

    if args.all:
        categories = ["rasters", "vectors", "cities", "fonts"]
    else:
        categories = []
        if args.rasters: categories.append("rasters")
        if args.vectors: categories.append("vectors")
        if args.cities: categories.append("cities")
        if args.fonts: categories.append("fonts")

    if not categories:
        print("❌ No category specified. Use --all or --rasters/--vectors/--cities/--fonts")
        parser.print_help()
        return 1

    print(f"\n🚀 Starting download for: {', '.join(categories)}")

    all_success = True
    for cat in categories:
        if not download_category(cat, ASSETS):
            all_success = False

    print(f"\n{'='*60}")
    if all_success:
        print("✅ ALL DOWNLOADS COMPLETED SUCCESSFULLY!")
        print("\n📝 Next steps:")
        print("   1. Run: python tools/process_map_assets.py")
        print("   2. Run: python tools/process_cities.py")
        print("   3. Implement WorldMapV4.cs")
    else:
        print("⚠️  Some downloads failed. Check errors above.")
        print("   You can re-run this script to retry failed downloads.")
        return 1

    return 0

if __name__ == "__main__":
    sys.exit(main())