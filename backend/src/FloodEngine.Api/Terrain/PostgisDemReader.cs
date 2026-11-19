using Npgsql;
using NpgsqlTypes;

namespace FloodEngine.Api.Terrain;

public sealed class PostgisDemReader(NpgsqlDataSource dataSource)
{
    private const string ReadDem = """
        SELECT d.ncols, d.nrows, d.srid, d.origin_x, d.origin_y, d.cell_size,
               ARRAY(SELECT unnest(d.elev)) AS elevation_m
        FROM flood.dem_matrix($1) AS d
        """;

    public async Task<DemGrid> ReadAsync(string domainId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainId);

        await using var command = dataSource.CreateCommand(ReadDem);
        command.Parameters.Add(new NpgsqlParameter<string> { NpgsqlDbType = NpgsqlDbType.Text, TypedValue = domainId });

        await using var result = await command.ExecuteReaderAsync(cancellationToken);
        if (!await result.ReadAsync(cancellationToken))
            throw new InvalidDataException($"The database returned no DEM grid for '{domainId}'.");

        var grid = new GridMetadata(
            domainId, result.GetInt32(0), result.GetInt32(1), result.GetInt32(2),
            result.GetDouble(3), result.GetDouble(4), result.GetDouble(5));

        // PostgreSQL unnest preserves array storage order and null cell positions.
        var elevations = result.GetFieldValue<double?[]>(6);
        if (grid.Cols <= 0 || grid.Rows <= 0 || (long)grid.Cols * grid.Rows != elevations.Length)
            throw new InvalidDataException($"Elevation count does not match the dimensions of '{domainId}'.");

        return new DemGrid(grid, elevations);
    }
}
