"""Convert an OpenStreetMap XML extract into the Unity pilot mesh data.

Requires rasterio and Shapely 2.1+.
"""
import argparse
import json
import math
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from rasterio.warp import transform
from shapely import constrained_delaunay_triangles, make_valid, segmentize, union_all
from shapely.geometry import LineString, Polygon
from shapely.geometry.polygon import orient


BBOX = (67.093, 24.921, 67.100, 24.927)  # west, south, east, north
SRID = 32642
SOURCE = "https://api.openstreetmap.org/api/0.6/map?bbox=67.093,24.921,67.100,24.927"
ROAD_WIDTHS = {
    "motorway": 12, "trunk": 12, "primary": 10, "secondary": 9,
    "tertiary": 7, "unclassified": 6, "residential": 5,
    "living_street": 4, "service": 3,
}


def project(points):
    lon, lat = zip(*points)
    east, north = transform("EPSG:4326", f"EPSG:{SRID}", lon, lat)
    return list(zip(east, north))


def polygons(geometry):
    if geometry.is_empty:
        return
    if geometry.geom_type == "Polygon":
        if geometry.area >= 0.5:
            yield orient(geometry, sign=-1)
    elif hasattr(geometry, "geoms"):
        for part in geometry.geoms:
            yield from polygons(part)


def flatten(points):
    return [round(value, 3) for point in points for value in point]


def triangle_points(polygon):
    return [
        value
        for triangle in constrained_delaunay_triangles(polygon).geoms
        for value in flatten(list(orient(triangle, sign=-1).exterior.coords)[:-1])
    ]


def metres(tag):
    match = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*(?:m)?\s*", tag)
    return float(match[1]) if match else None


def building_height(tags):
    height = metres(tags.get("height", ""))
    if height is not None and 0 < height <= 150:
        return height, "height"
    levels = metres(tags.get("building:levels", ""))
    if levels is not None and 0 < levels <= 50:
        return levels * 3, "levels"
    return 9, "default"


def generate(source_file):
    root = ET.parse(source_file).getroot()
    nodes = {
        node.get("id"): (float(node.get("lon")), float(node.get("lat")))
        for node in root.findall("node")
    }
    projected = dict(zip(nodes, project(list(nodes.values())), strict=True))
    west, south, east, north = BBOX
    boundary = Polygon(project([(west, south), (east, south), (east, north), (west, north)]))
    roads, buildings = [], []
    road_ids = []
    for way in root.findall("way"):
        tags = {tag.get("k"): tag.get("v") for tag in way.findall("tag")}
        highway = tags.get("highway", "").removesuffix("_link")
        is_road = (highway in ROAD_WIDTHS and tags.get("area") != "yes"
                   and tags.get("bridge", "no") == "no" and tags.get("tunnel", "no") == "no")
        is_building = "building" in tags and tags["building"] != "no"
        if not is_road and not is_building:
            continue

        refs = [node.get("ref") for node in way.findall("nd")]
        if len(refs) < 2 or any(ref not in projected for ref in refs):
            continue
        coordinates = [projected[ref] for ref in refs]
        if is_road:
            width = metres(tags.get("width", ""))
            if width is None or not 1 <= width <= 35:
                width = ROAD_WIDTHS[highway]
            road = LineString(coordinates).buffer(width / 2, cap_style="flat", join_style="round")
            road = road.intersection(boundary)
            if not road.is_empty:
                roads.append(road)
                road_ids.append(int(way.get("id")))

        if is_building and len(refs) > 3 and refs[0] == refs[-1]:
            height, height_source = building_height(tags)
            footprint = make_valid(Polygon(coordinates)).intersection(boundary)
            for part in polygons(footprint):
                buildings.append({
                    "osmWayId": int(way.get("id")),
                    "heightM": height,
                    "heightSource": height_source,
                    "roof": triangle_points(part),
                    "rings": [flatten(list(ring.coords)) for ring in [part.exterior, *part.interiors]],
                })

    road_triangles = [
        value
        for part in polygons(segmentize(union_all(roads), max_segment_length=8))
        for value in triangle_points(part)
    ]
    if not road_triangles or not buildings:
        raise ValueError("The extract must contain both road and building geometry.")
    if not all(math.isfinite(value) for value in road_triangles):
        raise ValueError("Road geometry contains a non-finite coordinate.")
    return {
        "srid": SRID,
        "sourceUrl": SOURCE,
        "licenseUrl": "https://opendatacommons.org/licenses/odbl/1-0/",
        "extent": list(boundary.bounds),
        "roadWayIds": road_ids,
        "roadTriangles": road_triangles,
        "buildings": buildings,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    pilot = generate(args.source)
    args.output.write_text(json.dumps(pilot, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Exported {len(pilot['roadWayIds'])} road ways and {len(pilot['buildings'])} building parts.")


if __name__ == "__main__":
    main()
