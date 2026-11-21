using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FloodTwin
{
    [DataContract]
    internal sealed class DemGrid
    {
        [DataMember(Name = "grid", IsRequired = true)]
        public GridMetadata Grid { get; private set; }

        [DataMember(Name = "elevationM", IsRequired = true)]
        public float?[] ElevationM { get; private set; }

        public static DemGrid Read(Stream json)
        {
            var serializer = new DataContractJsonSerializer(typeof(DemGrid));
            if (serializer.ReadObject(json) is not DemGrid dem ||
                dem.Grid == null || dem.Grid.Rows < 1 || dem.Grid.Cols < 1 ||
                !float.IsFinite(dem.Grid.CellSizeM) || dem.Grid.CellSizeM <= 0 ||
                dem.ElevationM == null || dem.ElevationM.Length != (long)dem.Grid.Rows * dem.Grid.Cols)
                throw new InvalidDataException("Invalid DEM grid dimensions, spacing, or elevation count.");

            return dem;
        }
    }

    [DataContract]
    internal sealed class GridMetadata
    {
        [DataMember(Name = "domainId", IsRequired = true)] public string DomainId { get; private set; }
        [DataMember(Name = "rows", IsRequired = true)] public int Rows { get; private set; }
        [DataMember(Name = "cols", IsRequired = true)] public int Cols { get; private set; }
        [DataMember(Name = "srid", IsRequired = true)] public int Srid { get; private set; }
        [DataMember(Name = "originX", IsRequired = true)] public double OriginX { get; private set; }
        [DataMember(Name = "originY", IsRequired = true)] public double OriginY { get; private set; }
        [DataMember(Name = "cellSizeM", IsRequired = true)] public float CellSizeM { get; private set; }
    }
}
