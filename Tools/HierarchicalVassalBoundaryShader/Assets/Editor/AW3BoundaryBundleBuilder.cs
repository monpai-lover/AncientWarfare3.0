#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class AW3BoundaryBundleBuilder
{
    private const string BundleName =
        "aw3_hierarchical_vassal_boundary";
    private const string ModOutputFolder = "GameResources/assetbundles";
    private const string FillShaderName = "AW3/HierarchicalVassal/Fill";
    private const string BoundaryShaderName =
        "AW3/HierarchicalVassal/Boundary";
    private const string FillShaderAsset =
        "Assets/Shaders/AW3HierarchicalVassalFill.shader";
    private const string BoundaryShaderAsset =
        "Assets/Shaders/AW3HierarchicalVassalBoundary.shader";

    [MenuItem("AW3/Build Hierarchical Vassal Boundary (Windows)")]
    public static void BuildWindows()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputDirectory = Path.GetFullPath(Path.Combine(
            projectRoot, "..", "..",
            ModOutputFolder.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(outputDirectory);

        Shader fill = AssetDatabase.LoadAssetAtPath<Shader>(FillShaderAsset);
        Shader boundary =
            AssetDatabase.LoadAssetAtPath<Shader>(BoundaryShaderAsset);
        if (fill == null || fill.name != FillShaderName ||
            boundary == null || boundary.name != BoundaryShaderName)
            throw new InvalidOperationException(
                "AW3 boundary shader assets are missing or renamed.");

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[] { FillShaderAsset, BoundaryShaderAsset }
        };
        BuildAssetBundleOptions options =
            BuildAssetBundleOptions.ChunkBasedCompression;
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            outputDirectory, new[] { build }, options,
            BuildTarget.StandaloneWindows64);
        if (manifest == null)
            throw new InvalidOperationException(
                "AW3 boundary AssetBundle build returned no manifest.");

        AssetDatabase.Refresh();
        Debug.Log("AW3 hierarchical vassal boundary bundle built: " +
            Path.Combine(outputDirectory, BundleName));
    }
}
#endif
