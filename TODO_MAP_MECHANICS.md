# HoI4 Map Mechanics Implementation TODO
Based on analysis of HoI4 (Clausewitz Engine) data files

---

## PHASE 1: Core Geographic Systems (High Priority)

### 1. StrategicRegionSystem ❌ NOT STARTED
**Purpose**: Group provinces into macro regions for weather simulation and AI strategic planning
**Data Source**: `map/strategicregions/*.txt` (298 regions, ~59 provinces each)
**Files to Create**:
- `Scripts/StrategicRegionSystem.cs` - Core system
- `Data/strategic_regions.json` - Region definitions

**Features**:
- 298 strategic regions covering globe
- Each region: id, name, province list, weather periods (12+ per year)
- Weather period: date range, temp min/max, phenomenon probabilities
- Integration: WeatherSystem queries region for daily weather tick
- Integration: AI uses regions for strategic planning

**Implementation Steps**:
1. Parse HoI4 strategic region files → JSON
2. Create StrategicRegionSystem with region lookup by province
3. Define WeatherPeriod struct with probability weights
4. Add GetRegionForProvince(provinceId) and GetWeatherPeriod(regionId, dayOfYear)

---

### 2. WeatherSystem ❌ NOT STARTED
**Purpose**: Daily probabilistic weather simulation with terrain-modulated probabilities and visual accumulation (mud/snow)
**Data Source**: `common/weather.txt` + strategic region weather periods
**Files to Create**:
- `Scripts/WeatherSystem.cs` - Core engine
- `Data/weather_definitions.json` - Phenomenon definitions

**Features**:
- **Phenomena**: no_phenomenon, rain_light, rain_heavy, snow, blizzard, mud, sandstorm, arctic_water
- **Daily Tick per Strategic Region**:
  - Find current weather period by date
  - Roll temperature: uniform(temp_min, temp_max) + terrain modifier
  - Roll phenomenon: weighted_random(period.probabilities * terrain_modifiers)
  - Update accumulation (mud/snow) per province
  - Apply effects to movement, combat, supply, attrition
- **Terrain Modifiers on Weather** (from HoI4):
  - Mud: mountain -0.3, plains +0.5, marsh +0.9, desert +0.1, jungle +0.2
  - Snow: mountain +0.8, desert -1.0, urban -0.4
  - Sandstorm: desert +0.8, mountain -1.0
  - Temperature: desert +5, mountain -10, hills -3, forest -1, urban +3, supercity +3, jungle +5
- **Accumulation Mechanics**:
  - Mud: rain_light +0.5, rain_heavy +1.8, max 200, dryout at 2-38°C
  - Snow: snowing +1.0, blizzard +5.0, max 80, melt at 0-12°C
  - Frozen mud: disappears below -1°C
- **Temperature Thresholds**: extreme_cold < -12, very_cold -12 to -4, temperate -4 to 20, very_hot 20-28, extreme_hot > 28
- **Duration**: weighted toward lower values (no_phenom 5-12, rain_light 5-20, etc.)

**Implementation Steps**:
1. Define WeatherPhenomenon enum and WeatherState struct
2. Create WeatherSystem with daily tick per strategic region
3. Implement accumulation system (mud/snow per province)
4. Add terrain probability modifiers
5. Integrate with ArmySystem (movement), WarSystem (combat), SupplySystem (flow), Attrition
6. Add visual blending support for map rendering

---

### 3. ProvinceAdjacencySystem ❌ NOT STARTED
**Purpose**: Province-level connection graph for pathfinding, movement, and naval operations
**Data Source**: `map/adjacencies.csv` (669 connections)
**Files to Create**:
- `Scripts/ProvinceAdjacencySystem.cs` - Core system
- `Data/province_adjacencies.json` - Adjacency list

**Features**:
- **Adjacency Types**:
  - `sea` (228): Naval movement between sea provinces
  - `impassable` (432): Blocks land movement (mountains, borders, jungles)
  - `strait/canal` (33): Relationship-gated special connections
- **Adjacency Graph**: provinceId → List<(neighborId, type, ruleName?)>
- **Pathfinding Integration**: A* edge costs modified by adjacency type
- **Impassable Borders**: Major mountain ranges (Himalayas, Andes, Alps, Pyrenees, Caucasus, etc.)

