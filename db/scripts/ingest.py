#!/usr/bin/env python3
# scripts/ingest.py
"""
Phase 1 ingestion: SRTM DEM -> cleaned, metric-CRS GeoTIFF -> PostGIS raster tiles (+GIST),
and historical rainfall CSV -> PostGIS tables.

Usage:
    set -a; source .env; set +a
    python scripts/ingest.py dem --aoi-name lyari_malir --bbox 66.95 24.80 67.20 25.00 \
        --srtm data/raw/N24E066.hgt data/raw/N24E067.hgt
    python scripts/ingest.py rainfall --csv data/raw/rainfall.csv --source PMD
"""

from __future__ import annotations

import argparse
import csv
import logging
import os
import shlex
import subprocess
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import psycopg
import rasterio
from rasterio.enums import Resampling
from rasterio.fill import fillnodata
from rasterio.merge import merge
from rasterio.transform import array_bounds
from rasterio.warp import calculate_default_transform, reproject
from scipy.ndimage import median_filter

# --------------------------------------------------------------------------- constants
SRTM_VOID = -32768  # SRTM no-data sentinel (int16)
OUT_NODATA = -9999.0  # Nodata for the float32 output raster
TARGET_SRID = (
    32642  # WGS84 / UTM 42N -> metric units, required for slope/flow-length maths
)
TARGET_CRS = f"EPSG:{TARGET_SRID}"
ELEV_MIN, ELEV_MAX = (
    -50.0,
    1000.0,
)  # Plausibility window for Karachi terrain; values outside => nodata
DEM_TABLE = "flood.dem_tiles"
CONTAINER_DATA_ROOT = (
    "/data"  # Must match the ./data:/data volume in docker-compose.yml
)

log = logging.getLogger("ingest")


@dataclass(frozen=True)
class DemConfig:
    aoi_name: str
    bbox: tuple[float, float, float, float]  # (west, south, east, north) in EPSG:4326
    srtm_files: list[Path]
    data_root: Path
    cell_size: float
    tile_size: str
    denoise: int  # median filter window (odd int); 0 disables
    fill_dist: int  # max search distance (px) for void interpolation
    compose_service: str
    source_label: str


def connect() -> psycopg.Connection:
    """Connection params come from the same env vars consumed by docker-compose."""
    return psycopg.connect(
        host=os.getenv("POSTGRES_HOST", "localhost"),
        port=int(os.getenv("POSTGRES_PORT", "5432")),
        dbname=os.environ["POSTGRES_DB"],
        user=os.environ["POSTGRES_USER"],
        password=os.environ["POSTGRES_PASSWORD"],
    )


# =========================================================================== DEM pipeline
def mosaic_and_clip(files: list[Path], bbox, pad: float = 0.005):
    """Mosaic SRTM tiles and clip to bbox (+ small pad so reprojection has no edge artefacts)."""
    w, s, e, n = bbox
    srcs = [rasterio.open(f) for f in files]
    try:
        arr, transform = merge(
            srcs,
            bounds=(w - pad, s - pad, e + pad, n + pad),
            nodata=SRTM_VOID,
            dtype="int16",
        )
        crs = srcs[0].crs
    finally:
        for src in srcs:
            src.close()
    return arr[0], transform, crs


def clean_elevation(raw: np.ndarray, fill_dist: int, denoise: int) -> np.ndarray:
    """Void-fill, outlier-mask and denoise; returns float32 with OUT_NODATA for unresolved cells."""
    dem = raw.astype("float32")
    void = raw == SRTM_VOID
    log.info("SRTM voids: %d cells (%.2f%%)", void.sum(), 100 * void.mean())

    # 1) Interpolate voids (mask: 0 => fill, non-zero => valid)
    if void.any():
        dem = fillnodata(
            dem,
            mask=(~void).astype("uint8"),
            max_search_distance=fill_dist,
            smoothing_iterations=1,
        )
    dem[dem == SRTM_VOID] = OUT_NODATA  # voids beyond search distance stay nodata

    # 2) Mask physically implausible spikes/pits
    bad = (dem != OUT_NODATA) & ((dem < ELEV_MIN) | (dem > ELEV_MAX))
    dem[bad] = OUT_NODATA
    log.info("Outliers masked: %d cells", bad.sum())

    # 3) Optional speckle suppression; nodata footprint is preserved
    if denoise and denoise > 1:
        nodata_mask = dem == OUT_NODATA
        dem = median_filter(dem, size=denoise, mode="nearest")
        dem[nodata_mask] = OUT_NODATA
    return dem


