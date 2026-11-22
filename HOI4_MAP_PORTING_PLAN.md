# HoI4 "No Compromise No Surrender" Map Porting Plan for Rogue World

## Overview
Port the complete HoI4 province-bitmap map system (16,290 provinces, 5632×2048) with all mechanics to Rogue World.

**Source:** `C:\Proje\Hearts of Iron IV No Compromise No Surrender`
**Target:** `C:\Proje\Rogue-World`

---

## Phase 1: Core Province Bitmap System (Foundation)

### 1.1 Province Definition & Lookup System
- [ ] **ProvinceDefinition.cs** - Data class for province (ID, RGB, type, terrain, water)
- [ ] **ProvinceBitmapLoader.cs** - Load `provinces.bmp` (5632×2048, 24-bit), build RGB→ProvinceID hash table (16,290 entries)
- [ ] **ProvinceManager.cs** - Runtime province state (owner, units, buildings, supply, weather)
- [ ] **IProvinceSource Implementation** - Bitmap-based province lookup for mouse/click → Province ID
- [ ] **Coordinate Conversion** - Screen ↔ World ↔ BMP pixel ↔ Lon/Lat (Equirectangular: 360°×180°)

### 1.2 Terrain Bitmap System
- [ ] **TerrainBitmapLoader.cs** - Load `terrain.bmp` (5632×2048, 8-bit), map color indices → terrain categories (via 00_terrain.txt terrain{} block)
- [ ] **TerrainCategory.cs** - All 16 terrain types with gameplay modifiers (movement, combat, supply, attrition)
- [ ] **TerrainSampler.cs** - O(1) terrain lookup per province/pixel

### 1.3 Heightmap & Rivers
- [ ] **HeightmapLoader.cs** - Load `heightmap.bmp` (8-bit), elevation data for visual/modifiers
- [ ] **RiversLoader.cs** - Load `rivers.bmp` (8-bit), river network for rendering/pathfinding
- [ ] **TreesLoader.cs** - Load `trees.bmp` (8-bit), forest density

### 1.4 Province Data Structures (Runtime)
```csharp
ProvinceData {
    id: int
    rgb: Color
    type: ProvinceType { Land, Sea, Lake }
    terrain: TerrainCategory
    terrainPaletteIndex: byte
    owner: string (country tag)
    controller: string
    units: List<Army>
    buildings: List<Building>
    supply: float
    vp: int (victory points)
    isCoastal: bool
    neighbors: List<int> (province IDs)
    strategicRegionId: int
    stateId: int (HoI4 states - optional)
}
```

---

## Phase 2: Strategic Regions & Weather System

### 2.1 Strategic Region Runtime
- [ ] **StrategicRegionRuntime.cs** - 298 regions, province lists, weather periods
- [ ] **WeatherEngine.cs** - Daily weather tick per region (already in ClimateSystem, needs integration)
- [ ] **WeatherPeriod** - 12+ periods/year with probabilities, temperature ranges
- [ ] **Terrain Weather Modifiers** - Apply terrain_modifiers from weather.txt

### 2.2 Weather Accumulation & Visuals
- [ ] Mud accumulation (rain → water level → mud visual)
- [ ] Snow accumulation (snow/blizzard → snow level → visual)
- [ ] Temperature thresholds (extreme cold/hot effects)
- [ ] Texture blending for mud/snow (shader-based)

---

## Phase 3: Adjacency & Movement Graph

### 3.1 Adjacency System
- [ ] **AdjacencyGraph.cs** - 669 connections (228 sea, 432 impassable, 9 empty)
- [ ] **Impassable Borders** - Mountain ranges, jungle borders (Himalayas, Andes, etc.)
- [ ] **Strait/Canal Rules** - 33 rules (Suez, Panama, Bosphorus, Gibraltar, etc.)
- [ ] **Relationship-Gated Passage** - Enemy/Friend/Neutral/Contested permissions

### 3.2 Pathfinding
- [ ] **A* Pathfinding** with terrain costs, supply penalties, threat, weather
- [ ] **Naval Pathfinding** - Sea province graph with strait rules
- [ ] **Movement Time Calculation** - HoI4 formula: terrain_cost / unit_speed * supply_mult

---

## Phase 4: Supply & Logistics Network

