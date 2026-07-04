using UnityEngine;

public class PerlinHeightmapGenerator : IHeightmapGenerator
{
    private readonly float frequency;
    private readonly float heightScale;

    public PerlinHeightmapGenerator(float frequency, float heightScale)
    {
        this.frequency = frequency;
        this.heightScale = heightScale;
    }

    public string AlgorithmName => "Perlin";

    public float[,] Generate(int resolution, int seed)
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
