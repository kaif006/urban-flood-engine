```mermaid
C4Component
    title Component Architecture for Karachi Digital Twin

    Person(user, "Urban Planner", "Visualizes flood zones and drainage paths to support urban planning decisions.")

    System_Ext(srtm, "NASA SRTM", "Provides raw Digital Elevation Model (DEM) data.")
    System_Ext(rainfall, "Rainfall Data Provider", "Supplies historical monsoon rainfall records.")

    ContainerDb(db, "Spatial Database Environment", "Docker, Ubuntu Linux, PostgreSQL, PostGIS", "Stores cleaned DEM grid and historical rainfall data for efficient spatial querying.")

    Container_Boundary(backend, "Simulation Backend API") {
        Component(d8, "D8 Algorithm Engine", "C#", "Traverses elevation matrix to accurately compute the directional flow of water based on topological slopes.")
        Component(accum, "Accumulation Logic", "C#", "Performs spatial matrix traversal to simulate water pooling in low-elevation drainage zones.")
        Component(api, "RESTful Routing", "ASP.NET Core, Minimal APIs", "Exposes the processed simulation data to the frontend exclusively in JSON format.")
    }

    Container_Boundary(frontend, "Unity 3D Visualizer") {
        Component(terrain, "Terrain Generation", "C# Scripts", "Imports processed DEM data and dynamically builds the base 3D terrain mesh of targeted sectors.")
        Component(shader, "Visual Simulation", "HLSL, URP", "Maps incoming JSON data directly onto the terrain mesh to visually highlight predicted flood zones.")
    }

    Rel(user, frontend, "Interacts with", "UI")
    
    Rel(db, srtm, "Ingests raw radar data from", "Data Pipeline")
    Rel(db, rainfall, "Ingests historical records from", "Data Pipeline")

    Rel(d8, db, "Queries elevation grids from", "SQL")
    Rel(accum, d8, "Aggregates directional flows from", "Internal")
    Rel(api, accum, "Serves calculated data from", "Internal")

    Rel(terrain, api, "Fetches spatial topology data via", "JSON/HTTPS")
    Rel(shader, api, "Fetches dynamic water flow data via", "JSON/HTTPS")
    Rel(shader, terrain, "Renders water accumulation over", "Engine pipeline")

```