### 4.1 Supply Nodes
- [ ] **SupplyNodeRuntime.cs** - 895 nodes (all level 1 in this mod)
- [ ] **SupplyArea.cs** - Per strategic region supply flow definitions

### 4.2 Railway Network
- [ ] **RailwayNetwork.cs** - 1,284 lines (1,226 standard, 50 broad, 8 narrow gauge)
- [ ] **Gauge Throughput** - Standard 1.0x, Broad 0.8x, Narrow 0.6x
- [ ] **Supply Flow Algorithm** - Daily tick with terrain penalties

### 4.3 Integration
- [ ] Terrain supply flow penalties (mountain -12%, etc.)
- [ ] Motorization/truck attrition by terrain
- [ ] Port capacity for naval supply

---

## Phase 5: Buildings & Infrastructure

### 5.1 Building System
- [ ] **BuildingRuntime.cs** - 15 types, 3D placement (x,y,z,rotation), levels
- [ ] **BuildingLimits** - Per terrain (bunker max level: mountain=1, plains=2, urban=3)
- [ ] **Building Effects** - Production, supply, defense, detection

### 5.2 Building Placement
- [ ] 75,676 buildings across 1,214 provinces
- [ ] Visual representation on map

---

## Phase 6: Visual Rendering (HoI4 Style)

### 6.1 Map Layers (Shader-Based)
- [ ] **Province Color Layer** - Country colors via province lookup texture
- [ ] **Terrain Texture Layer** - Atlas textures (atlas0/1/2.dds) with normal maps
- [ ] **Heightmap Shading** - Normal mapping from heightmap.bmp
- [ ] **River Rendering** - RiverSurface textures with flow animation
- [ ] **Tree Rendering** - Tree textures with seasonal variation
- [ ] **Border Rendering** - Country/State/Province/Sea borders (border_*.dds)
- [ ] **Weather Visuals** - Mud/snow texture blending, frost, arctic water
- [ ] **City Lights** - colormap_rgb_cityemissivemask_a.dds
- [ ] **Fog of War** - fow_noise textures

### 6.2 Camera & Projection
- [ ] **Equirectangular Projection** - 360°×180° → 5632×2048
- [ ] **Infinite Horizontal Wrapping** - HOI4 style
- [ ] **Zoom Levels** - LOD system (province → region → strategic)

---

## Phase 7: Gameplay Integration

### 7.1 Country/Province Ownership
- [ ] Country → Province ownership mapping
- [ ] Province transfer on occupation/peace conference
- [ ] Victory Points system

### 7.2 AI Integration
- [ ] Strategic region grouping for AI
- [ ] Supply-aware pathfinding
- [ ] Weather-aware planning

### 7.3 Save/Load
- [ ] Province state serialization (owner, supply, weather, buildings)
- [ ] Deterministic RNG for weather
- [ ] A==B verification

---

## Data Files Already Imported (Data/hoi4_imported/)
✅ buildings.json (75,676 buildings)
✅ province_adjacencies.json (669 adjacencies)
✅ railways.json (1,284 lines)
✅ strait_canal_rules.json (33 rules)
✅ strategic_regions.json (298 regions with weather)
✅ supply_nodes.json (895 nodes)

## Data Files TO IMPORT (Need BMP Processing)
⬜ provinces.bmp → RGB→ID lookup table (16,290 entries)
⬜ terrain.bmp → Color index → TerrainCategory mapping
⬜ heightmap.bmp → Elevation data
⬜ rivers.bmp → River network
⬜ trees.bmp → Forest density
⬜ world_normal.bmp → Normal map
⬜ cities.bmp → City/VP locations

## Texture Assets TO CONVERT (DDS → Godot Compatible)
⬜ atlas0/1/2.dds → Terrain texture atlases
⬜ atlas_normal0/1/2.dds → Normal maps
⬜ mud_diffuse/normal.dds → Mud visuals
⬜ snow_normal_rgb_diffuse_a.dds → Snow visuals
⬜ colormap_water_0/1/2.dds → Water coloring
⬜ border_*.dds → Border textures
⬜ fow_noise_0/1/2.dds → Fog of war
⬜ ice_diffuse/noise.dds → Polar ice
⬜ reflection.dds → Water reflection
⬜ underwater_terrain.dds → Seabed
⬜ strait.dds → Strait visualization

---

## Implementation Order

