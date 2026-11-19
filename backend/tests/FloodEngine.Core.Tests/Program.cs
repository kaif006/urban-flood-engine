using FloodEngine.Core;

Check("Unity fixture", 2, 3,
    [12, 11, null, 10, 9, 8],
    [D8Direction.SE, D8Direction.SE, null, D8Direction.E, D8Direction.E, D8Direction.None],
    [1, 1, null, 1, 3, 5]);

Check("Flat shared with earlier boundary", 3, 3,
    [4, 4, 4, 4, 4, 4, 4, 4, 4],
    [D8Direction.None, D8Direction.None, D8Direction.None,
     D8Direction.None, D8Direction.NW, D8Direction.None,
     D8Direction.None, D8Direction.None, D8Direction.None],
    [2, 1, 1, 1, 1, 1, 1, 1, 1]);

Check("Pit fills to the spill", 3, 3,
    [9, 9, 9, 9, 0, 3, 9, 9, 9],
    [D8Direction.SE, D8Direction.S, D8Direction.S, D8Direction.E, D8Direction.E,
     D8Direction.None, D8Direction.NE, D8Direction.N, D8Direction.N],
    [1, 1, 1, 1, 6, 9, 1, 1, 1]);

Check("NoData separates a negative terminal from another cell", 1, 3,
    [-2, null, 4], [D8Direction.None, null, D8Direction.None], [1, null, 1]);
Check("All NoData", 1, 1, [null], [null], [null]);

static void Check(string name, int rows, int cols, double?[] elevation,
    D8Direction?[] directions, int?[] counts)
{
    var original = (double?[])elevation.Clone();
    var result = D8Routing.Run(rows, cols, 30, elevation);
    if (!result.Directions.SequenceEqual(directions))
        throw new InvalidOperationException($"{name}: directions were {string.Join(", ", result.Directions)}");
    if (!result.ContributingCellCounts.SequenceEqual(counts))
        throw new InvalidOperationException($"{name}: counts were {string.Join(", ", result.ContributingCellCounts)}");
    if (!elevation.SequenceEqual(original))
        throw new InvalidOperationException($"{name}: routing modified the source elevations.");
    Console.WriteLine($"PASS {name}");
}
