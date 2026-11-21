using System.IO;
using UnityEngine;

namespace FloodTwin
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MockDemTerrain : MonoBehaviour
    {
        [SerializeField] private TextAsset fixture;

        private Mesh mesh;

        private void Start()
        {
            if (fixture == null)
            {
                Debug.LogError("Assign a mock DEM JSON fixture.", this);
                return;
            }

            using var json = new MemoryStream(fixture.bytes, writable: false);
            mesh = DemMeshBuilder.Build(DemGrid.Read(json));
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
