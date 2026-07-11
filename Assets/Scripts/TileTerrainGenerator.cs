using System.Diagnostics;
using UnityEngine;

public class TileTerrainGenerator
{
    private readonly int resolution;
    private readonly float tileSize;
    private readonly float terrainHeight;
    private readonly float baseFrequency;
    private readonly int octaves;
    private readonly float persistence;
    private readonly float lacunarity;
    private readonly int seed;
    private readonly int tileCountX;
    private readonly int tileCountZ;
    private readonly Material terrainMaterial;

    public TileTerrainGenerator(
        int resolution,
        float tileSize,
        float terrainHeight,
        float baseFrequency,
        int octaves,
        float persistence,
        float lacunarity,
        int seed,
        int tileCountX,
        int tileCountZ,
        Material terrainMaterial)
    {
        this.resolution = resolution;
        this.tileSize = tileSize;
        this.terrainHeight = terrainHeight;
        this.baseFrequency = baseFrequency;
        this.octaves = octaves;
        this.persistence = persistence;
        this.lacunarity = lacunarity;
        this.seed = seed;
        this.tileCountX = tileCountX;
        this.tileCountZ = tileCountZ;
        this.terrainMaterial = terrainMaterial;
    }

    public GameObject CreateTileObject(int tileX, int tileZ)
    {
        TerrainData terrainData = new TerrainData();
        terrainData.heightmapResolution = resolution;
        terrainData.size = new Vector3(tileSize, terrainHeight, tileSize);

        GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
        terrainObject.name = $"Tile_{tileX}_{tileZ}";
        terrainObject.transform.position = new Vector3(tileX * tileSize, 0f, tileZ * tileSize);

        Terrain terrain = terrainObject.GetComponent<Terrain>();
        terrain.materialTemplate = terrainMaterial;

        return terrainObject;
    }

    public void GenerateTileHeights(GameObject terrainObject, int tileX, int tileZ, out double tileMs)
    {
        Terrain terrain = terrainObject.GetComponent<Terrain>();
        Stopwatch tileStopwatch = Stopwatch.StartNew();
        float[,] heights = GenerateHeights(tileX, tileZ);
        terrain.terrainData.SetHeights(0, 0, heights);

        tileStopwatch.Stop();
        tileMs = tileStopwatch.Elapsed.TotalMilliseconds;
    }

    private float[,] GenerateHeights(int tileX, int tileZ)
    {
        float[,] heights = new float[resolution, resolution];
        float seedOffsetX = seed * 0.001f;
        float seedOffsetZ = seed * 0.002f;
        float totalWorldWidth = tileCountX * tileSize;
        float totalWorldLength = tileCountZ * tileSize;
        float maxAmplitude = 0f;
        float amplitude = 1f;

        for (int octave = 0; octave < octaves; octave++)
        {
            maxAmplitude += amplitude;
            amplitude *= persistence;
        }

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float worldX = tileX * tileSize + (x / (float)(resolution - 1)) * tileSize;
                float worldZ = tileZ * tileSize + (z / (float)(resolution - 1)) * tileSize;
                float normalizedWorldX = worldX / totalWorldWidth;
                float normalizedWorldZ = worldZ / totalWorldLength;
                float frequency = baseFrequency;
                amplitude = 1f;
                float value = 0f;

                for (int octave = 0; octave < octaves; octave++)
                {
                    float sampleX = normalizedWorldX * frequency + seedOffsetX;
                    float sampleZ = normalizedWorldZ * frequency + seedOffsetZ;
                    value += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;

                    frequency *= lacunarity;
                    amplitude *= persistence;
                }

                float normalizedValue = maxAmplitude > 0f ? value / maxAmplitude : 0f;
                heights[z, x] = Mathf.Clamp01(normalizedValue);
            }
        }

        return heights;
    }
}