**Implementation Steps**:
1. Parse HoI4 adjacencies.csv → JSON
2. Build adjacency dictionary with types
3. Add GetNeighbors(provinceId, AdjacencyType?) 
4. Add IsPassable(fromProv, toProv, unitType)
5. Integrate with ArmySystem pathfinding (A* edge cost)

---

### 4. StraitCanalSystem ❌ NOT STARTED
**Purpose**: 33 special strait/canal rules with relationship-gated passage permissions
**Data Source**: `map/adjacency_rules.txt` (33 rules)
**Files to Create**:
- `Scripts/StraitCanalSystem.cs` - Core system
- `Data/strait_canal_rules.json` - Rule definitions

**Features**:
- **Rule Structure**: name, required_provinces, permissions by relationship
- **Relationship Categories**: contested, enemy, friend, neutral
- **Permissions**: army, navy, submarine, trade (yes/no each)
- **Dynamic Blocking**: is_disabled conditions (flags, modifiers, country-specific)
- **Major Canals**: SUEZ (6), PANAMA (3), KIEL (4), BOSPHORUS (2), DARDANELLES (2), GIBRALTAR (4), MALACCA (9), HORMUZ (3), etc.

**Implementation Steps**:
1. Parse HoI4 adjacency_rules.txt → JSON
2. Create StraitCanalRule class with permission matrix
3. Implement CanPassThrough(rule, country, adjacency) logic
4. Add dynamic blocking via game flags/modifiers
5. Integrate with ProvinceAdjacencySystem and NavalInvasionSystem

---

## PHASE 2: Logistics & Infrastructure (Medium Priority)

### 5. RailwaySystem ❌ NOT STARTED
**Purpose**: Railway network with gauge types affecting supply throughput
**Data Source**: `map/railways.txt` (1,284 lines)
**Files to Create**:
- `Scripts/RailwaySystem.cs` - Core system
- `Data/railways.json` - Railway lines

**Features**:
- **Gauge Types**: Standard (1.0x, 1,226 lines), Broad/Russian (0.8x, 50 lines), Narrow (0.6x, 8 lines)
- **Railway Lines**: owner_level, num_segments, province chain
- **Supply Flow Integration**: gauge_mult * terrain_penalty * rail_efficiency
- **Visual**: Railway rendering on map

---

### 6. SupplyNodeSystem ❌ NOT STARTED
**Purpose**: 895 supply nodes with 3 levels for logistics network
**Data Source**: `map/supply_nodes.txt` + `map/supplyareas/`
**Files to Create**:
- `Scripts/SupplyNodeSystem.cs` - Core system
- `Data/supply_nodes.json` - Node definitions

**Features**:
- **Levels**: Level 1 (basic, all 895), Level 2 (regional hubs), Level 3 (major hubs/capitals/ports)
- **Supply Areas**: Per strategic region, controls flow, railway connections, port capacity
- **Flow Algorithm**: Level 3→2→1 distribution with terrain/gauge penalties

---

### 7. BuildingSystem ❌ NOT STARTED
**Purpose**: 15 building types with 3D placement, terrain limits, levels
**Data Source**: `map/buildings.txt`
**Files to Create**:
- `Scripts/BuildingSystem.cs` - Core system
- `Data/buildings.json` - Building definitions

**Features**:
- **Types**: arms_factory, industrial_complex, air_base, bunker, coastal_bunker, dockyard, anti_air_building, radar_station, nuclear_reactor, synthetic_refinery, rocket_site, etc.
- **Placement**: province_id, 3D coords (x,y,z), rotation, level
- **Terrain Limits**: Max building level per terrain (e.g., mountain: bunker level 1, urban: level 3)
- **Visual**: 3D models on map at coordinates

---

### 8. MapResourceSystem ❌ NOT STARTED
**Purpose**: 6 strategic resources distributed on map
**Data Source**: `common/resources/00_resources.txt` + `map/resources/`
**Files to Create**:
- `Scripts/MapResourceSystem.cs` - Core system
- `Data/map_resources.json` - Resource locations

