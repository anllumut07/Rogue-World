# Map Data Sources

This document records all data sources used for the Rogue World map, their licenses, download URLs, and retrieval dates.

## Natural Earth Data (Public Domain)

All Natural Earth vector and raster data are in the **public domain** (CC0 equivalent). No permission needed for any use including commercial. Attribution encouraged but not required.

**License Page:** https://www.naturalearthdata.com/about/terms-of-use/
**Retrieved:** 2026-11-18

### 1. Admin-0 Countries (1:10m scale)
- **Description:** 258 country polygons with ISO/FIPS codes, sovereignty, disputed boundaries
- **Version:** 5.1.1 (2022-05-08)
- **Official Page:** https://www.naturalearthdata.com/downloads/10m-cultural-vectors/10m-admin-0-countries/
- **Download URL:** https://naciscdn.org/naturalearth/10m/cultural/ne_10m_admin_0_countries.zip
- **File Size:** 4.93 MB (zip), 9.7 MB (uncompressed shapefile)
- **Format:** ESRI Shapefile (WGS84, EPSG:4326)
- **Features:** 258 countries
- **Key Fields:** `ADM0_A3` (ISO3), `NAME`, `SOVEREIGNT`, `TYPE`, `ECONOMY`, `INCOME_GRP`, `WIKIDATAID`
- **Retrieved:** 2026-11-18

### 2. Admin-1 States/Provinces (1:10m scale)
- **Description:** 4,500+ first-order administrative divisions (states, provinces, regions)
- **Version:** 5.1.1 (2022-05-08) — **Note: Still in beta per Natural Earth**
- **Official Page:** https://www.naturalearthdata.com/downloads/10m-cultural-vectors/10m-admin-1-states-provinces/
- **Download URL:** https://naciscdn.org/naturalearth/10m/cultural/ne_10m_admin_1_states_provinces.zip
- **File Size:** 14.9 MB (zip), 36.2 MB (uncompressed shapefile)
- **Format:** ESRI Shapefile (WGS84, EPSG:4326)
- **Features:** ~4,500 admin-1 units
- **Key Fields:** `ADM0_A3` (parent country ISO3), `NAME`, `NAME_ALT`, `TYPE_EN`, `ISO_3166_2`, `HASC`, `FIPS`, `WIKIDATAID`
- **Retrieved:** 2026-11-18
- **Known Issues (per Natural Earth):** Beta status; boundaries in Brazil, North Africa, Central Asia may lack detail on straight segments; some units missing names; France has both regions and departments as admin-1

### 3. Populated Places (1:10m scale)
- **Description:** ~7,000+ city/town points with population estimates, capitals, scale ranks
- **Version:** 5.1.2 (2022-05-13)
- **Official Page:** https://www.naturalearthdata.com/downloads/10m-cultural-vectors/10m-populated-places/
- **Download URL:** https://naciscdn.org/naturalearth/10m/cultural/ne_10m_populated_places.zip
- **File Size:** 2.81 MB (zip), 48.6 MB (uncompressed shapefile)
- **Format:** ESRI Shapefile (WGS84, EPSG:4326)
- **Features:** ~7,000 populated places
- **Key Fields:** `NAME`, `NAMEASCII`, `ADM0_A3`, `ADM1`, `POP_MAX`, `POP_MIN`, `POP_RANK`, `FEATURECLA` (Admin-0 capital, Admin-1 capital, etc.), `LATITUDE`, `LONGITUDE`, `WIKIDATAID`
- **Population Source:** LandScan (Oak Ridge National Laboratory) derived estimates for 90% of cities
- **Retrieved:** 2026-11-18

### 4. Shaded Relief (1:10m scale) — Grayscale, Land Only
- **Description:** Grayscale shaded relief derived from SRTM+ elevation, clipped to coastline
- **Version:** 3.2.0 (2012)
- **Official Page:** https://www.naturalearthdata.com/downloads/10m-raster-data/10m-shaded-relief/
- **Download URLs:**
  - Large (21,600 × 10,800 px): https://naciscdn.org/naturalearth/10m/raster/SR_HR.zip (42.31 MB)
  - Medium (16,200 × 8,100 px): https://naciscdn.org/naturalearth/10m/raster/SR_LR.zip (24.01 MB)
