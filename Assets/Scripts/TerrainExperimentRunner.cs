using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;

public class TerrainExperimentRunner : MonoBehaviour
{
    private enum ExperimentRunMode
    {
        SingleAlgorithm,
        RunAllAlgorithms
    }

    private enum HeightmapAlgorithm
    {
        Perlin,
        Fbm,
        DiamondSquare
    }

    private struct MeasurementResult
    {
        public int Run;
        public HeightmapAlgorithm Algorithm;
        public int Resolution;
        public double AlgorithmMs;
        public double SetHeightsMs;
        public double TotalMs;
        public double GenerationFrameMs;
        public double TotalUsedMemoryMb;
    }

    [SerializeField] private Terrain targetTerrain;
    [SerializeField] private ExperimentRunMode runMode = ExperimentRunMode.SingleAlgorithm;
    [SerializeField] private HeightmapAlgorithm selectedAlgorithm = HeightmapAlgorithm.Perlin;
    [SerializeField] private int heightmapResolution = 513;
    [SerializeField] private int[] measurementResolutions = { 513, 1025, 2049 };
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
    [SerializeField] private bool quitAfterCompletion = false;

    private bool experimentFailed;

    private void Start()
    {
        if (targetTerrain == null)
        {
            UnityEngine.Debug.LogWarning("TerrainExperimentRunner requires a Terrain reference.", this);
            return;
        }

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        UnityEngine.Debug.Log(
            $"Runtime frame settings applied. QualitySettings.vSyncCount={QualitySettings.vSyncCount}, " +
            $"Application.targetFrameRate={Application.targetFrameRate}, " +
            $"Screen.currentResolution.refreshRateRatio={Screen.currentResolution.refreshRateRatio}",
            this);

        StartCoroutine(RunExperiment());
    }

    private IEnumerator RunExperiment()
    {
        if (!ValidateMeasurementResolutions())
        {
            yield break;
        }

        TerrainData terrainData = targetTerrain.terrainData;
        terrainData.heightmapResolution = heightmapResolution;
        terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainLength);
        List<MeasurementResult> results = new List<MeasurementResult>();
        HeightmapAlgorithm[] algorithms = GetAlgorithmsToRun();

        foreach (HeightmapAlgorithm algorithm in algorithms)
        {
            foreach (int resolution in measurementResolutions)
            {
                terrainData.heightmapResolution = resolution;
                terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainLength);
                ResetTerrainHeights(terrainData, resolution);

                yield return null;

                yield return RunAlgorithmExperiment(terrainData, algorithm, resolution, results);

                if (experimentFailed)
                {
                    yield break;
                }

                yield return null;
            }
        }

        string csvFilePath = WriteResultsCsv(results, algorithms.Length, measurementResolutions.Length);
        UnityEngine.Debug.Log($"Terrain experiment complete. CSV file: {csvFilePath}", this);

        if (quitAfterCompletion)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.Log("Quit After Completion is enabled, but the Editor will not quit.", this);
#else
            Application.Quit();
