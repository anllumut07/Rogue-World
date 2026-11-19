# HOI4-Style World Map Implementation Plan

## Overview
Transform the current flat-colored country polygons + dotted ocean into a Hearts of Iron 4 style strategic map with:
- Shaded relief terrain (mountains, forests, deserts, tundra visible)
- Semi-transparent country colors over terrain
- Coastal glow + inner border highlights
- LOD system: strategic view (countries only) → operational (provinces) → tactical (cities)
- Proper labels with collision avoidance
- City markers (capitals, major cities, ports)

---

## Phase 1: Asset Acquisition & Preparation

### 1.1 Download Required Assets (Public Domain / CC0 / OFL)

| Asset | Source | URL | License | Target Path |
|-------|--------|-----|---------|-------------|
| **Shaded Relief (Grayscale, Land only)** | Natural Earth 1:10m | `https://naciscdn.org/naturalearth/10m/raster/SR_HR.tif` | Public Domain | `Assets/map_textures/SR_HR.tif` |
| **Natural Earth I (Cross-blended hypsometric + shaded relief + water + drainages)** | Natural Earth 1:10m | `https://naciscdn.org/naturalearth/10m/raster/NE1_HR_LC_SR_W_DR.zip` | Public Domain | `Assets/map_textures/NE1_HR_LC_SR_W_DR/` |
| **Natural Earth II (Idealized, pre-modern palette)** | Natural Earth 1:10m | `https://naciscdn.org/naturalearth/10m/raster/NE2_HR_LC_SR_W_DR.zip` | Public Domain | `Assets/map_textures/NE2_HR_LC_SR_W_DR/` |
| **Ocean Bottom (CleanTOPO2 bathymetry)** | Natural Earth 1:10m | `https://naciscdn.org/naturalearth/10m/raster/OB_HR.tif` | Public Domain | `Assets/map_textures/OB_HR.tif` |
| **Populated Places (Cities)** | Natural Earth 1:10m | `https://naciscdn.org/naturalearth/10m/cultural/ne_10m_populated_places.zip` | Public Domain | `Assets/map_data/ne_10m_populated_places/` |
| **Urban Areas** | Natural Earth 1:10m | `https://naciscdn.org/naturalearth/10m/cultural/ne_10m_urban_areas.zip` | Public Domain | `Assets/map_data/ne_10m_urban_areas/` |
| **Rivers/Lake Centerlines** | Natural Earth 1:10m | `https://naciscdn.org/naturalearth/10m/physical/ne_10m_rivers_lake_centerlines.zip` | Public Domain | `Assets/map_data/ne_10m_rivers/` |
| **Coastline** | Natural Earth 1:10m | `https://naciscdn.org/naturalearth/10m/physical/ne_10m_coastline.zip` | Public Domain | `Assets/map_data/ne_10m_coastline/` |
| **Admin-0 Countries (vector)** | Natural Earth 1:10m | Already have (countries.json, country_borders.json) | Public Domain | `Data/map/` |
| **Admin-1 States/Provinces** | Natural Earth 1:10m | Already have (regions.json) | Public Domain | `Data/map/` |

### 1.2 Supplementary City Data (GeoNames - CC BY 4.0)
| Asset | Source | URL | License | Target Path |
|-------|--------|-----|---------|-------------|
| **Cities 15000** (pop > 15k) | GeoNames | `http://download.geonames.org/export/dump/cities15000.zip` | CC BY 4.0 | `Assets/map_data/cities15000.txt` |
| **All Countries** (for validation) | GeoNames | `http://download.geonames.org/export/dump/allCountries.zip` | CC BY 4.0 | `Assets/map_data/allCountries.txt` |

### 1.3 Fonts (OFL - Open Font License)
| Font | Use Case | URL | License |
|------|----------|-----|---------|
| **Cinzel** | Country labels (elegant serif) | https://fonts.google.com/specimen/Cinzel | OFL |
| **Oswald** | Province/UI labels (condensed sans) | https://fonts.google.com/specimen/Oswald | OFL |
| **Cormorant** | Sea/Ocean labels (italic, elegant) | https://fonts.google.com/specimen/Cormorant | OFL |