def reproject_to_utm(
    dem: np.ndarray, transform, src_crs, cell_size: float, dst_path: Path
) -> None:
    """Warp to UTM 42N at a fixed metric cell size and write a tiled, compressed GeoTIFF."""
    h, w = dem.shape
    dst_transform, dst_w, dst_h = calculate_default_transform(
        src_crs, TARGET_CRS, w, h, *array_bounds(h, w, transform), resolution=cell_size
    )
    profile = dict(
        driver="GTiff",
        dtype="float32",
        count=1,
        crs=TARGET_CRS,
        transform=dst_transform,
        width=dst_w,
        height=dst_h,
        nodata=OUT_NODATA,
        tiled=True,
        blockxsize=256,
        blockysize=256,
        compress="deflate",
        predictor=3,
    )
    dst_path.parent.mkdir(parents=True, exist_ok=True)
    with rasterio.open(dst_path, "w", **profile) as dst:
        reproject(
            source=dem,
            destination=rasterio.band(dst, 1),
            src_transform=transform,
            src_crs=src_crs,
            src_nodata=OUT_NODATA,
            dst_transform=dst_transform,
            dst_crs=TARGET_CRS,
            dst_nodata=OUT_NODATA,
            resampling=Resampling.bilinear,
        )
    log.info("Wrote %s (%dx%d @ %.1f m)", dst_path, dst_w, dst_h, cell_size)


# =========================================================================== PostGIS load
def register_aoi(cfg: DemConfig) -> None:
    """Upsert AOI polygon (bbox envelope, EPSG:4326). Must precede dem_source insert (FK)."""
    w, s, e, n = cfg.bbox
    with connect() as conn:
        conn.execute(
            """INSERT INTO flood.aoi (name, geom)
               VALUES (%s, ST_MakeEnvelope(%s, %s, %s, %s, 4326))
               ON CONFLICT (name) DO UPDATE SET geom = EXCLUDED.geom""",
            (cfg.aoi_name, w, s, e, n),
        )


def load_dem_tiles(cfg: DemConfig, tif_path: Path) -> None:
    """
    Run raster2pgsql + psql INSIDE the container (no host-side PostGIS tooling required).
      -d  drop & recreate table      -I  GIST index on ST_ConvexHull(rast)
      -C  raster constraints         -M  VACUUM ANALYZE after load
      -Y  COPY (fast bulk load)      -F  add 'filename' column
      -t  tile size (e.g. 128x128)   -N  nodata value
    """
    container_tif = (
        f"{CONTAINER_DATA_ROOT}/{tif_path.relative_to(cfg.data_root).as_posix()}"
    )
    r2p = (
        f"raster2pgsql -s {TARGET_SRID} -t {cfg.tile_size} -I -C -M -Y -d -F "
        f"-N {int(OUT_NODATA)} {shlex.quote(container_tif)} {DEM_TABLE}"
    )
    inner = (
        f"set -o pipefail; {r2p} | "
        f'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
    )
    subprocess.run(
        ["docker", "compose", "exec", "-T", cfg.compose_service, "bash", "-c", inner],
        check=True,
    )
    log.info("Loaded %s from %s", DEM_TABLE, container_tif)


def record_dem_source(cfg: DemConfig) -> None:
    with connect() as conn:
        conn.execute(
            """INSERT INTO flood.dem_source
                   (aoi_name, source, input_files, srid, cell_size_m, tile_size, nodata_value)
               VALUES (%s, %s, %s, %s, %s, %s, %s)""",
            (
                cfg.aoi_name,
                cfg.source_label,
                [f.name for f in cfg.srtm_files],
                TARGET_SRID,
                cfg.cell_size,
                cfg.tile_size,
                OUT_NODATA,
            ),
        )


