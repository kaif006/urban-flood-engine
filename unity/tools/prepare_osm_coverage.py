"""Bake the Karachi study area into 2 km Unity mesh batches from a Geofabrik OSM PBF.

Requires osmium, rasterio, and Shapely 2.1+.
"""
import argparse
import gzip
import json
from pathlib import Path
from uuid import NAMESPACE_URL, uuid5

import osmium
from shapely import STRtree, make_valid, segmentize, union_all
from shapely.geometry import LineString, Polygon, box

from prepare_osm_pilot import ROAD_WIDTHS, SRID, building_height, flatten, metres, polygons, project, triangle_points

WEST, SOUTH, EAST, NORTH = 67.04, 24.86, 67.19, 24.97
BATCH_SIZE = 2000
SOURCE = "https://download.geofabrik.de/asia/pakistan.html"


def clip_boundary():
    return Polygon(project([(WEST, SOUTH), (EAST, SOUTH), (EAST, NORTH), (WEST, NORTH)]))


def read_features(pbf, boundary):
    reader = (osmium.FileProcessor(pbf).with_locations()
              .with_filter(osmium.filter.EntityFilter(osmium.osm.WAY)))
    roads, buildings = [], []
    for way in reader:
        tags = way.tags
        highway = tags.get("highway", "").removesuffix("_link")
        is_road = (highway in ROAD_WIDTHS and tags.get("area") != "yes"
                   and tags.get("bridge", "no") == "no" and tags.get("tunnel", "no") == "no")
        is_building = tags.get("building") not in (None, "no") and way.is_closed()
        if not is_road and not is_building:
            continue

        points = [(node.lon, node.lat) for node in way.nodes if node.location.valid()]
        if len(points) != len(way.nodes) or len(points) < 2:
            continue
        lon, lat = zip(*points)
        # Include features that straddle the clip boundary and roads whose widths reach it.
        if (max(lon) < WEST - .0002 or min(lon) > EAST + .0002 or
                max(lat) < SOUTH - .0002 or min(lat) > NORTH + .0002):
            continue

        xy = project(points)
        if is_road:
            width = metres(tags.get("width", ""))
            if width is None or not 1 <= width <= 35:
                width = ROAD_WIDTHS[highway]
            geometry = LineString(xy).buffer(width / 2, cap_style="flat").intersection(boundary)
            if not geometry.is_empty:
                roads.append((way.id, geometry))

        if is_building and len(points) >= 4:
            height, source = building_height(tags)
            footprint = make_valid(Polygon(xy)).intersection(boundary)
            for part in polygons(footprint):
                buildings.append((part, {
                    "osmWayId": way.id,
                    "heightM": height,
                    "heightSource": source,
                    "roof": triangle_points(part),
                    "rings": [flatten(list(ring.coords)) for ring in [part.exterior, *part.interiors]],
                }))
    return roads, buildings, reader.header.get("osmosis_replication_timestamp", "")


def write_batches(output, boundary, roads, buildings, snapshot):
    output.mkdir(parents=True, exist_ok=True)
    min_x, min_y, max_x, max_y = boundary.bounds
    road_tree = STRtree([geometry for _, geometry in roads])
    buildings_by_batch = {}
    for geometry, building in buildings:
        centre = geometry.representative_point()
        key = (int((centre.x - min_x) // BATCH_SIZE), int((centre.y - min_y) // BATCH_SIZE))
        buildings_by_batch.setdefault(key, []).append(building)

    batch_count = 0
    for col, x in enumerate(range(int(min_x), int(max_x) + 1, BATCH_SIZE)):
        for row, y in enumerate(range(int(min_y), int(max_y) + 1, BATCH_SIZE)):
            x0, y0 = min_x + col * BATCH_SIZE, min_y + row * BATCH_SIZE
            extent = box(x0, y0, x0 + BATCH_SIZE, y0 + BATCH_SIZE)
            candidates = road_tree.query(extent, predicate="intersects")
            clipped = [(roads[i][0], roads[i][1].intersection(extent)) for i in sorted(candidates)]
            road_mesh = [
                value
                for polygon in polygons(segmentize(union_all([geom for _, geom in clipped]), max_segment_length=8))
                for value in triangle_points(polygon)
            ]
            batch_buildings = buildings_by_batch.get((col, row), [])
            if not road_mesh and not batch_buildings:
                continue

            payload = {
                "srid": SRID,
                "sourceUrl": SOURCE,
                "snapshot": snapshot,
                "licenseUrl": "https://opendatacommons.org/licenses/odbl/1-0/",
                "roadWayIds": [way_id for way_id, geom in clipped if geom.area > 0],
                "roadTriangles": road_mesh,
                "buildings": batch_buildings,
            }
            asset = output / f"tile_{col:02d}_{row:02d}.bytes"
            asset.write_bytes(gzip.compress(json.dumps(payload, separators=(",", ":")).encode(), mtime=0))
            meta = output / (asset.name + ".meta")
            if not meta.exists():
                guid = uuid5(NAMESPACE_URL, "FloodTwin/CityCoverage/" + asset.name).hex
                meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n")
            batch_count += 1

    return batch_count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("extract", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    boundary = clip_boundary()
    roads, buildings, snapshot = read_features(args.extract, boundary)
    if not roads or not buildings:
        raise ValueError("The extract must contain roads and buildings in the shared study area.")
    batches = write_batches(args.output, boundary, roads, buildings, snapshot)
    print(f"{batches} batches, {len(roads)} road ways, {len(buildings)} building parts. Source snapshot: {snapshot}")


if __name__ == "__main__":
    main()
