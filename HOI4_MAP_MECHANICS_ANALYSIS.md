# Hearts of Iron IV — Map Mechanics Analysis

**Source Directory:** `C:\Proje\Hearts of Iron IV No Compromise No Surrender`  
**Engine:** Clausewitz Engine (Paradox Development Studio)  
**Game Version:** Based on files dated 2025-11-20  
**Analysis Date:** 2026-11-20

---

## 1. Province System (`map/definition.csv`)

### Province Definition Format
Each line defines a province with:
```
ID;R;G;B;Type;IsWater;TerrainCategory;Unknown
```

**Example:**
```
2	1;230;81;119;lake;false;desert;7
4	3;114;134;148;land;false;plains;6
4	4;0;0;232;sea;true;ocean;0
```

**Fields:**
- **ID**: Unique province identifier (1-16000+)
- **R;G;B**: RGB color in `provinces.bmp` (used for province lookup/rendering)
- **Type**: `land`, `sea`, `lake`
- **IsWater**: `true`/`false` — affects naval movement, supply
- **TerrainCategory**: Links to `common/terrain/00_terrain.txt` (plains, forest, hills, mountain, desert, urban, jungle, marsh, ocean, etc.)
- **Unknown**: Last column (appears to be padding/version)

### Key Statistics
- **Total provinces**: 16,000+ (based on definition.csv lines)
- **Land provinces**: ~12,000+
- **Sea provinces**: ~4,000+
- **Lake provinces**: Minor count

---

## 2. Terrain System (`common/terrain/00_terrain.txt`)

### Terrain Categories (Land)

| Terrain | Movement Cost | Combat Width | Defence Bonus | Attack Penalty | Supply Penalty | Special |
|---------|---------------|--------------|---------------|----------------|----------------|---------|
| **plains** | 1.0 | 144 / 72 | 0 | 0 | 0 | Base terrain |
| **forest** | 1.5 | 134 / 67 | +0.1 | -0.2 | +0.08 | Truck attrition 0.2 |
| **hills** | 1.5 | 127 / 63 | +0.1 | -0.1 | +0.10 | Truck attrition 0.2 |
| **mountain** | 2.0 | 120 / 60 | +0.2 | -0.2 | +0.12 | Attrition 0.3, Truck attrition 1.0 |
| **urban** | 1.2 | 200 / 66 | +0.3 | -0.2 | -0.20 | Air superiority -0.75 |
| **supercity** | 1.5 | 240 / 80 | +0.5 | -0.35 | -0.10 | Air superiority -0.85 |
| **jungle** | 1.5 | 134 / 67 | 0 | -0.3 | +0.16 | Attrition 0.2, Sickness 1.0, Truck attrition 3.0 |
| **marsh** | 2.0 | 124 / 62 | 0 | -0.3 | +0.16 | Attrition 0.35, Sickness 1.0, Truck attrition 4.0 |
| **desert** | 1.05 | 144 / 72 | 0 | 0 | +0.14 | Attrition 0.15, Sickness 1.0, Truck attrition 0.5 |

### Terrain Categories (Naval)

| Terrain | Min Seazone Dominance | Ship Class Modifiers |
|---------|----------------------|---------------------|
| **ocean** | 386.36 | Corvettes/Patrol boats nerfed (-0.3 to -0.75) |
| **water_fjords** | 128.79 | Large ships nerfed (DD -0.2, BB -0.4, CV -0.4) |
| **water_littoral** | 198.6 | Large ships slightly nerfed (-0.1 to -0.2) |
| **water_shallow_sea** | 257.58 | Big ships minor nerf, submarines nerfed (-0.25) |
| **water_deep_ocean** | 515.15 | Small ships heavily nerfed (-0.5 to -0.75), subs bonus (+0.25 visibility) |

### Graphical Terrain Mapping (`terrain` section)
Maps bitmap color indices (from `terrain.bmp`) to terrain types:
- Color 0, 5, 19 → plains
- Color 1, 4 → forest
- Color 2, 8, 17 → hills
- Color 3, 7, 12 → desert
- Color 6, 10, 11, 13, 15, 16, 18, 20, 27, 31 → mountain
- Color 9 → marsh
- Color 13 → urban (spawn_city = yes)
- Color 14 → lakes
- Color 21, 22 → jungle
- Color 27 (duplicate) → mountain (jungle blend)

---

## 3. Strategic Regions (`map/strategicregions/*.txt`)

