using System;
using System.IO;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    /// <summary>
    /// Owns the four materials shared by all hierarchical boundary meshes.
    /// A material is selected by tier, never cloned per chunk.
    /// </summary>
    internal static class HierarchicalVassalBoundaryMaterialLibrary
    {
        private const string BundleName =
            "aw3_hierarchical_vassal_boundary";
        private const string FillShaderAsset =
            "AW3/HierarchicalVassal/Fill";
        private const string BoundaryShaderAsset =
            "AW3/HierarchicalVassal/Boundary";
        private const string FillShaderPath =
            "Assets/Shaders/AW3HierarchicalVassalFill.shader";
        private const string BoundaryShaderPath =
            "Assets/Shaders/AW3HierarchicalVassalBoundary.shader";
        private static readonly Material[] BoundaryMaterials =
            new Material[3];
        private static bool _initialized;
        private static bool _warningWritten;
        private static Material _fillMaterial;

        internal static Material SharedFill
        {
            get
            {
                EnsureInitialized();
                return _fillMaterial;
            }
        }

        internal static Material ForTier(BoundaryTier pTier)
        {
            EnsureInitialized();
            int index = pTier == BoundaryTier.City ? 0 :
                pTier == BoundaryTier.VassalRealm ? 1 :
                pTier == BoundaryTier.SuzerainSystem ? 2 : -1;
            return index < 0 ? null : BoundaryMaterials[index];
        }

        internal static void Reset()
        {
            DestroyMaterial(ref _fillMaterial);
            for (int i = 0; i < BoundaryMaterials.Length; i++)
                DestroyMaterial(ref BoundaryMaterials[i]);
            _initialized = false;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            Shader fillShader = null;
            Shader boundaryShader = null;
            AssetBundle bundle = null;
            string bundlePath = string.Empty;
            try
            {
                if (ModClass.Instance == null ||
                    ModClass.Instance.GetDeclaration() == null)
                    throw new InvalidOperationException(
                        "mod declaration unavailable");

                string modFolder =
                    ModClass.Instance.GetDeclaration().FolderPath;
                bundlePath = Path.Combine(modFolder, "GameResources",
                    "assetbundles", BundleName);
                if (!File.Exists(bundlePath))
                    throw new FileNotFoundException(
                        "boundary bundle missing", bundlePath);

                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                    throw new InvalidOperationException(
                        "AssetBundle.LoadFromFile returned null");

                fillShader = LoadShader(bundle, FillShaderPath,
                    FillShaderAsset);
                boundaryShader = LoadShader(bundle, BoundaryShaderPath,
                    BoundaryShaderAsset);
                if (fillShader == null || boundaryShader == null)
                    throw new InvalidOperationException(
                        "boundary shader asset missing");
            }
            catch (Exception error)
            {
                LogFailureOnce(error.Message, bundlePath);
                fillShader = null;
                boundaryShader = null;
            }
            finally
            {
                // Keep shader objects resident; unloading true would destroy
                // them and invalidate every shared material.
                if (bundle != null) bundle.Unload(false);
            }

            if (fillShader == null || boundaryShader == null)
            {
                Shader fallback = Shader.Find("Sprites/Default");
                if (fallback == null)
                {
                    LogFailureOnce("Sprites/Default shader unavailable",
                        bundlePath);
                    return;
                }
                fillShader = fallback;
                boundaryShader = fallback;
            }

            _fillMaterial = new Material(fillShader)
            {
                name = "AW3_HierarchicalVassal_Fill_Shared"
            };
            for (int i = 0; i < BoundaryMaterials.Length; i++)
            {
                BoundaryMaterials[i] = new Material(boundaryShader)
                {
                    name = "AW3_HierarchicalVassal_Boundary_" + i
                };
            }

            // Sprites/Default has no relief property, but SetFloat is a
            // harmless no-op there and explicitly disables fallback relief.
            _fillMaterial.SetFloat("_ReliefStrength", 0f);
        }

        private static void LogFailureOnce(string pReason, string pPath)
        {
            if (_warningWritten) return;
            _warningWritten = true;
            ModClass.LogWarning("[AW3 hierarchical boundary] shader " +
                "bundle fallback path=" + pPath + " reason=" + pReason);
        }

        private static Shader LoadShader(AssetBundle pBundle,
            string pAssetPath, string pShaderName)
        {
            Shader shader = pBundle.LoadAsset<Shader>(pAssetPath);
            return shader ?? pBundle.LoadAsset<Shader>(pShaderName);
        }

        private static void DestroyMaterial(ref Material pMaterial)
        {
            if (pMaterial == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(pMaterial);
            else UnityEngine.Object.DestroyImmediate(pMaterial);
            pMaterial = null;
        }
    }
}
