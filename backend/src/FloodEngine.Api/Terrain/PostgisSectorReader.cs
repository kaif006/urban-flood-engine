using Npgsql;

namespace FloodEngine.Api.Terrain;

internal sealed class PostgisSectorReader(NpgsqlDataSource dataSource)
{
    private static readonly string[] SectorIds = ["gulshan_e_iqbal", "gulistan_e_jauhar"];

    private const string ReadMasks = """
        SELECT sector.name, ARRAY(
            SELECT cell_index
            FROM generate_series(0, $3 - 1) AS cells(cell_index)
            WHERE ST_Covers(sector.geom, ST_SetSRID(ST_MakePoint(
                $4 + (cell_index % $2 + 0.5) * $6,
                $5 - (cell_index / $2 + 0.5) * $6
            ), $7))
            ORDER BY cell_index
        ) AS cell_indices
        FROM (
            SELECT name, ST_Transform(geom, $7) AS geom
            FROM flood.aoi
            WHERE name = ANY($1)
        ) AS sector
        ORDER BY sector.name
        """;

    public async Task<IReadOnlyList<SectorMask>> ReadAsync(
        GridMetadata grid, int cellCount, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ReadMasks);
        command.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = SectorIds });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = grid.Cols });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = cellCount });
        command.Parameters.Add(new NpgsqlParameter<double> { TypedValue = grid.OriginX });
        command.Parameters.Add(new NpgsqlParameter<double> { TypedValue = grid.OriginY });
        command.Parameters.Add(new NpgsqlParameter<double> { TypedValue = grid.CellSizeM });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = grid.Srid });

        var masks = new List<SectorMask>(SectorIds.Length);
        await using var result = await command.ExecuteReaderAsync(cancellationToken);
        while (await result.ReadAsync(cancellationToken))
            masks.Add(new SectorMask(result.GetString(0), result.GetFieldValue<int[]>(1)));

        if (masks.Count != SectorIds.Length)
            throw new InvalidDataException("Both reporting sectors must be registered in flood.aoi.");

        return masks;
    }
}

internal sealed record SectorMask(string SectorId, int[] CellIndices);
