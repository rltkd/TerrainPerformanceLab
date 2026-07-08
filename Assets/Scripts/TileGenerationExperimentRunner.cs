using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;

public class TileGenerationExperimentRunner : MonoBehaviour
{
    private enum TileGenerationMode
    {
        BatchAll,
        TilesPerFrame4,
        TilesPerFrame2,
        TilesPerFrame1
    }

    private struct TileMeasurementResult
    {
        public int Run;
        public TileGenerationMode GenerationMode;
        public int TileCount;
        public int TilesPerFrame;
        public double TotalCompletionMs;
        public double AverageTileMs;
        public double MaxTileMs;
        public double MaxFrameMs;
        public double AverageFrameMs;
        public double P95FrameMs;
        public int FramesUsed;
        public double TotalUsedMemoryMb;
    }

    [SerializeField] private int tileResolution = 1025;
    [SerializeField] private int tileCountX = 4;
    [SerializeField] private int tileCountZ = 4;
    [SerializeField] private float tileSize = 500f;
    [SerializeField] private float terrainHeight = 300f;
    [SerializeField] private int seed = 12345;
    [SerializeField] private float baseFrequency = 6f;
    [SerializeField] private int octaves = 5;
    [SerializeField] private float persistence = 0.5f;
    [SerializeField] private float lacunarity = 2.0f;
    [SerializeField] private int warmupCount = 1;
    [SerializeField] private int measurementCount = 10;
    [SerializeField] private bool quitAfterCompletion = true;
    [SerializeField] private TileGenerationMode[] generationModes =
    {
        TileGenerationMode.BatchAll,
        TileGenerationMode.TilesPerFrame4,
        TileGenerationMode.TilesPerFrame2,
        TileGenerationMode.TilesPerFrame1
    };

    private readonly List<GameObject> generatedTiles = new List<GameObject>();

    private void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        UnityEngine.Debug.Log(
            $"Tile runtime frame settings applied. QualitySettings.vSyncCount={QualitySettings.vSyncCount}, " +
            $"Application.targetFrameRate={Application.targetFrameRate}, " +
            $"Screen.currentResolution.refreshRateRatio={Screen.currentResolution.refreshRateRatio}",
            this);

