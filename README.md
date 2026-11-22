# urban-flood-engine
A 3D topological urban flood simulator for Karachi, built with ASP.NET Core, PostGIS, and Unity.

## Unity project

Open `unity/` with Unity Editor `6000.3.13f1`.

### Load live terrain

Start PostGIS with `docker compose up -d db`. The database must already contain the `gulshan_jauhar` DEM.

From the repository root, set `ConnectionStrings__FloodDatabase` using your local `.env` values. Use the configured host port if it differs from `5432`.

```bash
export ConnectionStrings__FloodDatabase='Host=127.0.0.1;Port=5432;Database=YOUR_DB;Username=YOUR_USER;Password=YOUR_PASSWORD'
dotnet run --project backend/src/FloodEngine.Api -- --urls http://127.0.0.1:5080
```

Open `Assets/Scenes/SampleScene.unity` and press Play. On the **Terrain** GameObject, set **Api Base Url** if the API runs on a different address, or enable **Use Mock Fixture** for offline work. The local HTTP URL is for Editor and development builds. Use an HTTPS API URL for a release build.

### Check flow and sector overlays

After terrain loads, the overlay panel shows the contributing-cell legend and sector controls. Select **Gulshan-e-Iqbal** or **Gulistan-e-Jauhar** to dim the cells outside that sector. Disable **Show overlay** to see the terrain material again. The colour scale is logarithmic and stays fixed when switching sectors.

In mock mode, the southeast sample is the highest at 5 contributing cells and the northeast sample stays blank. The mock sector masks demonstrate selection on the small fixture. Live mode reads the two registered sector masks from the API.

### Inspect the Gulshan Chowrangi pilot

In live mode, click **Focus Gulshan Chowrangi** in the lower-left panel. Use the **Roads** and **Buildings** toggles to compare the geometry with the terrain, then select **Return to shared view**. The focus view highlights the original pilot block within the wider city layer. The shared DEM and D8 counts are the simulation inputs and outputs.

The bundled vectors come from [OpenStreetMap contributors](https://www.openstreetmap.org/copyright) under [ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/). The [source extract](https://api.openstreetmap.org/api/0.6/map?bbox=67.093,24.921,67.100,24.927) was retrieved on 22 November 2026. It includes 88 road ways and six closed-way building footprints in the pilot. Road widths use mapped values or class defaults. Buildings use mapped heights, 3 m per mapped level, or a 9 m default. Bridge and tunnel ways are omitted from the ground-draped road layer.

To regenerate the bundled geometry from a saved XML extract, use Python with rasterio and Shapely 2.1 or newer.

```bash
python unity/tools/prepare_osm_pilot.py /path/to/pilot.osm \
  unity/Assets/FloodTwin/Resources/FloodTwin/CityPilot.json
```

### Run the desktop demo

Build **Linux (x86_64)** from Unity Build Profiles with **Development Build** selected for the local HTTP API. Keep the executable and its companion files together. Replace the example executable path with your build output.

```bash
./KarachiFloodTwin.x86_64 --mock
./KarachiFloodTwin.x86_64 --api-url http://127.0.0.1:5080
```

Use the middle mouse button to pan, the right mouse button to orbit around the terrain under the view centre, and the wheel to zoom. Press **R** to reset the full-domain view. Left-click the terrain to inspect its row, column, elevation, D8 direction, and contributing-cell count. The road and building layers are shown in live mode. If the API is offline, start it and select **Retry**.

The expanded city data uses the [Geofabrik Pakistan OSM extract](https://download.geofabrik.de/asia/pakistan.html), snapshot `2026-11-21T21:20:51Z`. It is clipped to the shared simulation boundary and packaged as 56 compressed 2 km batches covering 19,621 road ways and 41,190 building parts. The input population follows mapped coverage. Building geometry comes from closed ways, and bridge and tunnel ways are omitted from the ground-draped road layer. In Unity, geometry outside the valid DEM is skipped.

To regenerate the batches, install osmium, rasterio, and Shapely 2.1 or newer and run the following against the downloaded PBF. Use an empty output directory when rebuilding from a different extract, then replace the bundled batches as a set.

```bash
python unity/tools/prepare_osm_coverage.py /path/to/pakistan-latest.osm.pbf \
  unity/Assets/FloodTwin/Resources/FloodTwin/CityCoverage
```
