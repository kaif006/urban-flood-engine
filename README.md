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