### Structure
```txt
strategic_region = {
    id = 1
    name = "STRATEGICREGION_1"
    provinces = { 219 221 253 ... }  # List of province IDs
    weather = {
        period = {
            between = { 0.0 30.0 }        # Day of year range (0-365)
            temperature = { -2.0 7.0 }    # Min/max Celsius
            no_phenomenon = 0.400         # Probability weights
            rain_light = 0.200
            rain_heavy = 0.150
            snow = 0.150
            blizzard = 0.000
            arctic_water = 0.000
            mud = 0.300
            sandstorm = 0.000
            min_snow_level = 0.000
        }
        # 12+ periods per region covering full year
    }
}
```

### Key Features
- **200+ strategic regions** covering entire globe
- Each region groups 50-200 provinces
- **Weather system**: 12+ periods per year with seasonal variation
- Weather affects: movement, combat, supply, attrition, visibility
- Temperature ranges drive snow/mud mechanics

### Weather Probabilities (England example - Region 1)
| Period | Temp Range | Rain Light | Rain Heavy | Snow | Mud |
|--------|------------|------------|------------|------|-----|
| Jan (0.0-30.0) | -2 to 7°C | 20% | 15% | 15% | 30% |
| Apr (0.3-29.3) | 2 to 12°C | 100% | 15% | 0% | 30% |
| Jul (0.6-30.6) | 12 to 22°C | 100% | 15% | 0% | 30% |
| Oct (0.9-30.9) | 2 to 12°C | 100% | 15% | 5% | 30% |
| Dec (0.11-30.11) | -1 to 5°C | 100% | 15% | 10% | 30% |

---

## 4. Weather System (`common/weather.txt`)

### Terrain Modifiers on Weather
| Weather | Terrain Effects |
|---------|----------------|
| **mud** | Mountain -0.3, Plains +0.5, Marsh +0.9, Desert +0.1, Jungle +0.2 |
| **rain_light** | Jungle +0.2 |
| **rain_heavy** | Jungle +0.2 |
| **snow** | Mountain +0.8, Desert -1.0, Urban -0.4 |
| **blizzard** | Mountain +0.8, Desert -1.0, Urban -0.4 |
| **sandstorm** | Desert +0.8, Mountain -1.0 |
| **temperature** | Desert +5, Mountain -10, Hills -3, Forest -1, Urban +3, Supercity +3, Jungle +5 |

### Temperature Thresholds
```txt
weather_extreme_cold = { -100.0 -12.0 }
weather_very_cold = { -12.0 -4.0 }
weather_very_hot = { 20.0 28.0 }
weather_extreme_hot = { 28.0 100.0 }
```

### Mud Mechanics
- **Water gain**: Rain light +0.5, Rain heavy +1.8, Max 200.0, Min 5.0
- **Dryout**: Temperature 2-38°C, multiplier 0.0005-0.4
- **Frozen mud**: Disappears below -1.0°C
- **Visual**: Linked to `flooded` state modifier

### Snow Mechanics
- **Gain**: Snowing +1.0, Blizzard +5.0, Max 80.0
- **Visual min**: 128 (0-255 scale)
- **Melt**: Temperature 0-12°C, multiplier 0.001-0.9
- **Snowing range**: 1°C to -4°C optimal

### Duration (days, weighted toward lower)
- No phenomenon: 5-12
- Rain light: 5-20
- Rain heavy: 2-15
- Snow: 5-20
- Blizzard: 2-15
- Sandstorm: 2-10

### Performance Settings
```txt
provinces_per_update = 584
regions_per_update = 7
init_run_passes = 14
texture_refresh_freq = 2.0
```

---

## 5. Adjacency System (`map/adjacencies.csv` + `map/adjacency_rules.txt`)

### Adjacency Types
| Type | Description |
|------|-------------|
| **sea** | Naval movement between sea provinces |
| **impassable** | Blocks land movement entirely (mountains, jungles, borders) |
| **strait/canal** | Special naval/land connections with rules |

### Adjacency Rules (Canals & Straits)
Each rule defines passage permissions by relationship:

```txt
adjacency_rule = {
    name = "SUEZ_CANAL"
    
    contested = { army=yes navy=yes submarine=no trade=no }
    enemy =     { army=no  navy=no  submarine=no trade=no }
    friend =    { army=yes navy=yes submarine=yes trade=yes }
    neutral =   { army=yes navy=yes submarine=yes trade=yes }
    
    required_provinces = { 12049 1155 4073 9947 1178 }
    is_disabled = { ... }  # Can be blocked by flags/modifiers
    icon = 12049
    offset = { 1 0 -6 }
}
```