### 1.4 Asset Processing Pipeline

```bash
# Create directory structure
mkdir -p Assets/map_textures
mkdir -p Assets/map_data/ne_10m_populated_places
mkdir -p Assets/map_data/ne_10m_urban_areas
mkdir -p Assets/map_data/ne_10m_rivers
mkdir -p Assets/map_data/ne_10m_coastline
mkdir -p Assets/map_data/cities15000
mkdir -p Assets/fonts

# Download Natural Earth rasters (large files - do manually or via script)
# SR_HR.tif - Grayscale shaded relief (land only)
# NE1_HR_LC_SR_W_DR.zip - Full color cross-blended hypsometric with shaded relief
# NE2_HR_LC_SR_W_DR.zip - Natural Earth II (idealized)
# OB_HR.tif - Ocean bathymetry

# Download vector data
# ne_10m_populated_places.zip -> extract to Assets/map_data/ne_10m_populated_places/
# ne_10m_urban_areas.zip -> extract to Assets/map_data/ne_10m_urban_areas/
# ne_10m_rivers_lake_centerlines.zip -> extract to Assets/map_data/ne_10m_rivers/
# ne_10m_coastline.zip -> extract to Assets/map_data/ne_10m_coastline/

# Download GeoNames cities15000
# cities15000.zip -> extract to Assets/map_data/cities15000/

# Download fonts from Google Fonts
# Cinzel-Regular.ttf, Cinzel-Bold.ttf
# Oswald-Regular.ttf, Oswald-Medium.ttf, Oswald-Bold.ttf
# Cormorant-Italic.ttf, Cormorant-Regular.ttf
```

### 1.5 Raster Processing (GDAL/ Python)

Create `tools/process_map_assets.py`:
```python
#!/usr/bin/env python3
"""
Process Natural Earth raster assets for Godot:
1. Convert GeoTIFF to PNG (Godot friendly)
2. Generate mipmaps for LOD
3. Create terrain color ramp texture
4. Create ocean bathymetry texture
5. Split large rasters into tiles if needed
"""

import subprocess
import os
from pathlib import Path

ASSETS_DIR = Path("Assets/map_textures")

def run_gdal_translate(src, dst, **kwargs):
    """Convert GeoTIFF to PNG with options."""
    cmd = ["gdal_translate", "-of", "PNG"]
    for k, v in kwargs.items():
        cmd.extend([f"-{k}", str(v)])
    cmd.extend([str(src), str(dst)])
    subprocess.run(cmd, check=True)

def create_mipmaps(src_dir, base_name):
    """Generate mipmap chain for LOD."""
    # 21600x10800 -> 10800x5400 -> 5400x2700 -> 2700x1350 -> 1350x675
    pass

# Main processing
if __name__ == "__main__":
    # 1. Convert SR_HR.tif (shaded relief) -> grayscale PNG
    # 2. Convert NE1_HR_LC_SR_W_DR.tif -> color PNG
    # 3. Convert OB_HR.tif -> bathymetry PNG
    # 4. Generate mipmaps for each
    pass
```

**GDAL Commands for manual processing:**
```bash
# Shaded relief (grayscale) - land only
gdal_translate -of PNG -ot Byte -scale 0 255 Assets/map_textures/SR_HR.tif Assets/map_textures/shaded_relief.png

# Natural Earth I (color with shaded relief)
gdal_translate -of PNG -ot Byte Assets/map_textures/NE1_HR_LC_SR_W_DR/NE1_HR_LC_SR_W_DR.tif Assets/map_textures/natural_earth_1.png

# Natural Earth II (idealized)
gdal_translate -of PNG -ot Byte Assets/map_textures/NE2_HR_LC_SR_W_DR/NE2_HR_LC_SR_W_DR.tif Assets/map_textures/natural_earth_2.png

# Ocean bathymetry
gdal_translate -of PNG -ot Byte -scale 0 10000 Assets/map_textures/OB_HR.tif Assets/map_textures/ocean_bathymetry.png

# Generate mipmaps (Godot can do this automatically on import with "Mipmaps" enabled)
```

---

## Phase 2: Data Pipeline - Cities & Labels

### 2.1 City Data Processing

