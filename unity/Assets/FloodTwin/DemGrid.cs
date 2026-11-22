using System.IO;
using Newtonsoft.Json;

namespace FloodTwin
{
    internal sealed class DemGrid
    {
        [JsonProperty("grid", Required = Required.Always)]
        public GridMetadata Grid { get; private set; }

        [JsonProperty("elevationM", Required = Required.Always)]
        public float?[] ElevationM { get; private set; }

        public static DemGrid Read(Stream json)
        {
            var dem = ReadJson<DemGrid>(json);
            if (dem.Grid == null || dem.Grid.Rows < 1 || dem.Grid.Cols < 1 ||
                !float.IsFinite(dem.Grid.CellSizeM) || dem.Grid.CellSizeM <= 0 ||
                dem.ElevationM == null || dem.ElevationM.Length != (long)dem.Grid.Rows * dem.Grid.Cols)
                throw new InvalidDataException("Invalid DEM grid dimensions, spacing, or elevation count.");

            return dem;
        }

        internal static T ReadJson<T>(Stream json) where T : class
        {
            using var reader = new StreamReader(json);
            using var tokens = new JsonTextReader(reader);
            var serializer = JsonSerializer.CreateDefault();
            return serializer.Deserialize<T>(tokens)
                ?? throw new InvalidDataException("Expected a JSON object.");
        }
    }

    internal sealed class GridMetadata
    {
        public bool Matches(GridMetadata other) =>
            other != null && DomainId == other.DomainId && Rows == other.Rows && Cols == other.Cols &&
            Srid == other.Srid && OriginX == other.OriginX && OriginY == other.OriginY &&
            CellSizeM == other.CellSizeM;

        [JsonProperty("domainId", Required = Required.Always)] public string DomainId { get; private set; }
        [JsonProperty("rows", Required = Required.Always)] public int Rows { get; private set; }
        [JsonProperty("cols", Required = Required.Always)] public int Cols { get; private set; }
        [JsonProperty("srid", Required = Required.Always)] public int Srid { get; private set; }
        [JsonProperty("originX", Required = Required.Always)] public double OriginX { get; private set; }
        [JsonProperty("originY", Required = Required.Always)] public double OriginY { get; private set; }
        [JsonProperty("cellSizeM", Required = Required.Always)] public float CellSizeM { get; private set; }
    }
}
