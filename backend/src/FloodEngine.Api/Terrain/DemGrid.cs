namespace FloodEngine.Api.Terrain;

/// <summary>Projected grid metadata with an upper-left corner origin in metres.</summary>
public sealed record GridMetadata(
    string DomainId,
    int Cols,
    int Rows,
    int Srid,
    double OriginX,
    double OriginY,
    double CellSizeM);

/// <summary>Flat, row-major elevations in metres. Null marks NoData.</summary>
public sealed record DemGrid(GridMetadata Grid, double?[] ElevationM);