Create `tools/process_cities.py`:
```python
#!/usr/bin/env python3
"""
Process city data from Natural Earth + GeoNames:
1. Merge NE populated_places with GeoNames cities15000
2. Assign cities to countries (ISO3) via point-in-polygon
3. Classify: Capital / Major (>1M) / Minor (>100k) / Tiny (>15k)
4. Generate city_positions.json for runtime
5. Create label priority data for collision avoidance
"""

import json
import csv
from pathlib import Path
from shapely.geometry import Point, Polygon, shape
from shapely.strtree import STRtree
import geopandas as gpd

def process_cities():
    # Load country polygons from country_borders.json
    with open("Data/map/country_borders.json") as f:
        country_borders = json.load(f)
    
    # Build country geometry index
    country_geoms = {}
    for iso3, rings in country_borders.items():
        polygons = [Polygon(ring[0]) for ring in rings if len(ring) > 0]
        if polygons:
            country_geoms[iso3] = polygons[0] if len(polygons) == 1 else Polygon(polygons)
    
    tree = STRtree(list(country_geoms.values()))
    iso3_list = list(country_geoms.keys())
    
    # Process Natural Earth populated places
    ne_cities = gpd.read_file("Assets/map_data/ne_10m_populated_places/ne_10m_populated_places.shp")
    
    # Process GeoNames cities15000
    gn_cities = []
    with open("Assets/map_data/cities15000/cities15000.txt", encoding="utf-8") as f:
        reader = csv.DictReader(f, delimiter="\t", fieldnames=[
            "geonameid", "name", "asciiname", "alternatenames", "latitude", "longitude",
            "feature_class", "feature_code", "country_code", "cc2", "admin1_code",
            "admin2_code", "admin3_code", "admin3_code", "population", "elevation",
            "dem", "timezone", "modification_date"
        ])
        for row in reader:
            if int(row["population"]) >= 15000:
                gn_cities.append(row)
    
    # Merge & deduplicate
    # Assign each city to country via point-in-polygon
    # Classify: capital (feature_code=PPLC), major (>1M), minor (>100k), small (>15k)
    # Output: Data/map/cities_processed.json
    
    cities_output = {
        "cities": [],
        "label_priorities": {}  # For collision avoidance
    }
    
    with open("Data/map/cities_processed.json", "w") as f:
        json.dump(cities_output, f, ensure_ascii=False, indent=2)

if __name__ == "__main__":
    process_cities()
```

### 2.2 Label Collision Avoidance Data

Generate label positions with priorities:
```json
{
  "countries": {
    "USA": { "anchor": [1080, 400], "bbox": [400, 200], "priority": 100, "font_size": 24 },
    "FRA": { "anchor": [1100, 380], "bbox": [150, 100], "priority": 90, "font_size": 18 },
    ...
  },
  "cities": {
    "PARIS": { "pos": [1105, 385], "country": "FRA", "pop": 2161000, "is_capital": true, "priority": 80 },
    "LYON": { "pos": [1120, 400], "country": "FRA", "pop": 516000, "is_capital": false, "priority": 50 },
    ...
  },
  "seas": {
    "MEDITERRANEAN SEA": { "anchor": [1150, 500], "rotation": 0.2, "priority": 30 },
    ...
  }
}
```

---

## Phase 3: WorldMapV4 - HOI4 Style Renderer

### 3.1 Architecture Overview

```
WorldMapV4 (Node2D)
├── CanvasLayer (Layer -2) - Ocean Bathymetry
│   └── TextureRect (OB_HR.tif with gradient overlay)
├── CanvasLayer (Layer -1) - Terrain Base
│   ├── TextureRect (NE1_HR_LC_SR_W_DR - full color)
│   └── TextureRect (Shaded Relief - multiply blend, 30% opacity)
├── CanvasLayer (Layer 0) - Country Fills
│   ├── Polygon2D per country (semi-transparent, 55% alpha)
│   ├── Line2D per country border (dark, zoom-adaptive width)
│   └── Line2D per country coastal glow (inner, turquoise)
├── CanvasLayer (Layer 1) - Province Borders
│   └── Line2D per province (visible at zoom > 0.5)
├── CanvasLayer (Layer 2) - Rivers/Coastline
│   └── Line2D (rivers: blue, coastline: dark)
├── CanvasLayer (Layer 3) - City Markers
│   ├── Sprite2D per city (capital star / circle / port anchor)
│   └── Label per city (zoom-adaptive visibility)
├── CanvasLayer (Layer 4) - Labels
│   ├── Label per country (curved along shape, zoom-adaptive)
│   ├── Label per sea/ocean (italic, spaced, zoom-adaptive)
│   └── Label per province (at high zoom)
├── CanvasLayer (Layer 5) - UI Overlays
│   ├── Selection highlight
│   ├── Frontlines
│   ├── Movement arrows
│   └── Tooltips
└── Camera2D (with smooth zoom/pan)
```

