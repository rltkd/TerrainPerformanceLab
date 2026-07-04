using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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
    [SerializeField] private int warmupCount = 1;
    [SerializeField] private int measurementCount = 10;

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
        TerrainData terrainData = targetTerrain.terrainData;
        terrainData.heightmapResolution = heightmapResolution;
        terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainLength);

        int totalRunCount = warmupCount + measurementCount;
        int savedMeasurementCount = 0;
        StringBuilder csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("run,algorithm,resolution,algorithm_ms,set_heights_ms,total_ms,generation_frame_ms,total_used_memory_mb");

        for (int runIndex = 0; runIndex < totalRunCount; runIndex++)
        {
            ResetTerrainHeights(terrainData);

            yield return null;

            ProfilerRecorder totalUsedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);

            try
            {
                int generationFrameCount = Time.frameCount;
                Stopwatch totalStopwatch = Stopwatch.StartNew();

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

                if (runIndex >= warmupCount)
                {
                    int measurementRun = runIndex - warmupCount + 1;
                    AppendCsvRow(csvBuilder, measurementRun, algorithmMs, setHeightsMs, totalMs, generationFrameMs, totalUsedMemoryMb);
                    savedMeasurementCount++;
                }
            }
            finally
            {
                totalUsedMemoryRecorder.Dispose();
            }
        }

#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string resultsDirectory = Path.Combine(projectRoot, "Results");
#else
        string resultsDirectory = Path.Combine(Application.persistentDataPath, "Results");
#endif
        Directory.CreateDirectory(resultsDirectory);

        string fileName = $"perlin_terrain_results_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string filePath = Path.Combine(resultsDirectory, fileName);
        File.WriteAllText(filePath, csvBuilder.ToString(), Encoding.UTF8);

        UnityEngine.Debug.Log(
            $"Perlin terrain benchmark CSV saved: {filePath}, saved_measurement_rows={savedMeasurementCount}",
            this);
    }

    private void ResetTerrainHeights(TerrainData terrainData)
    {
        terrainData.SetHeights(0, 0, new float[heightmapResolution, heightmapResolution]);
    }

    private void AppendCsvRow(
        StringBuilder csvBuilder,
        int run,
        double algorithmMs,
        double setHeightsMs,
        double totalMs,
        double generationFrameMs,
        double totalUsedMemoryMb)
    {
        csvBuilder.Append(run);
        csvBuilder.Append(",Perlin,");
        csvBuilder.Append(heightmapResolution);
        csvBuilder.Append(',');
        csvBuilder.Append(algorithmMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(setHeightsMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(totalMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(generationFrameMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(totalUsedMemoryMb.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.AppendLine();
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
