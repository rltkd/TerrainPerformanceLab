using System.Collections;
using System.Diagnostics;
using UnityEngine;
using Unity.Profiling;

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
            UnityEngine.Debug.LogWarning("PerlinTerrainGenerator requires a Terrain reference.", this);
            return;
        }

        StartCoroutine(GenerateTerrainWithProfiling());
    }

    private IEnumerator GenerateTerrainWithProfiling()
    {
        ProfilerRecorder totalUsedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);

        try
        {
            yield return null;

            int generationFrameCount = Time.frameCount;
            Stopwatch totalStopwatch = Stopwatch.StartNew();

            TerrainData terrainData = targetTerrain.terrainData;
            terrainData.heightmapResolution = heightmapResolution;
            terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainLength);

            Stopwatch algorithmStopwatch = Stopwatch.StartNew();
            float[,] heights = GenerateHeights(heightmapResolution);
            algorithmStopwatch.Stop();

            Stopwatch setHeightsStopwatch = Stopwatch.StartNew();
            terrainData.SetHeights(0, 0, heights);
            setHeightsStopwatch.Stop();

            totalStopwatch.Stop();

            double algorithmMs = algorithmStopwatch.Elapsed.TotalMilliseconds;
            double setHeightsMs = setHeightsStopwatch.Elapsed.TotalMilliseconds;
            double totalMs = totalStopwatch.Elapsed.TotalMilliseconds;

            yield return null;

            int measurementFrameCount = Time.frameCount;
            double generationFrameMs = Time.unscaledDeltaTime * 1000.0;
            bool hasTotalUsedMemorySample = totalUsedMemoryRecorder.Valid && totalUsedMemoryRecorder.Count > 0;

            if (!hasTotalUsedMemorySample)
            {
                UnityEngine.Debug.LogWarning(
                    $"ProfilerRecorder sample missing. generation_frame_count={generationFrameCount}, " +
                    $"total_used_memory_valid={totalUsedMemoryRecorder.Valid}, " +
                    $"total_used_memory_count={totalUsedMemoryRecorder.Count}",
                    this);
            }

            double totalUsedMemoryMb = hasTotalUsedMemorySample
                ? totalUsedMemoryRecorder.LastValue / (1024.0 * 1024.0)
                : double.NaN;

            UnityEngine.Debug.Log(
                $"algorithm_ms={algorithmMs:F3}, set_heights_ms={setHeightsMs:F3}, total_ms={totalMs:F3}, " +
                $"generation_frame_ms={generationFrameMs:F3}, total_used_memory_mb={totalUsedMemoryMb:F3}, " +
                $"generation_frame_count={generationFrameCount}, measurement_frame_count={measurementFrameCount}",
                this);
        }
        finally
        {
            totalUsedMemoryRecorder.Dispose();
        }
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
