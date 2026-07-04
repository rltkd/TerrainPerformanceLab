public interface IHeightmapGenerator
{
    string AlgorithmName { get; }
    float[,] Generate(int resolution, int seed);
}
