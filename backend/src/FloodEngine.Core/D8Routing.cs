namespace FloodEngine.Core;

public enum D8Direction { None, N, NE, E, SE, S, SW, W, NW }

/// <summary>Row-major outputs. Null marks NoData. Counts include the cell itself.</summary>
public sealed record D8Result(
    IReadOnlyList<D8Direction?> Directions,
    IReadOnlyList<int?> ContributingCellCounts);

public static class D8Routing
{
    private static readonly (int Row, int Column, D8Direction Direction)[] Neighbors =
    [
        (-1, 0, D8Direction.N), (-1, 1, D8Direction.NE),
        (0, 1, D8Direction.E), (1, 1, D8Direction.SE),
        (1, 0, D8Direction.S), (1, -1, D8Direction.SW),
        (0, -1, D8Direction.W), (-1, -1, D8Direction.NW)
    ];

    /// <summary>
    /// Fills depressions to spill level for routing. Valid cells next to the grid edge
    /// or NoData anchor filling and can terminate flow. NoData never receives flow.
    /// </summary>
    public static D8Result Run(int rows, int columns, double cellSizeM, ReadOnlySpan<double?> elevationM)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        if (!double.IsFinite(cellSizeM) || cellSizeM <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeM));
        if ((long)rows * columns != elevationM.Length)
            throw new ArgumentException("Expected one elevation per grid cell.", nameof(elevationM));

        var surface = elevationM.ToArray();
        foreach (var elevation in surface)
            if (elevation is double value && !double.IsFinite(value))
                throw new ArgumentException("Elevations must be finite or null.", nameof(elevationM));

        var flatDirections = new int[surface.Length];
        Array.Fill(flatDirections, -1);
        FillDepressions(rows, columns, surface, flatDirections);

        var receivers = new int[surface.Length];
        Array.Fill(receivers, -1);
        var directions = new D8Direction?[surface.Length];
        for (int cell = 0; cell < surface.Length; cell++)
        {
            if (surface[cell] is not double height)
                continue;

            int direction = flatDirections[cell];
            double steepest = 0;
            for (int step = 0; step < Neighbors.Length; step++)
            {
                int neighbor = GetNeighbor(cell, step, rows, columns);
                if (neighbor < 0 || surface[neighbor] is not double lower || lower >= height)
                    continue;

                var offset = Neighbors[step];
                // The common cell size cancels when comparing slopes.
                double slope = height - lower;
                if (offset.Row != 0 && offset.Column != 0)
                    slope /= Math.Sqrt(2);
                if (slope > steepest)
                {
                    steepest = slope;
                    direction = step;
                }
            }

            directions[cell] = direction < 0 ? D8Direction.None : Neighbors[direction].Direction;
            if (direction >= 0)
                receivers[cell] = GetNeighbor(cell, direction, rows, columns);
        }

        return new D8Result(Array.AsReadOnly(directions), Array.AsReadOnly(Accumulate(directions, receivers)));
    }

    private static void FillDepressions(int rows, int columns, double?[] surface, int[] flatDirections)
    {
        var visited = new bool[surface.Length];
        var frontier = new PriorityQueue<int, (double Height, int Cell)>();
        for (int cell = 0; cell < surface.Length; cell++)
        {
            if (surface[cell] is not double height)
                continue;

            for (int step = 0; step < Neighbors.Length; step++)
            {
                int neighbor = GetNeighbor(cell, step, rows, columns);
                if (neighbor >= 0 && surface[neighbor].HasValue)
                    continue;

                visited[cell] = true;
                frontier.Enqueue(cell, (height, cell));
                break;
            }
        }

        while (frontier.TryDequeue(out int cell, out var priority))
        {
            for (int step = 0; step < Neighbors.Length; step++)
            {
                int neighbor = GetNeighbor(cell, step, rows, columns);
                if (neighbor < 0 || visited[neighbor] || surface[neighbor] is not double height)
                    continue;

                visited[neighbor] = true;
                double filled = Math.Max(height, priority.Height);
                surface[neighbor] = filled;
                // Follow the earlier flood cell across a flat if no downhill step exists.
                if (filled == priority.Height)
                    flatDirections[neighbor] = (step + 4) % Neighbors.Length;
                frontier.Enqueue(neighbor, (filled, neighbor));
            }
        }
    }

    private static int?[] Accumulate(D8Direction?[] directions, int[] receivers)
    {
        var counts = new int?[receivers.Length];
        var incoming = new int[receivers.Length];
        foreach (int receiver in receivers)
            if (receiver >= 0)
                incoming[receiver]++;

        var ready = new Queue<int>();
        int valid = 0;
        for (int cell = 0; cell < directions.Length; cell++)
        {
            if (!directions[cell].HasValue)
                continue;

            valid++;
            counts[cell] = 1;
            if (incoming[cell] == 0)
                ready.Enqueue(cell);
        }

        int processed = 0;
        while (ready.TryDequeue(out int cell))
        {
            processed++;
            int receiver = receivers[cell];
            if (receiver < 0)
                continue;

            counts[receiver] += counts[cell];
            if (--incoming[receiver] == 0)
                ready.Enqueue(receiver);
        }

        if (processed != valid)
            throw new InvalidOperationException("D8 routing contains a cycle.");
        return counts;
    }

    private static int GetNeighbor(int cell, int step, int rows, int columns)
    {
        int row = cell / columns + Neighbors[step].Row;
        int column = cell % columns + Neighbors[step].Column;
        return row >= 0 && row < rows && column >= 0 && column < columns
            ? row * columns + column
            : -1;
    }
}
