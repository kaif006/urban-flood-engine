-- scripts/verify.sql  (docker compose exec -T db psql -U $POSTGRES_USER -d $POSTGRES_DB -f /dev/stdin < scripts/verify.sql)

-- Raster registered with expected SRID / scale / tile size / nodata
SELECT r_table_name, srid, scale_x, scale_y, blocksize_x, blocksize_y, nodata_values
FROM raster_columns WHERE r_table_schema = 'flood';

-- GIST index present on the tile footprint
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname = 'flood' AND tablename = 'dem_tiles';

-- Index is actually used for AOI window queries
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM flood.dem_tiles t, flood.aoi a
WHERE a.name = 'test_sector'
  AND ST_ConvexHull(t.rast) && ST_Transform(a.geom, 32642);

-- Matrix dimensions delivered to the backend
SELECT ncols, nrows, srid, origin_x, origin_y, cell_size, nodata FROM flood.dem_matrix('test_sector');
