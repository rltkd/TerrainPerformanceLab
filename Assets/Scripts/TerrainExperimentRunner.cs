using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;

public class TerrainExperimentRunner : MonoBehaviour
{
    private enum HeightmapAlgorithm
    {
        Perlin,
        Fbm,
        DiamondSquare
    }

    [SerializeField] private Terrain targetTerrain;
    [SerializeField] private HeightmapAlgorithm selectedAlgorithm = HeightmapAlgorithm.Perlin;
    [SerializeField] private int heightmapResolution = 513;
    [SerializeField] private float terrainWidth = 500f;
    [SerializeField] private float terrainLength = 500f;
    [SerializeField] private float terrainHeight = 300f;
    [SerializeField] private int seed = 12345;
    [SerializeField] private float frequency = 6f;
    [SerializeField] private float heightScale = 1f;
    [SerializeField] private int fbmOctaves = 5;
    [SerializeField] private float fbmPersistence = 0.5f;
    [SerializeField] private float fbmLacunarity = 2.0f;
    [SerializeField] private float diamondSquareRoughness = 0.5f;
    [SerializeField] private int warmupCount = 1;
    [SerializeField] private int measurementCount = 10;

    private void Start()
    {
        if (targetTerrain == null)
        {
            UnityEngine.Debug.LogWarning("TerrainExperimentRunner requires a Terrain reference.", this);
            return;
        }

        StartCoroutine(GenerateTerrainWithProfiling());
    }

    private IEnumerator GenerateTerrainWithProfiling()
    {
        TerrainData terrainData = targetTerrain.terrainData;
        terrainData.heightmapResolution = heightmapResolution;
        terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainLength);
        IHeightmapGenerator heightmapGenerator = CreateHeightmapGenerator();

        if (selectedAlgorithm == HeightmapAlgorithm.DiamondSquare && !IsPowerOfTwoPlusOne(heightmapResolution))
        {
            UnityEngine.Debug.LogError(
                $"Diamond-Square requires heightmapResolution to be 2^n + 1. Current value: {heightmapResolution}",
                this);
            yield break;
        }

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
                float[,] heights = heightmapGenerator.Generate(heightmapResolution, seed);
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
                    AppendCsvRow(csvBuilder, measurementRun, heightmapGenerator.AlgorithmName, algorithmMs, setHeightsMs, totalMs, generationFrameMs, totalUsedMemoryMb);
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

        string algorithmFileName = GetAlgorithmFileName(heightmapGenerator.AlgorithmName);
        string fileName = $"{algorithmFileName}_terrain_results_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string filePath = Path.Combine(resultsDirectory, fileName);
        File.WriteAllText(filePath, csvBuilder.ToString(), Encoding.UTF8);

        UnityEngine.Debug.Log(
            $"{heightmapGenerator.AlgorithmName} terrain benchmark CSV saved: {filePath}, saved_measurement_rows={savedMeasurementCount}",
            this);
    }

    private void ResetTerrainHeights(TerrainData terrainData)
    {
        terrainData.SetHeights(0, 0, new float[heightmapResolution, heightmapResolution]);
    }

    private void AppendCsvRow(
        StringBuilder csvBuilder,
        int run,
        string algorithmName,
        double algorithmMs,
        double setHeightsMs,
        double totalMs,
        double generationFrameMs,
        double totalUsedMemoryMb)
    {
        csvBuilder.Append(run);
        csvBuilder.Append(',');
        csvBuilder.Append(algorithmName);
        csvBuilder.Append(',');
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

    private IHeightmapGenerator CreateHeightmapGenerator()
    {
        switch (selectedAlgorithm)
        {
            case HeightmapAlgorithm.DiamondSquare:
                return new DiamondSquareHeightmapGenerator(diamondSquareRoughness, heightScale);
            case HeightmapAlgorithm.Fbm:
                return new FbmHeightmapGenerator(frequency, fbmOctaves, fbmPersistence, fbmLacunarity, heightScale);
            default:
                return new PerlinHeightmapGenerator(frequency, heightScale);
        }
    }

    private bool IsPowerOfTwoPlusOne(int resolution)
    {
        int size = resolution - 1;
        return resolution > 1 && (size & (size - 1)) == 0;
    }

    private string GetAlgorithmFileName(string algorithmName)
    {
        return algorithmName.Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
    }
}
