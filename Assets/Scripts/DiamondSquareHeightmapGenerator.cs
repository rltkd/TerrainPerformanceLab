using System;
using UnityEngine;

public class DiamondSquareHeightmapGenerator : IHeightmapGenerator
{
    private readonly float roughness;
    private readonly float heightScale;

    public DiamondSquareHeightmapGenerator(float roughness, float heightScale)
    {
        this.roughness = roughness;
        this.heightScale = heightScale;
    }

    public string AlgorithmName => "Diamond-Square";

    public float[,] Generate(int resolution, int seed)
    {
        if (!IsPowerOfTwoPlusOne(resolution))
        {
            throw new ArgumentException($"Diamond-Square requires resolution to be 2^n + 1. Current value: {resolution}");
        }

        float[,] heights = new float[resolution, resolution];
        System.Random random = new System.Random(seed);
        int last = resolution - 1;

        heights[0, 0] = NextRandomFloat(random);
        heights[0, last] = NextRandomFloat(random);
        heights[last, 0] = NextRandomFloat(random);
        heights[last, last] = NextRandomFloat(random);

        int stepSize = last;
        float displacement = 1f;

        while (stepSize > 1)
        {
            int halfStep = stepSize / 2;

            for (int y = halfStep; y < last; y += stepSize)
            {
                for (int x = halfStep; x < last; x += stepSize)
                {
                    float average =
                        (heights[y - halfStep, x - halfStep] +
                         heights[y - halfStep, x + halfStep] +
                         heights[y + halfStep, x - halfStep] +
                         heights[y + halfStep, x + halfStep]) * 0.25f;

                    heights[y, x] = average + RandomDisplacement(random, displacement);
                }
            }

            for (int y = 0; y <= last; y += halfStep)
            {
                int xStart = (y + halfStep) % stepSize;
                for (int x = xStart; x <= last; x += stepSize)
                {
                    float sum = 0f;
                    int count = 0;

                    if (x - halfStep >= 0)
                    {
                        sum += heights[y, x - halfStep];
                        count++;
                    }

                    if (x + halfStep <= last)
                    {
                        sum += heights[y, x + halfStep];
                        count++;
                    }

                    if (y - halfStep >= 0)
                    {
                        sum += heights[y - halfStep, x];
                        count++;
                    }

                    if (y + halfStep <= last)
                    {
                        sum += heights[y + halfStep, x];
                        count++;
                    }

                    heights[y, x] = (sum / count) + RandomDisplacement(random, displacement);
                }
            }

            stepSize /= 2;
            displacement *= roughness;
        }

        NormalizeHeights(heights, resolution);
        return heights;
    }

    private bool IsPowerOfTwoPlusOne(int resolution)
    {
        int size = resolution - 1;
        return resolution > 1 && (size & (size - 1)) == 0;
    }

    private float NextRandomFloat(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private float RandomDisplacement(System.Random random, float displacement)
    {
        return ((float)random.NextDouble() * 2f - 1f) * displacement;
    }

    private void NormalizeHeights(float[,] heights, int resolution)
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float height = heights[y, x];
                if (height < min)
                {
                    min = height;
                }

                if (height > max)
                {
                    max = height;
                }
            }
        }

        float range = max - min;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float normalizedHeight = range > 0f ? (heights[y, x] - min) / range : 0f;
                heights[y, x] = Mathf.Clamp01(normalizedHeight * heightScale);
            }
        }
    }
}
