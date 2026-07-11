using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TileTerrainMaterialSetup
{
    private const string MaterialsFolderPath = "Assets/Materials";
    private const string TerrainMaterialPath = MaterialsFolderPath + "/TerrainBuiltInMaterial.mat";
    private const string TerrainShaderName = "Nature/Terrain/Standard";

    static TileTerrainMaterialSetup()
    {
        EditorApplication.delayCall += EnsureMaterialAndAssignToRunners;
        EditorApplication.hierarchyChanged += EnsureMaterialAndAssignToRunners;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EnsureMaterialAndAssignToRunners();
    }

    private static void EnsureMaterialAndAssignToRunners()
    {
        Material material = EnsureTerrainMaterial();
        if (material == null)
        {
            Debug.LogWarning(
                $"Tile terrain material setup could not create or load {TerrainMaterialPath}. Runtime terrain will keep its default material.");
            return;
        }

        AssignMaterialToOpenSceneRunners(material);
    }

    private static Material EnsureTerrainMaterial()
    {
        if (!AssetDatabase.IsValidFolder(MaterialsFolderPath))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
        if (existingMaterial != null)
        {
            return existingMaterial;
        }

        Shader terrainShader = Shader.Find(TerrainShaderName);
        if (terrainShader == null)
        {
            Debug.LogWarning(
                $"Could not find Built-in Terrain shader '{TerrainShaderName}'. " +
                "TerrainBuiltInMaterial.mat was not created. Check Built-in shader availability for this Unity version.");
            return null;
        }

        Material material = new Material(terrainShader)
        {
            name = "TerrainBuiltInMaterial"
        };

        AssetDatabase.CreateAsset(material, TerrainMaterialPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Created Built-in terrain material at {TerrainMaterialPath} using shader '{TerrainShaderName}'.");
        return AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
    }

    private static void AssignMaterialToOpenSceneRunners(Material material)
    {
        TileGenerationExperimentRunner[] runners = UnityEngine.Object.FindObjectsByType<TileGenerationExperimentRunner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        bool changedAnyScene = false;

        for (int i = 0; i < runners.Length; i++)
        {
            SerializedObject serializedRunner = new SerializedObject(runners[i]);
            SerializedProperty terrainMaterialProperty = serializedRunner.FindProperty("terrainMaterial");

            if (terrainMaterialProperty == null || terrainMaterialProperty.objectReferenceValue != null)
            {
                continue;
            }

            terrainMaterialProperty.objectReferenceValue = material;
            serializedRunner.ApplyModifiedProperties();
            EditorUtility.SetDirty(runners[i]);
            EditorSceneManager.MarkSceneDirty(runners[i].gameObject.scene);
            changedAnyScene = true;
        }

        if (changedAnyScene)
        {
            Debug.Log($"Assigned {TerrainMaterialPath} to TileGenerationExperimentRunner terrainMaterial fields in open scenes.");
        }
    }
}