### 3.2 Key Visual Features

#### Ocean Rendering
```csharp
// Layer -2: Ocean bathymetry with gradient
// Base: OB_HR.tif (bathymetry) colored by depth
// Overlay: Dark navy gradient (poles darker, equator lighter)
// Vignette: Subtle radial darkening at edges
// Animated: Very subtle caustics/shimmer (optional)
```

#### Terrain Rendering (The Core HOI4 Look)
```csharp
// Layer -1: 
// 1. Base color: NE1_HR_LC_SR_W_DR (cross-blended hypsometric + shaded relief)
// 2. Overlay: SR_HR.tif (grayscale shaded relief) with Multiply blend, 30% opacity
// 3. This gives: Mountains pop, valleys recede, forests darker, deserts lighter
// 4. Country fills go ON TOP at 55% alpha so terrain shows through
```

#### Country Rendering
```csharp
// Layer 0:
// - Fill: Country color at 55% alpha (CountryTerrainAlpha)
// - Border: Dark line (0E141B), width = 1.1 / zoom (clamped 0.35-3px)
// - Coastal glow: Inner turquoise line (0.3, 0.8, 0.9, 0.2), width = border * 3.2
// - Player country: Gold border (FFD700) at 55% alpha
// - Selected: Gold pulsing border (2.5px base, animated)
// - Hover: Lightened fill (+25%)
```

#### LOD System
| Zoom Level | Visible Features | Label Detail |
|------------|-----------------|--------------|
| 0.1 - 0.5 (Strategic) | Country fills, borders, coastlines | Major countries only, seas/oceans |
| 0.5 - 1.5 (Operational) | + Province borders, major rivers | All countries, major seas, capitals |
| 1.5 - 3.0 (Tactical) | + Minor rivers, urban areas | All cities > 100k, province names |
| 3.0+ (Detail) | + All cities, terrain detail | All labels, tooltips on hover |

---

## Phase 4: Implementation Steps

### Step 1: Create Asset Loader
**File:** `Scripts/MapAssets.cs`
- Load all textures at startup
- Generate mipmaps
- Manage memory (dispose unused)

### Step 2: Create Label System
**File:** `Scripts/MapLabelSystem.cs`
- Curved labels along country shapes
- Collision avoidance (simulated annealing or force-directed)
- Zoom-adaptive visibility
- Sea labels on curved paths

### Step 3: Create City System
**File:** `Scripts/MapCitySystem.cs`
- City markers (capital=star, major=circle, port=anchor)
- Population-based scaling
- Label collision with country labels
- Zoom-adaptive visibility

### Step 4: Create WorldMapV4
**File:** `Scripts/WorldMapV4.cs`
- Main entry point
- Composes all layers
- Handles input (pan/zoom/select)
- Integrates with existing game systems (GameManager, ArmySystem, etc.)

### Step 5: Integration
- Add `--map-v4` flag to GameManager
- Modify Game.cs to instantiate WorldMapV4
- Ensure all existing signals work (CountrySelected, ProvinceSelectionChanged, etc.)

---

## Phase 5: Performance Optimization

### 5.1 Texture Management
- Use Godot's built-in mipmaps (import with "Mipmaps" enabled)
- Large textures: Split into tiles if > 4096px
- Use `TextureRect` with `ExpandMode = FitWidthKeepAspect` for full-screen

