C4Container
    title Container Diagram for Karachi Digital Twin

    Person(user, "Urban Planner/Researcher", "Visualizes flood zones and drainage paths to support urban planning decisions.")

    System_Boundary(c1, "Karachi Digital Twin") {
        Container(frontend, "Unity 3D Visualizer", "C#, Unity URP", "Dynamically builds 3D terrain mesh and visualizes spatial data and predicted flood patterns.")
        Container(backend, "Simulation Backend API", "ASP.NET Core, Minimal APIs", "Executes core elevation-based water flow calculations and topological routing, serving JSON data.")
        ContainerDb(database, "Spatial Database", "PostgreSQL, PostGIS, Docker", "Stores cleaned DEM and historical rainfall data, queried efficiently by spatial location.")
    }

    System_Ext(srtm, "NASA SRTM", "Provides raw Digital Elevation Model (DEM) data.")
    System_Ext(rainfall, "Rainfall Data Provider", "Supplies historical monsoon rainfall records.")

    Rel(user, frontend, "Views simulations and interacts with", "UI")
    Rel(frontend, backend, "Fetches dynamic accumulation and flow data via", "JSON/REST API")
    Rel(backend, database, "Queries spatial elevation matrices via", "SQL/TCP")
    Rel(backend, srtm, "Acquires elevation data from", "HTTPS")
    Rel(backend, rainfall, "Acquires weather data from", "HTTPS")