#endif
        }
    }

    private IEnumerator RunAlgorithmExperiment(
        TerrainData terrainData,
        HeightmapAlgorithm algorithm,
        int resolution,
        List<MeasurementResult> results)
    {
        experimentFailed = false;
        IHeightmapGenerator heightmapGenerator = CreateHeightmapGenerator(algorithm);
        UnityEngine.Debug.Log($"[START] algorithm={heightmapGenerator.AlgorithmName}, resolution={resolution}", this);

        int totalRunCount = warmupCount + measurementCount;
        int savedMeasurementCount = 0;

        for (int runIndex = 0; runIndex < totalRunCount; runIndex++)
        {
            ResetTerrainHeights(terrainData, resolution);

            yield return null;

            ProfilerRecorder totalUsedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
            bool runFailed = false;
            int generationFrameCount = 0;
            double algorithmMs = 0.0;
            double setHeightsMs = 0.0;
            double totalMs = 0.0;

            try
            {
                generationFrameCount = Time.frameCount;
                Stopwatch totalStopwatch = Stopwatch.StartNew();

                try
                {
                    Stopwatch algorithmStopwatch = Stopwatch.StartNew();
                    float[,] heights = heightmapGenerator.Generate(resolution, seed);
                    algorithmStopwatch.Stop();

                    Stopwatch setHeightsStopwatch = Stopwatch.StartNew();
                    terrainData.SetHeights(0, 0, heights);
                    setHeightsStopwatch.Stop();

                    totalStopwatch.Stop();

                    algorithmMs = algorithmStopwatch.Elapsed.TotalMilliseconds;
                    setHeightsMs = setHeightsStopwatch.Elapsed.TotalMilliseconds;
                    totalMs = totalStopwatch.Elapsed.TotalMilliseconds;
                }
                catch (System.Exception exception)
                {
                    totalStopwatch.Stop();
                    UnityEngine.Debug.LogError(
                        $"{heightmapGenerator.AlgorithmName} experiment failed: {exception.Message}",
                        this);
                    experimentFailed = true;
                    runFailed = true;
                }

                if (!runFailed)
                {
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
                        results.Add(new MeasurementResult
                        {
                            Run = measurementRun,
                            Algorithm = algorithm,
                            Resolution = resolution,
                            AlgorithmMs = algorithmMs,
                            SetHeightsMs = setHeightsMs,
                            TotalMs = totalMs,
                            GenerationFrameMs = generationFrameMs,
                            TotalUsedMemoryMb = totalUsedMemoryMb
                        });
                        savedMeasurementCount++;
                    }
                }
            }
            finally
            {
                totalUsedMemoryRecorder.Dispose();
            }

            if (runFailed)
            {
                yield break;
            }
        }

        UnityEngine.Debug.Log(
            $"[COMPLETE] algorithm={heightmapGenerator.AlgorithmName}, resolution={resolution}, measurements={savedMeasurementCount}",
            this);
    }

    private void ResetTerrainHeights(TerrainData terrainData, int resolution)
    {
        terrainData.SetHeights(0, 0, new float[resolution, resolution]);
    }

    private string WriteResultsCsv(List<MeasurementResult> results, int algorithmCount, int resolutionCount)
    {
#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string resultsDirectory = Path.Combine(projectRoot, "Results");
#else
        string resultsDirectory = Path.Combine(Application.persistentDataPath, "Results");
#endif
        Directory.CreateDirectory(resultsDirectory);

        StringBuilder csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("run,algorithm,resolution,algorithm_ms,set_heights_ms,total_ms,generation_frame_ms,total_used_memory_mb");

        for (int i = 0; i < results.Count; i++)
        {
            AppendCsvRow(csvBuilder, results[i]);
        }

        string fileName = $"runtime_terrain_results_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string filePath = Path.Combine(resultsDirectory, fileName);
        File.WriteAllText(filePath, csvBuilder.ToString(), Encoding.UTF8);

        UnityEngine.Debug.Log(
            $"Runtime terrain results CSV saved: {filePath}, total_rows={results.Count}, algorithm_count={algorithmCount}, resolution_count={resolutionCount}",
            this);

        return filePath;
    }

    private void AppendCsvRow(StringBuilder csvBuilder, MeasurementResult result)
    {
        csvBuilder.Append(result.Run);
        csvBuilder.Append(',');
        csvBuilder.Append(GetAlgorithmName(result.Algorithm));
        csvBuilder.Append(',');
        csvBuilder.Append(result.Resolution);
        csvBuilder.Append(',');
        csvBuilder.Append(result.AlgorithmMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.SetHeightsMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.TotalMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.GenerationFrameMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.TotalUsedMemoryMb.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.AppendLine();
    }

    private HeightmapAlgorithm[] GetAlgorithmsToRun()
    {
        if (runMode == ExperimentRunMode.RunAllAlgorithms)
        {
            return new[]
            {
                HeightmapAlgorithm.Perlin,
                HeightmapAlgorithm.Fbm,
                HeightmapAlgorithm.DiamondSquare
            };
        }

        return new[] { selectedAlgorithm };
    }

    private IHeightmapGenerator CreateHeightmapGenerator(HeightmapAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case HeightmapAlgorithm.DiamondSquare:
                return new DiamondSquareHeightmapGenerator(diamondSquareRoughness, heightScale);
            case HeightmapAlgorithm.Fbm:
                return new FbmHeightmapGenerator(frequency, fbmOctaves, fbmPersistence, fbmLacunarity, heightScale);
            default:
                return new PerlinHeightmapGenerator(frequency, heightScale);
        }
    }

    private string GetAlgorithmName(HeightmapAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case HeightmapAlgorithm.DiamondSquare:
                return "Diamond-Square";
            case HeightmapAlgorithm.Fbm:
                return "fBm";
            default:
                return "Perlin";
        }
    }

    private bool ValidateMeasurementResolutions()
    {
        if (measurementResolutions == null || measurementResolutions.Length == 0)
        {
            UnityEngine.Debug.LogError("Measurement resolutions list is empty.", this);
            return false;
        }

        for (int i = 0; i < measurementResolutions.Length; i++)
        {
            int resolution = measurementResolutions[i];
            if (!IsPowerOfTwoPlusOne(resolution))
            {
                UnityEngine.Debug.LogError(
                    $"Invalid measurement resolution: {resolution}. All resolutions must be 2^n + 1.",
                    this);
                return false;
            }
        }

        return true;
    }

    private bool IsPowerOfTwoPlusOne(int resolution)
    {
        int size = resolution - 1;
        return resolution > 1 && (size & (size - 1)) == 0;
    }
}