**Features**:
- **Resources**: oil, aluminium, rubber, tungsten, steel, chromium
- **Costs**: CIC 0.125, convoy 0.1 each
- **Distribution**: Per province resource amounts
- **Integration**: EconomySystem, EquipmentSystem, TradeSystem

---

## PHASE 3: Visual & Polish (Lower Priority)

### 9. MapVisualSystem ❌ NOT STARTED
**Purpose**: 7 bitmap layers + texture atlases + weather shaders
**Data Source**: `map/*.bmp`, `map/terrain/*.dds`
**Files to Create**:
- `Scripts/MapVisualSystem.cs` - Rendering system
- `Shaders/weather_shader.gdshader` - Mud/snow/ice blending

**Bitmap Layers** (5632×2048):
- provinces.bmp (RGB→ID lookup) - 34.6 MB
- terrain.bmp (palette index→terrain) - 11.5 MB
- heightmap.bmp (elevation) - 11.5 MB
- rivers.bmp (river network) - 11.5 MB
- trees.bmp (forest density) - 2.9 MB
- world_normal.bmp (lighting) - 8.7 MB
- cities.bmp (city/VP locations) - 11.5 MB

**Terrain Textures** (DDS):
- atlas0/1/2.dds + normals
- mud_diffuse/normal.dds, snow_normal_rgb_diffuse_a.dds
- colormap_*.dds (city lights, water)
- fow_noise_*.dds (fog of war)
- ice_diffuse/noise.dds, underwater_terrain.dds
- border_*.dds (country/state/province/sea)
- reflection.dds, strait.dds

**Shader Features**:
- Mud/snow/ice texture blending based on accumulation
- Seasonal tree color variation
- Water reflection + ice
- Fog of war noise
- Border rendering at multiple levels

---

## Implementation Order Recommendation

```
Week 1: StrategicRegionSystem + WeatherSystem (core weather loop)
Week 2: ProvinceAdjacencySystem + StraitCanalSystem (movement/naval)
Week 3: RailwaySystem + SupplyNodeSystem (logistics)
Week 4: BuildingSystem + MapResourceSystem (infrastructure)
Week 5: MapVisualSystem + Weather Shaders (visuals)
Week 6: Integration testing, AI updates, performance optimization
```

---

## Key Integration Points

| System | Integrates With |
|--------|----------------|
| WeatherSystem | ArmySystem (movement/attrition), WarSystem (combat), SupplySystem (flow), MapVisualSystem (shaders) |
| ProvinceAdjacencySystem | ArmySystem (pathfinding), NavalInvasionSystem, StraitCanalSystem |
| StraitCanalSystem | ProvinceAdjacencySystem, NavalInvasionSystem, DiplomacySystem (relationships) |
| RailwaySystem | SupplyNodeSystem, SupplySystem |
| SupplyNodeSystem | SupplySystem, RailwaySystem |
| BuildingSystem | EconomySystem, EquipmentSystem, TerrainSystem (limits) |
| MapResourceSystem | EconomySystem, EquipmentSystem, TradeSystem |

---

## Determinism Requirements (Critical for Rogue World)

- All random rolls: `DeterministicRNG` (xorshift64*)
- Daily tick produces identical state across clients
- Weather uses seeded RNG per strategic region per day
- Terrain sampling from bitmap is deterministic
- Pathfinding tie-breaking is deterministic

---

## Data Pipeline

```
HoI4 Raw Files (.txt/.csv/.bmp/.dds)
        ↓
Python Parsers (parse_hoi4_map.py)
        ↓
JSON Data Files (Data/*.json)
        ↓
Runtime Systems (Initialize() loads JSON)
        ↓
Daily Tick (GameManager → Systems)
```

---

## Next Steps

1. **Create StrategicRegionSystem** - Parse HoI4 strategic regions, define weather periods
2. **Create WeatherSystem** - Implement daily probabilistic weather with terrain modifiers
3. **Create ProvinceAdjacencySystem** - Parse adjacencies.csv, build graph
4. **Create StraitCanalSystem** - Parse adjacency_rules.txt, implement permission logic
5. **Integration** - Wire into ArmySystem, WarSystem, SupplySystem, NavalInvasionSystem