        StartCoroutine(RunExperiment());
    }

    private IEnumerator RunExperiment()
    {
        List<TileMeasurementResult> results = new List<TileMeasurementResult>();

        for (int modeIndex = 0; modeIndex < generationModes.Length; modeIndex++)
        {
            TileGenerationMode mode = generationModes[modeIndex];
            yield return RunModeExperiment(mode, results);
            yield return CleanupTiles();
            yield return null;
        }

        string csvFilePath = WriteResultsCsv(results);
        UnityEngine.Debug.Log($"Tile generation experiment complete. CSV file: {csvFilePath}", this);

        if (quitAfterCompletion)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.Log("Quit After Completion is enabled, but the Editor will not quit.", this);
#else
            Application.Quit();
#endif
        }
    }

    private IEnumerator RunModeExperiment(TileGenerationMode mode, List<TileMeasurementResult> results)
    {
        int totalRunCount = warmupCount + measurementCount;
        int savedMeasurementCount = 0;
        int tilesPerFrame = GetTilesPerFrame(mode);
        int tileCount = tileCountX * tileCountZ;

        UnityEngine.Debug.Log($"[START] tile_generation_mode={mode}, tiles_per_frame={tilesPerFrame}", this);

        for (int runIndex = 0; runIndex < totalRunCount; runIndex++)
        {
            yield return CleanupTiles();
            yield return null;

            TileTerrainGenerator tileGenerator = new TileTerrainGenerator(
                tileResolution,
                tileSize,
                terrainHeight,
                baseFrequency,
                octaves,
                persistence,
                lacunarity,
                seed,
                tileCountX,
                tileCountZ);

            List<double> tileTimes = new List<double>(tileCount);
            List<double> frameTimes = new List<double>();

            for (int tileIndexForSetup = 0; tileIndexForSetup < tileCount; tileIndexForSetup++)
            {
                int tileX = tileIndexForSetup % tileCountX;
                int tileZ = tileIndexForSetup / tileCountX;
                generatedTiles.Add(tileGenerator.CreateTileObject(tileX, tileZ));
            }

            yield return null;

            ProfilerRecorder totalUsedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            int tileIndex = 0;

            while (tileIndex < tileCount)
            {
                int tilesThisFrame = Mathf.Min(tilesPerFrame, tileCount - tileIndex);

                for (int i = 0; i < tilesThisFrame; i++)
                {
                    int currentTileIndex = tileIndex + i;
                    int tileX = currentTileIndex % tileCountX;
                    int tileZ = currentTileIndex / tileCountX;
                    tileGenerator.GenerateTileHeights(generatedTiles[currentTileIndex], tileX, tileZ, out double tileMs);
                    tileTimes.Add(tileMs);
                }

                tileIndex += tilesThisFrame;

                if (tileIndex < tileCount)
                {
                    yield return null;
                    frameTimes.Add(Time.unscaledDeltaTime * 1000.0);
                }
            }

            totalStopwatch.Stop();

            yield return null;
            frameTimes.Add(Time.unscaledDeltaTime * 1000.0);

            bool hasTotalUsedMemorySample = totalUsedMemoryRecorder.Valid && totalUsedMemoryRecorder.Count > 0;

            if (!hasTotalUsedMemorySample)
            {
                UnityEngine.Debug.LogWarning(
                    $"ProfilerRecorder sample missing. generation_mode={mode}, total_used_memory_valid={totalUsedMemoryRecorder.Valid}, total_used_memory_count={totalUsedMemoryRecorder.Count}",
                    this);
            }

            double totalUsedMemoryMb = hasTotalUsedMemorySample
                ? totalUsedMemoryRecorder.LastValue / (1024.0 * 1024.0)
                : double.NaN;

            totalUsedMemoryRecorder.Dispose();

            if (runIndex >= warmupCount)
            {
                results.Add(new TileMeasurementResult
                {
                    Run = runIndex - warmupCount + 1,
                    GenerationMode = mode,
                    TileCount = tileCount,
                    TilesPerFrame = tilesPerFrame,
                    TotalCompletionMs = totalStopwatch.Elapsed.TotalMilliseconds,
                    AverageTileMs = CalculateAverage(tileTimes),
                    MaxTileMs = CalculateMax(tileTimes),
                    MaxFrameMs = CalculateMax(frameTimes),
                    AverageFrameMs = CalculateAverage(frameTimes),
                    P95FrameMs = CalculatePercentile(frameTimes, 0.95f),
                    FramesUsed = frameTimes.Count,
                    TotalUsedMemoryMb = totalUsedMemoryMb
                });
                savedMeasurementCount++;
            }
        }

        UnityEngine.Debug.Log($"[COMPLETE] tile_generation_mode={mode}, measurements={savedMeasurementCount}", this);
    }

    private IEnumerator CleanupTiles()
    {
        for (int i = 0; i < generatedTiles.Count; i++)
        {
            if (generatedTiles[i] != null)
            {
                Destroy(generatedTiles[i]);
            }
        }

        generatedTiles.Clear();
        yield return null;
    }

    private string WriteResultsCsv(List<TileMeasurementResult> results)
    {
#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string resultsDirectory = Path.Combine(projectRoot, "Results");
#else
        string resultsDirectory = Path.Combine(Application.persistentDataPath, "Results");
#endif
        Directory.CreateDirectory(resultsDirectory);

        StringBuilder csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("run,generation_mode,tile_layout,tile_count,tiles_per_frame,tile_resolution,total_completion_ms,average_tile_ms,max_tile_ms,max_frame_ms,average_frame_ms,p95_frame_ms,frames_used,total_used_memory_mb");

        for (int i = 0; i < results.Count; i++)
        {
            AppendCsvRow(csvBuilder, results[i]);
        }

        string fileName = $"tile_generation_results_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string filePath = Path.Combine(resultsDirectory, fileName);
        File.WriteAllText(filePath, csvBuilder.ToString(), Encoding.UTF8);

        UnityEngine.Debug.Log($"Tile generation results CSV saved: {filePath}, total_rows={results.Count}", this);
        return filePath;
    }

    private void AppendCsvRow(StringBuilder csvBuilder, TileMeasurementResult result)
    {
        csvBuilder.Append(result.Run);
        csvBuilder.Append(',');
        csvBuilder.Append(result.GenerationMode);
        csvBuilder.Append(',');
        csvBuilder.Append(tileCountX);
        csvBuilder.Append('x');
        csvBuilder.Append(tileCountZ);
        csvBuilder.Append(',');
        csvBuilder.Append(result.TileCount);
        csvBuilder.Append(',');
        csvBuilder.Append(result.TilesPerFrame);
        csvBuilder.Append(',');
        csvBuilder.Append(tileResolution);
        csvBuilder.Append(',');
        csvBuilder.Append(result.TotalCompletionMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.AverageTileMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.MaxTileMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.MaxFrameMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.AverageFrameMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.P95FrameMs.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.Append(',');
        csvBuilder.Append(result.FramesUsed);
        csvBuilder.Append(',');
        csvBuilder.Append(result.TotalUsedMemoryMb.ToString("F3", CultureInfo.InvariantCulture));
        csvBuilder.AppendLine();
    }

    private int GetTilesPerFrame(TileGenerationMode mode)
    {
        switch (mode)
        {
            case TileGenerationMode.TilesPerFrame4:
                return 4;
            case TileGenerationMode.TilesPerFrame2:
                return 2;
            case TileGenerationMode.TilesPerFrame1:
                return 1;
            default:
                return tileCountX * tileCountZ;
        }
    }

    private double CalculateAverage(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        double sum = 0.0;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }

        return sum / values.Count;
    }

    private double CalculateMax(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        double max = values[0];
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
            }
        }

        return max;
    }

    private double CalculatePercentile(List<double> values, float percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        List<double> sortedValues = new List<double>(values);
        sortedValues.Sort();
        int index = Mathf.Clamp(Mathf.CeilToInt(percentile * sortedValues.Count) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }
}