def run_dem(cfg: DemConfig) -> None:
    out_tif = cfg.data_root / "processed" / f"{cfg.aoi_name}_dem_utm.tif"
    raw, transform, crs = mosaic_and_clip(cfg.srtm_files, cfg.bbox)
    dem = clean_elevation(raw, cfg.fill_dist, cfg.denoise)
    reproject_to_utm(dem, transform, crs, cfg.cell_size, out_tif)
    register_aoi(cfg)
    load_dem_tiles(cfg, out_tif)
    record_dem_source(cfg)


# =========================================================================== rainfall
STATION_UPSERT = """
INSERT INTO flood.rainfall_station (station_id, name, geom)
VALUES (%(station_id)s, %(name)s, ST_SetSRID(ST_MakePoint(%(lon)s, %(lat)s), 4326))
ON CONFLICT (station_id) DO UPDATE SET name = EXCLUDED.name, geom = EXCLUDED.geom
"""
OBS_UPSERT = """
INSERT INTO flood.rainfall_observation (station_id, observed_on, rainfall_mm, source)
VALUES (%(station_id)s, %(date)s, %(rainfall_mm)s, %(source)s)
ON CONFLICT (station_id, observed_on) DO UPDATE
    SET rainfall_mm = EXCLUDED.rainfall_mm, source = EXCLUDED.source
"""


def run_rainfall(csv_path: Path, source: str) -> None:
    """CSV columns: station_id,name,lon,lat,date(YYYY-MM-DD),rainfall_mm. Blank rainfall rows are skipped."""
    stations: dict[str, dict] = {}
    observations: list[dict] = []
    skipped = 0
    with csv_path.open(newline="", encoding="utf-8-sig") as fh:
        for row in csv.DictReader(fh):
            if not (row.get("rainfall_mm") or "").strip():
                skipped += 1
                continue
            stations[row["station_id"]] = {
                k: row[k] for k in ("station_id", "name", "lon", "lat")
            }
            observations.append(
                {
                    "station_id": row["station_id"],
                    "date": row["date"],
                    "rainfall_mm": row["rainfall_mm"],
                    "source": source,
                }
            )
    with connect() as conn, conn.cursor() as cur:
        cur.executemany(STATION_UPSERT, list(stations.values()))
        cur.executemany(OBS_UPSERT, observations)
    log.info(
        "Rainfall: %d stations, %d observations upserted, %d blank rows skipped",
        len(stations),
        len(observations),
        skipped,
    )


# =========================================================================== CLI
def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    sub = p.add_subparsers(dest="cmd", required=True)

    d = sub.add_parser("dem", help="Ingest SRTM DEM for an AOI")
    d.add_argument("--aoi-name", required=True)
    d.add_argument(
        "--bbox", nargs=4, type=float, required=True, metavar=("W", "S", "E", "N")
    )
    d.add_argument("--srtm", nargs="+", type=Path, required=True)
    d.add_argument("--data-root", type=Path, default=Path("data"))
    d.add_argument(
        "--cell-size", type=float, default=30.0, help='metres (SRTM 1" ~30, 3" ~90)'
    )
    d.add_argument("--tile-size", default="128x128")
    d.add_argument("--denoise", type=int, default=3, help="median window; 0 disables")
    d.add_argument("--fill-dist", type=int, default=100)
    d.add_argument("--compose-service", default="db")
    d.add_argument("--source-label", default="NASA SRTM")

    r = sub.add_parser("rainfall", help="Ingest historical rainfall CSV")
    r.add_argument("--csv", type=Path, required=True)
    r.add_argument("--source", default="unspecified")
    return p


def main() -> None:
    logging.basicConfig(
        level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s"
    )
    args = build_parser().parse_args()
    if args.cmd == "dem":
        run_dem(
            DemConfig(
                aoi_name=args.aoi_name,
                bbox=tuple(args.bbox),
                srtm_files=args.srtm,
                data_root=args.data_root,
                cell_size=args.cell_size,
                tile_size=args.tile_size,
                denoise=args.denoise,
                fill_dist=args.fill_dist,
                compose_service=args.compose_service,
                source_label=args.source_label,
            )
        )
    else:
        run_rainfall(args.csv, args.source)


if __name__ == "__main__":
    main()