### 5.2 Draw Call Reduction
- Batch country fills: Single `Polygon2D` per country (already done)
- Borders: Use `Line2D` per country (already done)
- Rivers: Single `Line2D` with many points
- Cities: Use `MultiMesh` with `MultiMeshInstance2D` for thousands of markers

### 5.3 LOD Culling
```csharp
// In _Process():
float zoom = _camera.Zoom.X;
bool showProvinces = zoom > 0.5f;
bool showCities = zoom > 1.5f;
bool showCityNames = zoom > 2.5f;
bool showProvinceNames = zoom > 3.0f;

// Toggle layer visibility
_provinceLayer.Visible = showProvinces;
_cityMarkerLayer.Visible = showCities;
_cityLabelLayer.Visible = showCityNames;
```

### 5.4 Spatial Index for Hit Testing
- Already implemented: Province grid (128px cells)
- Extend to cities: QuadTree or grid for city hit testing

---

## Phase 6: Visual Polish

### 6.1 Era/Time Effects (Already Exist)
- Era tone (1936-1945 color shift)
- Day/night terminator (optional toggle)
- Vignette (subtle)

### 6.2 New Effects
- **Coastal waves**: Subtle animated lines at coast (shader)
- **Terrain lighting**: Slight directional light from top-left (shader on terrain layer)
- **Map paper texture**: Very subtle paper grain overlay (optional, toggleable)

### 6.3 Shader for Terrain Enhancement
```glsl
// terrain_shader.gdshader
shader_type canvas_item;
render_mode blend_mix;

uniform sampler2D terrain_texture : source_color;
uniform sampler2D shaded_relief : source_color;
uniform float shaded_relief_strength : hint_range(0, 1) = 0.3;
uniform float terrain_saturation : hint_range(0, 2) = 1.0;

void fragment() {
    vec4 terrain = texture(terrain_texture, UV);
    vec4 relief = texture(shaded_relief, UV);
    
    // Multiply blend for shaded relief
    vec3 result = terrain.rgb * (1.0 - shaded_relief_strength) + 
                  terrain.rgb * relief.r * shaded_relief_strength;
    
    // Saturation control
    float gray = dot(result, vec3(0.299, 0.587, 0.114));
    result = mix(vec3(gray), result, terrain_saturation);
    
    COLOR = vec4(result, terrain.a);
}
```

---

## Asset Download Checklist

### Manual Download Required (Large Files)
- [ ] `SR_HR.tif` - https://naciscdn.org/naturalearth/10m/raster/SR_HR.tif (~150 MB)
- [ ] `NE1_HR_LC_SR_W_DR.zip` - https://naciscdn.org/naturalearth/10m/raster/NE1_HR_LC_SR_W_DR.zip (~400 MB)
- [ ] `NE2_HR_LC_SR_W_DR.zip` - https://naciscdn.org/naturalearth/10m/raster/NE2_HR_LC_SR_W_DR.zip (~400 MB)
- [ ] `OB_HR.tif` - https://naciscdn.org/naturalearth/10m/raster/OB_HR.tif (~200 MB)

### Auto-Downloadable (Script)
- [ ] `ne_10m_populated_places.zip` - https://naciscdn.org/naturalearth/10m/cultural/ne_10m_populated_places.zip
- [ ] `ne_10m_urban_areas.zip` - https://naciscdn.org/naturalearth/10m/cultural/ne_10m_urban_areas.zip
- [ ] `ne_10m_rivers_lake_centerlines.zip` - https://naciscdn.org/naturalearth/10m/physical/ne_10m_rivers_lake_centerlines.zip
- [ ] `ne_10m_coastline.zip` - https://naciscdn.org/naturalearth/10m/physical/ne_10m_coastline.zip
- [ ] `cities15000.zip` - http://download.geonames.org/export/dump/cities15000.zip
- [ ] Fonts: Cinzel, Oswald, Cormorant from Google Fonts

---

## CREDITS.md Entries (To Be Added)