### Sprint 1: Core Province System (Week 1-2)
1. ProvinceBitmapLoader + RGB→ID hash table
2. ProvinceManager + ProvinceData
3. Coordinate conversions (Screen/World/BMP/LonLat)
4. Basic province rendering (color by owner)
5. Mouse click → Province selection

### Sprint 2: Terrain & Weather (Week 2-3)
1. TerrainBitmapLoader + TerrainCategory system
2. StrategicRegionRuntime + WeatherEngine integration
3. Mud/Snow accumulation + visual blending
4. Temperature system

### Sprint 3: Adjacency & Movement (Week 3-4)
1. AdjacencyGraph + Impassable borders
2. Strait/Canal rules (33 rules)
3. A* Pathfinding with terrain costs
4. Movement time calculation

### Sprint 4: Supply & Railways (Week 4-5)
1. SupplyNodeRuntime + SupplyArea
2. RailwayNetwork with gauge system
3. Daily supply tick with terrain penalties

### Sprint 5: Buildings & Visuals (Week 5-7)
1. BuildingRuntime + placement
2. Terrain texture atlases + shaders
3. Border rendering
4. River/Tree rendering
4. Weather visual blending
5. City lights, FoW

### Sprint 6: Integration & Polish (Week 7-8)
1. Country ownership system
2. AI integration
3. Save/Load with A==B verification
4. Performance optimization (spatial indexing, LOD)
5. Editor testing & screenshots vs HoI4 reference

---

## Technical Requirements

### Godot/C# Specific
- Use `Image` class for BMP loading (CPU-side)
- Upload province lookup texture to GPU (Texture2D)
- Custom shaders for terrain blending, borders, weather
- Spatial grid for O(1) province lookup
- DeterministicRNG for all randomness

### Memory Estimates
- Province lookup texture: 5632×2048×4 bytes = ~44 MB (GPU)
- Terrain texture: 5632×2048×1 byte = ~11 MB (CPU) + GPU
- Heightmap: 5632×2048×1 byte = ~11 MB
- Province data array: 16,290 × ~200 bytes = ~3 MB
- Total: ~70 MB + textures

### Performance Targets
- Province lookup: O(1) via texture/array
- Weather tick: 298 regions × ~60 provinces = ~18k ops/frame
- Supply tick: 895 nodes + railway propagation
- Pathfinding: A* on ~460 connected provinces
- 60 FPS at zoom 1.0 with full map

---

## Verification Checklist

### Functional
- [ ] Click province → shows correct ID, terrain, owner
- [ ] Weather changes daily per strategic region
- [ ] Mud/snow accumulates and renders
- [ ] Units move along adjacency graph with correct time
- [ ] Straits/canals block correctly by relationship
- [ ] Supply flows from nodes through railways
- [ ] Buildings render at correct positions
- [ ] Borders render at country/state/province/sea levels

### Visual (Compare with HoI4 Reference)
- [ ] Terrain colors match HoI4 (plains, forest, mountain, desert, etc.)
- [ ] Heightmap shading matches
- [ ] River rendering matches
- [ ] Border styles match (country thick, state medium, province thin)
- [ ] Water coloring matches (coastal → deep ocean gradient)
- [ ] Mud/snow transitions smooth
- [ ] City lights visible at night
- [ ] Fog of war renders correctly

### Determinism
- [ ] Save → Load → A==B (100 days simulation)
- [ ] Weather deterministic per region
- [ ] Pathfinding deterministic
- [ ] Supply flow deterministic

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| BMP loading slow | Load async, show progress, cache processed data |
| Memory high | Stream textures, use compressed formats (BC7/ASTC) |
| Shader complexity | Start simple, add layers incrementally |
| Performance | Spatial indexing, LOD, frustum culling |
| Coordinate bugs | Comprehensive unit tests for all conversions |
| Determinism | Single RNG source, no floating-point non-determinism |

---

## Next Steps

1. **Start Sprint 1**: Create ProvinceBitmapLoader and ProvinceManager
2. **Test**: Load provinces.bmp, verify 16,290 provinces, test click→province
3. **Integrate**: Replace current GeoJSON country system with province system
4. **Iterate**: Add terrain, weather, adjacency, supply, visuals

---

*Plan created: 2026-11-21*
*Based on analysis of HoI4 "No Compromise No Surrender" mod (16,290 provinces, 298 strategic regions, 33 straits, 895 supply nodes, 1,284 railways, 75k buildings)*