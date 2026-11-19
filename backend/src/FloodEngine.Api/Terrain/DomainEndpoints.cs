using FloodEngine.Core;

namespace FloodEngine.Api.Terrain;

internal static class DomainEndpoints
{
    private const string SharedDomainId = "gulshan_jauhar";

    public static void MapDomainEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var domain = endpoints.MapGroup($"/api/v1/domains/{SharedDomainId}");
        domain.MapGet("/terrain", ReadTerrainAsync);
        domain.MapGet("/flow", ReadFlowAsync);
        domain.MapGet("/sectors", ReadSectorsAsync);
    }

    private static Task<DemGrid> ReadTerrainAsync(PostgisDemReader reader, CancellationToken cancellationToken) =>
        reader.ReadAsync(SharedDomainId, cancellationToken);

    private static async Task<FlowResponse> ReadFlowAsync(
        PostgisDemReader reader, CancellationToken cancellationToken)
    {
        var dem = await reader.ReadAsync(SharedDomainId, cancellationToken);
        var grid = dem.Grid;
        var flow = D8Routing.Run(grid.Rows, grid.Cols, grid.CellSizeM, dem.ElevationM);
        var direction = new string?[flow.Directions.Count];
        for (int i = 0; i < direction.Length; i++)
        {
            direction[i] = flow.Directions[i] switch
            {
                null => null,
                D8Direction.None => "NONE",
                var code => code.ToString()
            };
        }

        return new FlowResponse(grid, direction, flow.ContributingCellCounts);
    }

    private sealed record FlowResponse(
        GridMetadata Grid,
        string?[] Direction,
        IReadOnlyList<int?> ContributingCellCount);

    private static async Task<SectorsResponse> ReadSectorsAsync(
        PostgisDemReader demReader, PostgisSectorReader sectorReader, CancellationToken cancellationToken)
    {
        var dem = await demReader.ReadAsync(SharedDomainId, cancellationToken);
        var grid = dem.Grid;
        var masks = await sectorReader.ReadAsync(grid, dem.ElevationM.Length, cancellationToken);
        var flow = D8Routing.Run(grid.Rows, grid.Cols, grid.CellSizeM, dem.ElevationM);
        var reports = new List<SectorReport>(masks.Count);

        foreach (var mask in masks)
        {
            var indices = new List<int>(mask.CellIndices.Length);
            var counts = new List<int>(mask.CellIndices.Length);
            foreach (int index in mask.CellIndices)
            {
                if (flow.ContributingCellCounts[index] is not int count)
                    continue;

                indices.Add(index);
                counts.Add(count);
            }

            reports.Add(new SectorReport(mask.SectorId, indices, counts));
        }

        return new SectorsResponse(grid, reports);
    }

    private sealed record SectorsResponse(GridMetadata Grid, IReadOnlyList<SectorReport> Sectors);

    // Each reported count corresponds to the original grid index at the same position.
    private sealed record SectorReport(
        string SectorId,
        IReadOnlyList<int> CellIndices,
        IReadOnlyList<int> ContributingCellCount);
}