- **File Sizes:** 
  - SR_HR.tif: 233 MB (GeoTIFF, grayscale, 21,600 × 10,800)
  - SR_LR.tif: 131 MB (GeoTIFF, grayscale, 16,200 × 8,100)
- **Format:** GeoTIFF with TFW world file (WGS84, EPSG:4326)
- **Projection:** Geographic (Plate Carrée), registers precisely with 10m vector data
- **Retrieved:** 2026-11-18

---

## Attribution (as recommended by Natural Earth)

**Short:** Made with Natural Earth.

**Long:** Made with Natural Earth. Free vector and raster map data @ naturalearthdata.com.

---

## Disputed Boundaries Policy

Natural Earth shows **de facto** boundaries by default (who controls the territory on the ground) rather than de jure (legal claims). Optional point-of-view (POV) variants are available for 30+ countries.

**Policy Page:** https://www.naturalearthdata.com/about/disputed-boundaries-policy/

This project uses the default de facto boundaries without POV variants.

---

## Country ID Mapping (Game ISO3 → Natural Earth ISO3)

The game uses 179 country entries (including some non-standard codes). Natural Earth v5.1.1 has 258 countries. This table maps game IDs to NE IDs for data linking.

| Game ID | Game Name | NE ISO3 | NE Name | Notes |
|---------|-----------|---------|---------|-------|
| AFG | Afghanistan | AFG | Afghanistan | Direct match |
| ... | (173 direct matches) | ... | ... | Direct match |
| **-99** | Northern Cyprus | **CYP** | Cyprus | NE: part of Cyprus; disputed area in separate theme |
| **-99** | Somaliland | **SOM** | Somalia | NE: part of Somalia; self-declared, not widely recognized |
| **CS-KM** | Kosovo | **KOS** | Kosovo | NE: KOS (recognized by ~100 countries) |
| **ESH** | Western Sahara | **SAH** | W. Sahara | NE: SAH (disputed; de facto Morocco control) |
| **GUF** | French Guiana | **FRA** | France | NE: overseas region of France; use map units for separate |
| **PSE** | West Bank / Palestine | **PSX** | Palestine | NE: PSX (Palestine) |
| **SSD** | South Sudan | **SDS** | S. Sudan | NE: SDS |
| **ATF** | French Southern Territories | **ATF** | Fr. S. Antarctic Lands | NE: ATF (no populated places) |

**Total mapped:** 179 / 179 (6 required manual mapping, 173 direct ISO3 matches)

**NE countries NOT in game (85):** Mostly small island territories, overseas regions, and disputed areas not in game scope (e.g., ABW Aruba, AIA Anguilla, AND Andorra, ATC Ashmore and Cartier Islands, etc.). Full list in `country_mapping_report.json`.

**Proposed fixes for game countries without NE data:**
1. **Northern Cyprus (-99)** → Map to CYP; add disputed area overlay from NE admin-0 breakaway theme
2. **Somaliland (-99)** → Map to SOM; add note in game data
3. **Kosovo (CS-KM)** → Rename to KOS in game data (standard ISO3)
4. **Western Sahara (ESH)** → Rename to SAH in game data (standard ISO3)
5. **French Guiana (GUF)** → Include as region of FRA; use NE admin-1 for visual separation
6. **Palestine (PSE)** → Rename to PSX in game data (standard ISO3)
7. **South Sudan (SSD)** → Rename to SDS in game data (standard ISO3)

---

## Data Not Used (and why)

| Dataset | Reason |
|---------|--------|
| GADM | Non-commercial license (CC BY-NC 4.0) — not permitted for this project |
| OpenStreetMap | ODbL license (share-alike) — requires approval per project rules |
| Natural Earth Admin-2 (Counties) | Too detailed for current scope; admin-1 sufficient for visual regions layer |
| Natural Earth Cross-Blended Hypsometric Tints | Color relief; we use grayscale shaded relief + programmatic biome coloring |
| NASA SRTM / DEM | Natural Earth shaded relief already derived from SRTM+ |