```markdown
## Map Assets

### Natural Earth Data (Public Domain)
- **Natural Earth I with Shaded Relief, Water, and Drainages (1:10m)**
  - Source: https://www.naturalearthdata.com/downloads/10m-raster-data/
  - License: Public Domain
  - Files: NE1_HR_LC_SR_W_DR.tif, SR_HR.tif
  
- **Natural Earth II (1:10m)**
  - Source: https://www.naturalearthdata.com/downloads/10m-raster-data/
  - License: Public Domain
  - File: NE2_HR_LC_SR_W_DR.tif
  
- **Ocean Bathymetry (CleanTOPO2, 1:10m)**
  - Source: https://www.naturalearthdata.com/downloads/10m-raster-data/
  - License: Public Domain
  - File: OB_HR.tif
  
- **Populated Places (1:10m)**
  - Source: https://www.naturalearthdata.com/downloads/10m-cultural-vectors/
  - License: Public Domain
  - File: ne_10m_populated_places.shp
  
- **Urban Areas (1:10m)**
  - Source: https://www.naturalearthdata.com/downloads/10m-cultural-vectors/
  - License: Public Domain
  - File: ne_10m_urban_areas.shp
  
- **Rivers & Lake Centerlines (1:10m)**
  - Source: https://www.naturalearthdata.com/downloads/10m-physical-vectors/
  - License: Public Domain
  - File: ne_10m_rivers_lake_centerlines.shp
  
- **Coastline (1:10m)**
  - Source: https://www.naturalearthdata.com/downloads/10m-physical-vectors/
  - License: Public Domain
  - File: ne_10m_coastline.shp
  
- **Admin-0 Countries & Admin-1 States (1:10m)**
  - Source: https://www.naturalearthdata.com/downloads/10m-cultural-vectors/
  - License: Public Domain
  - Already integrated via countries.json, regions.json

### GeoNames (CC BY 4.0)
- **Cities 15000** (cities with population > 15,000)
  - Source: http://download.geonames.org/export/dump/cities15000.zip
  - License: Creative Commons Attribution 4.0 International
  - Attribution: "GeoNames data by GeoNames.org"

### Fonts (OFL - Open Font License)
- **Cinzel** by Natanael Gama
  - Source: https://fonts.google.com/specimen/Cinzel
  - License: SIL Open Font License 1.1
  - Use: Country labels
  
- **Oswald** by Vernon Adams
  - Source: https://fonts.google.com/specimen/Oswald
  - License: SIL Open Font License 1.1
  - Use: Province/UI labels
  
- **Cormorant** by Christian Thalmann
  - Source: https://fonts.google.com/specimen/Cormorant
  - License: SIL Open Font License 1.1
  - Use: Sea/Ocean labels
```

---

## Implementation Priority Order

1. **Week 1**: Asset download + processing pipeline
2. **Week 2**: WorldMapV4 core renderer (terrain + ocean + country fills)
3. **Week 3**: Label system (curved country labels, sea labels)
4. **Week 4**: City system (markers, labels, collision avoidance)
5. **Week 5**: LOD system + performance optimization
6. **Week 6**: Integration + polish + testing

---

## Testing Checklist

- [ ] All assets load without errors
- [ ] Terrain renders correctly (mountains, forests, deserts visible)
- [ ] Ocean renders with bathymetry gradient
- [ ] Country fills at 55% alpha show terrain underneath
- [ ] Borders scale correctly with zoom
- [ ] Coastal glow visible
- [ ] Labels curve along country shapes
- [ ] No label overlaps (collision avoidance works)
- [ ] Cities appear at correct zoom levels
- [ ] City markers: capital=star, major=circle, port=anchor
- [ ] LOD transitions smooth
- [ ] FPS > 60 at all zoom levels
- [ ] Memory usage < 500 MB
- [ ] Save/Load works (A==B)
- [ ] All existing signals work (CountrySelected, ProvinceSelectionChanged, etc.)
- [ ] Map modes (Political, Terrain, Supply, etc.) work correctly
- [ ] Existing gameplay unaffected (armies, diplomacy, etc.)

---

## Notes

- **Do NOT modify existing WorldMap.cs or WorldMapV2.cs** - create WorldMapV4.cs as new file
- **Keep all existing signals identical** for drop-in replacement
- **Use `--map-v4` flag** in GameManager for enabling
- **Test with both scenarios** (historical_1936, modern_world)
- **Document all asset sources** in CREDITS.md