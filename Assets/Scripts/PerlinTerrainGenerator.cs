using UnityEngine;

public class PerlinTerrainGenerator : MonoBehaviour
{
    [SerializeField] private Terrain targetTerrain;
    [SerializeField] private int heightmapResolution = 513;
    [SerializeField] private float terrainWidth = 500f;
    [SerializeField] private float terrainLength = 500f;
    [SerializeField] private float terrainHeight = 300f;
    [SerializeField] private int seed = 12345;
    [SerializeField] private float frequency = 6f;
    [SerializeField] private float heightScale = 1f;

    private void Start()
    {
        if (targetTerrain == null)
        {
            Debug.LogWarning("PerlinTerrainGenerator requires a Terrain reference.", this);
            return;
        }

        TerrainData terrainData = targetTerrain.terrainData;
        terrainData.heightmapResolution = heightmapResolution;
        terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainLength);

        float[,] heights = GenerateHeights(heightmapResolution);
        terrainData.SetHeights(0, 0, heights);
    }

    private float[,] GenerateHeights(int resolution)
    {
        float[,] heights = new float[resolution, resolution];
        float seedOffsetX = seed * 0.001f;
        float seedOffsetY = seed * 0.002f;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float normalizedX = x / (float)(resolution - 1);
                float normalizedY = y / (float)(resolution - 1);
                float sampleX = normalizedX * frequency + seedOffsetX;
                float sampleY = normalizedY * frequency + seedOffsetY;
                float noise = Mathf.PerlinNoise(sampleX, sampleY);

                heights[y, x] = Mathf.Clamp01(noise * heightScale);
            }
        }

        return heights;
    }
}
