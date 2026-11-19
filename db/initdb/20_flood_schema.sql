-- initdb/20_flood_schema.sql
-- Runs after the image's own 10_postgis.sh. Idempotent.

CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_raster;

CREATE SCHEMA IF NOT EXISTS flood;

-- ---------------------------------------------------------------------------
-- Areas of interest (vulnerable sectors). Stored in WGS84; transformed on demand.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS flood.aoi (
    aoi_id      SERIAL PRIMARY KEY,
    name        TEXT UNIQUE NOT NULL,
    geom        GEOMETRY(Polygon, 4326) NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS aoi_geom_gist ON flood.aoi USING GIST (geom);

-- ---------------------------------------------------------------------------
-- DEM provenance / ingestion audit trail.
-- flood.dem_tiles itself is (re)created by raster2pgsql (-d) in the ingest script.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS flood.dem_source (
    dem_source_id SERIAL PRIMARY KEY,
    aoi_name      TEXT NOT NULL REFERENCES flood.aoi(name) ON DELETE CASCADE,
    source        TEXT NOT NULL,              -- e.g. 'NASA SRTM 1 arc-sec'
    input_files   TEXT[] NOT NULL,
    srid          INT NOT NULL,
    cell_size_m   DOUBLE PRECISION NOT NULL,
    tile_size     TEXT NOT NULL,
    nodata_value  DOUBLE PRECISION NOT NULL,
    ingested_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- Historical monsoon rainfall
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS flood.rainfall_station (
    station_id  TEXT PRIMARY KEY,
    name        TEXT,
    geom        GEOMETRY(Point, 4326) NOT NULL
);
CREATE INDEX IF NOT EXISTS rainfall_station_geom_gist ON flood.rainfall_station USING GIST (geom);

CREATE TABLE IF NOT EXISTS flood.rainfall_observation (
    station_id   TEXT NOT NULL REFERENCES flood.rainfall_station(station_id) ON DELETE CASCADE,
    observed_on  DATE NOT NULL,
    rainfall_mm  NUMERIC(7,2) NOT NULL CHECK (rainfall_mm >= 0),
    source       TEXT,
    PRIMARY KEY (station_id, observed_on)
);
CREATE INDEX IF NOT EXISTS rainfall_obs_date_idx ON flood.rainfall_observation (observed_on);

-- ---------------------------------------------------------------------------
-- Matrix accessor for the C# D8 backend: returns the AOI-clipped DEM as a
-- row-major 2D array plus the georeferencing needed to rebuild coordinates.
--   cell (r,c) centre X = origin_x + (c + 0.5) * cell_size
--   cell (r,c) centre Y = origin_y - (r + 0.5) * cell_size
-- NULL entries in elev == nodata cells.
-- plpgsql (not sql) so the function can be created before flood.dem_tiles exists.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION flood.dem_matrix(p_aoi TEXT)
RETURNS TABLE (
    ncols INT, nrows INT, srid INT,
    origin_x DOUBLE PRECISION, origin_y DOUBLE PRECISION,
    cell_size DOUBLE PRECISION, nodata DOUBLE PRECISION,
    elev DOUBLE PRECISION[][]
)
LANGUAGE plpgsql STABLE AS $$
DECLARE
    v_srid INT;
    v_rast raster;
BEGIN
    SELECT rc.srid INTO v_srid
      FROM raster_columns rc
     WHERE rc.r_table_schema = 'flood'
       AND rc.r_table_name   = 'dem_tiles'
       AND rc.r_raster_column = 'rast';

    -- ST_ConvexHull(...) && geom is the form that engages the GIST index created by raster2pgsql -I
    SELECT ST_Clip(ST_Union(t.rast), ST_Transform(a.geom, v_srid), TRUE)
      INTO v_rast
      FROM flood.aoi a
      JOIN flood.dem_tiles t
        ON ST_ConvexHull(t.rast) && ST_Transform(a.geom, v_srid)
     WHERE a.name = p_aoi
     GROUP BY a.geom;

    IF v_rast IS NULL THEN
        RAISE EXCEPTION 'No DEM coverage found for AOI "%"', p_aoi;
    END IF;

    RETURN QUERY
    SELECT ST_Width(v_rast), ST_Height(v_rast), ST_SRID(v_rast),
           ST_UpperLeftX(v_rast), ST_UpperLeftY(v_rast),
           ST_ScaleX(v_rast), ST_BandNoDataValue(v_rast, 1),
           ST_DumpValues(v_rast, 1);
END;
$$;
