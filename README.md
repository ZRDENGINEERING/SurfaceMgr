# SurfaceMgr

A Civil 3D 2026 plugin (.NET 8 / C#) that builds TIN surfaces from public elevation data. Draw a closed polyline around your area of interest, run a command, and SurfaceMgr finds the data, downloads only what it needs, clips and reprojects it to the drawing's coordinate system, and creates the surface. No manual file handling.

## Commands

| Command | Source | Data | Notes |
|---|---|---|---|
| `SURFTXDEM` | TxGIO (TNRIS) | DEM | Fast lookup via ArcGIS index + public S3 bucket. Mosaics all sub-tiles. |
| `SURFTXLID` | TxGIO (TNRIS) | LiDAR (LAZ) | Ground points only. Still uses the slower page scan (see limitations). |
| `SURFUSGSDEM` | USGS 3DEP (TNM Access API) | DEM | Streams tiles with GDAL `/vsicurl/`. Lists the 2 newest years for you to pick from. |
| `SURFUSGSLID` | USGS 3DEP | LiDAR (LAZ) | Shelved, may time out on large tiles. |
| `SURFUSERLID` | Local file | LAZ | You pick a LAZ you already have. |

LiDAR commands ask for a surface method:

- **Points**: classified ground points (class 2) are written to an ENZ CSV and added as a point file. This is the densest and most faithful surface.
- **Raster**: ground points are gridded to a Float32 GeoTIFF with IDW at a resolution you choose, then added as a DEM file. Useful when you want a lighter surface or need to compare against agency DEMs (which are hydro-flattened).

## How `SURFTXDEM` works

1. Select a closed polyline. Its size is checked against a limit (a polite "try smaller" instead of a 100 MB drawing).
2. The AOI centroid is looked up in the TxGIO LiDAR index, picking the best available collection.
3. The collection ID maps to its tile-name slug through a built-in catalog.
4. The AOI is intersected with the USGS quarter-quad grid to get the quads you need.
5. The public S3 bucket is listed for that collection and only the matching `_dem.zip` files are downloaded.
6. All `.img` sub-tiles in each zip are mosaicked (`gdalbuildvrt`), then warped and cut to the AOI in the drawing's CRS (`gdalwarp -cutline -crop_to_cutline`).
7. A uniquely named TIN surface is created and the GeoTIFF is attached.

Outputs are saved in `SurfaceMgr_Output` next to the `.dwg`. Civil 3D stores file path references, so do not delete this folder.

## Requirements

- Civil 3D 2026 (.NET 8)
- OSGeo4W at `C:\OSGeo4W` with GDAL and PDAL (`gdalwarp`, `gdalbuildvrt`, `pdal`)
- Internet access
- A saved drawing with a coordinate system assigned

## Supported coordinate systems

The drawing's Civil 3D zone code is mapped to an EPSG code. Texas State Plane (NAD83, US feet):

| Zone | EPSG |
|---|---|
| North (NF) | 2275 |
| North Central (NCF) | 2276 |
| Central (CF) | 2277 |
| South Central (SCF) | 2278 |
| South (SF) | 2279 |

The CRS definition is fetched from epsg.io and used with ProjNET to transform the AOI to WGS84 for service queries. Rasters are reprojected by GDAL.

## Build and load

```
dotnet build
```

Each build gets a unique assembly name (`SurfaceMgr_<stamp>`) and is copied to `SurfaceMgr.bundle\Contents\Build_<stamp>\`, with older builds removed. This lets you load a new build without restarting Civil 3D.

Load with `NETLOAD` and pick the DLL in the newest `Build_*` folder.

## Known limitations

- `SURFTXLID` still scans TxGIO pages. It should move to the same S3 + index approach as `SURFTXDEM`, filtering `_lpc.zip` / `_sm-lpc.zip`.
- The collection catalog is a partial snapshot. Some collections (for example Pecos-Dallas) are missing and will not resolve.
- TxGIO LAZ source CRS is hardcoded to EPSG:6344 (NAD83(2011) UTM 15N). It should be read per collection.
- USGS datasets are grouped by title, which can group differently than expected when an AOI spans several acquisitions.
- The final transaction runs after `await`, so the document is locked with `doc.LockDocument()`. Keep any new prompts before the first `await`.
- The AOI size limit (`MaxAcres`, currently 2000) is a placeholder.

## Project layout

```
Commands:   GetTXDEMCommands, GetTXLidCommands, GetUSGSCommands,
            GetUsgsLazCommands, SurfFromLidCommands
Locators:   TxLidarIndexLocator, TxQuadGridLocator, TxgioResource,
            TnrisCollectionCatalog, UsgsTnmLocator
Processing: MergerDEMs (GDAL), LazGroundExtractor (PDAL)
Geometry:   ClipBoundary, ClipBoundaryValidation, CivilCoordSystem,
            CoordTransform, QuadResolver, QuadInfo
Civil 3D:   SurfaceFactory, SurfaceStats, AppStartup
```

## Disclaimer

Provided as-is, without warranty. Always verify generated surfaces against source data before using them for engineering work. SurfaceMgr is not affiliated with or endorsed by Autodesk, TxGIO/TNRIS, or USGS. Autodesk assemblies are referenced from your local Civil 3D install and are not distributed with this project.

## License

MIT. See [LICENSE](LICENSE).