### Major Canals/Straits Defined
| Name | Provinces | Key Feature |
|------|-----------|-------------|
| **SUEZ_CANAL** | 5 provinces | Blockable by global/country flags |
| **PANAMA_CANAL** | 2 provinces | Blockable by flags + dynamic modifier |
| **KIEL_CANAL** | 4 provinces | Blockable by modifier on country 37 (Germany) |
| **BOSPHORUS_STRAIT** | 2 provinces | Country-flag blockable |
| **DARDANELLES_STRAIT** | 2 provinces | Country-flag blockable |
| **GIBRALTAR_STRAIT** | 4 provinces | Submarines always pass |
| **DANISH_BELTS_STRAIT** | 9 provinces | Øresund/Storebælt |
| **KERCH_STRAIT** | 2 provinces | Crimea access |
| **TSUGARU_STRAIT** | 3 provinces | Hokkaido-Honshu |
| **SUNDA_STRAIT** | 2 provinces | Sumatra-Java |
| **LOMBOK_STRAIT** | 4 provinces | Bali-Lombok |
| **MALACCA_STRAIT** | 9 provinces | Major trade route |
| **HORMUZ_STRAIT** | 3 provinces | Persian Gulf access |
| **MANDEB_STRAIT** | 2 provinces | Red Sea access |
| **GUARDAFUI_CHANNEL** | 2 provinces | Socotra |

### Impassable Borders
Defined in `adjacencies.csv` with `Type=impassable`:
- **Afghanistan-Pakistan** (mountains/Khyber Pass)
- **Himalayas** (extensive border China-India)
- **Tibetan Plateau** (high altitude)
- **Burmese Jungles** (dense terrain)
- **Altai Mountains** (Russia-Mongolia-China)
- **Northern Canada** (arctic)
- **Darien Gap** (Panama-Colombia)
- **Andes** (Chile-Argentina)
- **Alaska** (coastal mountains)
- **Scandinavia** (Norway-Sweden-Finland borders)
- **Caucasus Mountains**
- **Iran Alborz/Zagros Mountains**
- **Alps** (Switzerland-Italy-Austria)
- **Pyrenees** (France-Spain)
- **Carpathians** (Romania-Ukraine)
- **Balkans** mountain borders
- **US-Canada** northern sections
- **Mexico** mountain ranges

---

## 6. Supply System (`map/supply_nodes.txt`)

### Supply Nodes
- **Format**: `level province_id` (level 1-3, province ID)
- **Count**: 896+ supply nodes defined
- **Levels**: 
  - Level 1: Basic supply points (most common)
  - Level 2: Regional hubs
  - Level 3: Major logistical centers (capitals, major ports)

### Supply Areas (`map/supplyareas/`)
Each strategic region has supply area definitions controlling:
- Supply flow between nodes
- Railway connections
- Port capacity
- Motorization requirements

---

## 7. Railway System (`map/railways.txt`)

### Format
```
owner_level num_segments province1 province2 province3 ...
```

**Example:**
```
1 4 3277 11251 317 6389 
2 9 1136 14876 6172 7102 10032 10128 14917 14890 12007 1117 11930 4067 6189 1063
```

- **Owner level**: 1=standard, 2=broad gauge, 3=narrow gauge (affects throughput)
- **Segments**: Number of connections in this line
- **Provinces**: Connected province chain

### Key Features
- **895+ railway lines** defined
- Connects major industrial centers, ports, supply nodes
- Gauge differences affect logistics (standard vs broad vs narrow)
- Integrates with supply system for land logistics

---

## 8. Buildings (`map/buildings.txt`)

### Format
```
province_id;building_type;x;y;z;rotation;level
```

**Example:**
```
1;air_base;2248.00;15.28;2027.00;1.83;0
2;arms_factory;2690.00;9.57;1799.00;0.71;0
2;industrial_complex;2688.00;9.65;1799.00;4.84;0
2;dockyard;2690.00;9.65;1784.00;-3.14;0
```

### Building Types
| Type | Purpose |
|------|---------|
| **arms_factory** | Military equipment production |
| **industrial_complex** | Civilian/military factory construction |
| **air_base** | Aircraft operations |
| **bunker** | Land fortification |
| **coastal_bunker** | Naval fortification |
| **dockyard** | Ship construction/repair |
| **anti_air_building** | Air defense |
| **radar_station** | Detection range |
| **nuclear_reactor** | Nuclear program |

### Placement
- **3D coordinates** (x, y, z) for map rendering
- **Rotation** for visual orientation
- **Level** (0 = base, higher = upgraded)

---

## 9. Map Visual Assets (`map/` + `map/terrain/`)

### Bitmap Maps (all 5632×2048 = 11,535,412 pixels)
| File | Purpose | Size |
|------|---------|------|
| **provinces.bmp** | Province ID lookup (RGB→ID) | 34.6 MB |
| **terrain.bmp** | Terrain type per pixel (color index) | 11.5 MB |
| **heightmap.bmp** | Elevation data | 11.5 MB |
| **rivers.bmp** | River network | 11.5 MB |
| **trees.bmp** | Forest density | 2.9 MB |
| **world_normal.bmp** | Normal map for lighting | 8.7 MB |
| **cities.bmp** | City/VP locations | 11.5 MB |

