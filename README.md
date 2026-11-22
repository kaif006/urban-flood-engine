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
