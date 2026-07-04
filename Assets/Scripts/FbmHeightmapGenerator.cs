using UnityEngine;

public class FbmHeightmapGenerator : IHeightmapGenerator
{
    private readonly float baseFrequency;
    private readonly int octaves;
    private readonly float persistence;
    private readonly float lacunarity;
    private readonly float heightScale;

    public FbmHeightmapGenerator(
        float baseFrequency,
        int octaves,
        float persistence,
        float lacunarity,
        float heightScale)
    {
        this.baseFrequency = baseFrequency;
        this.octaves = octaves;
        this.persistence = persistence;
        this.lacunarity = lacunarity;
        this.heightScale = heightScale;
    }

    public string AlgorithmName => "fBm";

    public float[,] Generate(int resolution, int seed)
    {
        float[,] heights = new float[resolution, resolution];
        float seedOffsetX = seed * 0.001f;
        float seedOffsetY = seed * 0.002f;
        float maxAmplitude = 0f;
        float amplitude = 1f;

        for (int octave = 0; octave < octaves; octave++)
        {
            maxAmplitude += amplitude;
            amplitude *= persistence;
        }

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float normalizedX = x / (float)(resolution - 1);
                float normalizedY = y / (float)(resolution - 1);
                float frequency = baseFrequency;
                amplitude = 1f;
                float value = 0f;

                for (int octave = 0; octave < octaves; octave++)
                {
                    float sampleX = normalizedX * frequency + seedOffsetX;
                    float sampleY = normalizedY * frequency + seedOffsetY;
                    value += Mathf.PerlinNoise(sampleX, sampleY) * amplitude;

                    frequency *= lacunarity;
                    amplitude *= persistence;
                }

                float normalizedValue = maxAmplitude > 0f ? value / maxAmplitude : 0f;
                heights[y, x] = Mathf.Clamp01(normalizedValue * heightScale);
            }
        }

        return heights;
    }
}
