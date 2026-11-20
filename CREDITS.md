# Credits & Attributions

## Map Data

### Natural Earth
**Free vector and raster map data @ naturalearthdata.com**

All Natural Earth data is in the public domain. The authors (Tom Patterson and Nathaniel Vaughn Kelso) and all contributors renounce all financial claim to the maps.

**Datasets used:**
- Admin-0 Countries (1:10m, v5.1.1) — 258 country polygons
- Admin-1 States/Provinces (1:10m, v5.1.1) — 4,500+ admin-1 units
- Populated Places (1:10m, v5.1.2) — 7,000+ city points with population (processed via tools/process_cities.py to cities_runtime.json with country validation)
- Shaded Relief Grayscale (1:10m, v3.2.0) — SRTM+-derived hillshade

**License:** Public Domain (CC0 equivalent) — https://www.naturalearthdata.com/about/terms-of-use/

---

## Game Engine & Framework

### Godot Engine
**Godot 4.7.2 (Mono / C# / .NET 8)**
https://godotengine.org/
**License:** MIT

### .NET Runtime
**Microsoft .NET 8.0**
https://dotnet.microsoft.com/
**License:** MIT

---

## Libraries & NuGet Packages

| Package | Version | License | Purpose |
|---------|---------|---------|---------|
| GodotSharp | 4.7.2 | MIT | Godot C# bindings |
| System.Text.Json | 8.0+ | MIT | JSON serialization |
| Newtonsoft.Json | 13.x | MIT | JSON (legacy/compat) |

---

## Development Tools

- **Visual Studio Code** — https://code.visualstudio.com/ (MIT)
- **Git** — https://git-scm.com/ (GPL-2.0)
- **Python 3.x** (map data pipeline) — https://python.org/ (PSF)
- **pyshp / shapefile** — https://pypi.org/project/pyshp/ (MIT)
- **shapely** — https://pypi.org/project/shapely/ (BSD)
- **numpy** — https://numpy.org/ (BSD)

---

## Special Thanks

- **Natural Earth** team (Tom Patterson, Nathaniel Vaughn Kelso, and contributors) for providing exceptional public domain map data
- **NACIS** (North American Cartographic Information Society) for hosting the CDN mirror
- **Godot Engine** contributors for the amazing open-source game engine

---

## Third-Party Data Sources (Referenced in Natural Earth)

Natural Earth incorporates data from the following sources (per their Terms of Use):

- **The Washington Post** — country data (non-exclusive license)
- **European Commission, JRC, IES** — European river/lake data (non-exclusive license)
- **XNR Productions** — North American road data (non-exclusive license)
- **International Mapping Associates, Inc.** — time zone data (non-exclusive license)
- **Wikidata** — multilingual names (CC0 / Public Domain)
- **UN SALB** — admin-1 boundary references
- **Statoids** — administrative division references
- **GeoNames** — gazetteer data
- **LandScan / Oak Ridge National Laboratory** — population estimates for cities

---

## Fonts (to be added)

*Game UI fonts will be documented here when finalized. Currently using Godot default fonts.*

---

## Audio Assets (to be added)

*CC0 / public domain audio assets will be documented here per Assets/audio/README.md specification.*