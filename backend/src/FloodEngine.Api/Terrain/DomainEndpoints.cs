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
}