### Terrain Textures (`map/terrain/`)
- **atlas0.dds** (13.7 MB) - Main terrain texture atlas
- **atlas1.dds** (1.4 MB) - Secondary textures
- **atlas2.dds** (350 KB) - Detail textures
- **atlas_normal0/1/2.dds** - Normal maps
- **mud_diffuse/normal.dds** - Mud visual states
- **snow_normal_rgb_diffuse_a.dds** - Snow visual
- **colormap_rgb_cityemissivemask_a.dds** - City lights
- **colormap_water_0/1/2.dds** - Water coloring
- **fow_noise_0/1/2.dds** - Fog of war noise
- **ice_diffuse/noise.dds** - Polar ice
- **border_*.dds** - Country/state/province/sea borders
- **reflection.dds** - Water reflection
- **underwater_terrain.dds** - Seabed
- **strait.dds** - Strait visualization

### Tree/Seasonal
- **Tree_season.bmp** - Seasonal tree colors
- **Tree_tint.bmp** - Tree color variation

---

## 10. Resources (`common/resources/00_resources.txt`)

### Strategic Resources
| Resource | Icon | CIC Cost | Convoy Cost |
|----------|------|----------|-------------|
| **oil** | Frame 1 | 0.125 | 0.1 |
| **aluminium** | Frame 2 | 0.125 | 0.1 |
| **rubber** | Frame 3 | 0.125 | 0.1 |
| **tungsten** | Frame 4 | 0.125 | 0.1 |
| **steel** | Frame 5 | 0.125 | 0.1 |
| **chromium** | Frame 6 | 0.125 | 0.1 |

- All resources have identical CIC/convoys cost
- Differentiated by icon and production locations
- Distributed via `map/resources/` (not analyzed here)

---

## 11. Map Modes (`common/map_modes/`)

Directory exists for defining strategic map overlays:
- Political, Terrain, Supply, Weather, Resistance, Compliance
- Construction, Resources, Factories, Airbases, Ports
- Diplomatic, Ideology, Threat, Relations
- Strategic regions, States, Provinces

---

## 12. Key Mechanics Summary for Rogue World Implementation

### For Terrain System
1. **8 land terrain types** with distinct movement/combat/supply effects
2. **5 naval terrain types** with ship-class-specific modifiers
3. **Color-index mapping** from bitmap to terrain type
4. **Buildings max level** per terrain (bunker limits)

### For Weather System
1. **Per-strategic-region** weather definitions
2. **12+ seasonal periods** with probability weights
3. **Temperature-driven** snow/mud transitions
4. **Terrain modifiers** on weather probabilities
5. **Visual accumulation** (mud/snow depth → texture blending)

### For Adjacency/Straits
1. **Relationship-based** passage rules (enemy/friend/neutral/contested)
2. **Required provinces** control (canal ownership)
3. **Dynamic blocking** via flags/modifiers
4. **Impassable borders** for major mountain ranges

### For Supply/Logistics
1. **Node-based** supply (3 levels)
2. **Railway network** with gauge types
3. **Supply areas** per strategic region
4. **Motorization/truck attrition** by terrain

### For Visuals
1. **Multi-layer bitmap** system (province, terrain, height, rivers)
2. **Texture atlases** with normal maps
3. **Seasonal/weather** texture blending (mud, snow, ice)
4. **Border rendering** at multiple levels (country, state, province, sea)

---

## 13. File Encoding Notes
- All `.txt`/`.csv` files: **UTF-8 with BOM** (Paradox standard)
- Bitmap files: **BMP 24-bit** (5632×2048)
- Texture files: **DDS** (DXT compressed, mipmapped)
- Clausewitz script format: `{ key = value }` nested blocks

---

## 14. Integration Points for Rogue World

| HoI4 System | Rogue World Equivalent | Notes |
|-------------|----------------------|-------|
| Province bitmap | Country/territory bitmap | Same lookup technique |
| Terrain categories | Biome/terrain types | Direct mapping possible |
| Strategic regions | Macro regions | Group provinces for AI/weather |
| Weather periods | Seasonal system | Probability-based |
| Adjacency rules | Border crossing rules | Relationship-gated |
| Supply nodes | Logistics hubs | Node + railway network |
| Railway lines | Rail network | Gauge → throughput |
| Building placement | Infrastructure | 3D coords on map |
| Resource distribution | Resource map | 6 strategic resources |

---

**Analysis Complete** — This provides a comprehensive reference for implementing HoI4-style map mechanics in Rogue World.