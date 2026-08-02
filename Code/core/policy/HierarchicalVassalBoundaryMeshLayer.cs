using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AncientWarfare3.core.policy
{
    /// <summary>
    /// Main-thread presentation owner for hierarchical boundary drafts.
    /// Task 11 can feed this layer through Submit; until then the facade keeps
    /// the queue empty and no legacy per-edge path is used.
    /// </summary>
    internal static class HierarchicalVassalBoundaryMeshLayer
    {
        private const int HeightDimension = 36;
        private const int MaximumUploadsPerFrame = 2;
        private const int MaximumPendingCompletions = 8;
        private const int MaximumRetries = 2;
        private const float FillZ = -0.20f;
        private const float BoundaryZ = -0.18f;

        private static readonly Dictionary<RenderKey, RenderPair> Pairs =
            new Dictionary<RenderKey, RenderPair>();
        private static readonly Dictionary<BoundaryChunkKey, HeightResource>
            Heights = new Dictionary<BoundaryChunkKey, HeightResource>();
        private static readonly Dictionary<RenderKey, long> AcceptedRevisions =
            new Dictionary<RenderKey, long>();
        private static readonly Dictionary<RenderKey, int> RetryCounts =
            new Dictionary<RenderKey, int>();
        private static readonly Queue<BoundaryWorkerCompletion> Pending =
            new Queue<BoundaryWorkerCompletion>();

        private static readonly int HeightTexId =
            Shader.PropertyToID("_HeightTex");
        private static readonly int HeightTransformId =
            Shader.PropertyToID("_HeightUvScaleOffset");
        private static readonly int CameraWorldPerPixelId =
            Shader.PropertyToID("_CameraWorldPerPixel");
        private static readonly int LeftColorId =
            Shader.PropertyToID("_LeftColor");
        private static readonly int RightColorId =
            Shader.PropertyToID("_RightColor");

        private static GameObject _fillRoot;
        private static GameObject _boundaryRoot;
        private static long _worldGeneration = long.MinValue;
        private static float _cameraWorldPerPixel = float.NaN;
        private static float _cameraOrthographicSize = float.NaN;
        private static int _cameraPixelHeight = -1;
        private static bool _minimapHidden;
        private static bool _warningWritten;

        internal static int MaximumUploads { get { return MaximumUploadsPerFrame; } }

        internal static bool Submit(BoundaryWorkerCompletion pCompletion)
        {
            return TryAcceptCompletion(pCompletion);
        }

        internal static bool TryAcceptCompletion(
            BoundaryWorkerCompletion pCompletion)
        {
            if (pCompletion == null) return false;
            if (_worldGeneration == long.MinValue)
                _worldGeneration = pCompletion.WorldGeneration;
            if (pCompletion.WorldGeneration != _worldGeneration)
                return false;

            var key = new RenderKey(pCompletion.ChunkKey, pCompletion.Layer);
            if (AcceptedRevisions.TryGetValue(key, out long latest) &&
                pCompletion.Revision <= latest)
                return false;
            if (Pending.Count >= MaximumPendingCompletions)
            {
                WarnOnce("completion queue saturated; requesting rescan");
                return false;
            }

            AcceptedRevisions[key] = pCompletion.Revision;
            RetryCounts.Remove(key);
            Pending.Enqueue(pCompletion);
            return true;
        }

        internal static int Drain()
        {
            int uploads = 0;
            while (uploads < MaximumUploadsPerFrame && Pending.Count > 0)
            {
                BoundaryWorkerCompletion completion = Pending.Dequeue();
                var key = new RenderKey(completion.ChunkKey, completion.Layer);
                if (!IsCurrent(completion, key)) continue;
                if (completion.IsFailure)
                {
                    WarnOnce("topology completion failed for " + key + ": " +
                        completion.FailureReason);
                    RetryOrDrop(completion, key);
                    uploads++;
                    continue;
                }

                try
                {
                    if (Upload(completion, key))
                    {
                        RetryCounts.Remove(key);
                    }
                    else
                    {
                        RetryOrDrop(completion, key);
                    }
                }
                catch (Exception error)
                {
                    WarnOnce("boundary mesh upload failed for " + key + ": " +
                        BoundedMessage(error));
                    RetryOrDrop(completion, key);
                }
                uploads++;
            }
            UpdateCameraWorldPerPixel();
            return uploads;
        }

        internal static void ProcessFrame()
        {
            try
            {
                if (!Config.game_loaded ||
                    !HierarchicalVassalMapModeService.IsActive())
                {
                    SetRootsActive(false, false);
                    return;
                }

                EnsureRoots();
                SetRootsActive(true, !_minimapHidden);
                Drain();
            }
            catch (Exception error)
            {
                WarnOnce("boundary mesh frame failed: " + BoundedMessage(error));
                SetRootsActive(false, false);
            }
        }

        internal static void ResetWorld(long pWorldGeneration)
        {
            if (pWorldGeneration < 0L)
                throw new ArgumentOutOfRangeException(nameof(pWorldGeneration));
            _worldGeneration = pWorldGeneration;
            AcceptedRevisions.Clear();
            RetryCounts.Clear();
            Pending.Clear();
            DestroyResources();
        }

        internal static void Reset()
        {
            Pending.Clear();
            AcceptedRevisions.Clear();
            RetryCounts.Clear();
            // Keep the last generation token so completions from the world
            // being torn down cannot become valid merely because roots reset.
            // The next world load publishes its explicit token via ResetWorld.
            _cameraWorldPerPixel = float.NaN;
            _cameraOrthographicSize = float.NaN;
            _cameraPixelHeight = -1;
            _minimapHidden = false;
            DestroyResources();
            HierarchicalVassalBoundaryMaterialLibrary.Reset();
        }

        internal static void SetMinimapHidden(bool pHidden)
        {
            _minimapHidden = pHidden;
            // Fill remains visible during the minimap capture. Only boundary
            // roots are hidden so political areas still contribute to the map.
            SetRootActive(_boundaryRoot, !pHidden &&
                Config.game_loaded &&
                HierarchicalVassalMapModeService.IsActive());
        }

        private static bool IsCurrent(BoundaryWorkerCompletion pCompletion,
            RenderKey pKey)
        {
            return pCompletion.WorldGeneration == _worldGeneration &&
                AcceptedRevisions.TryGetValue(pKey, out long latest) &&
                latest == pCompletion.Revision;
        }

        private static void RetryOrDrop(BoundaryWorkerCompletion pCompletion,
            RenderKey pKey)
        {
            int retries = RetryCounts.TryGetValue(pKey, out int value)
                ? value : 0;
            if (retries >= MaximumRetries)
            {
                RetryCounts.Remove(pKey);
                return;
            }
            RetryCounts[pKey] = retries + 1;
            if (Pending.Count < MaximumPendingCompletions)
                Pending.Enqueue(pCompletion);
            else
                WarnOnce("boundary retry queue saturated for " + pKey);
        }

        private static bool Upload(BoundaryWorkerCompletion pCompletion,
            RenderKey pKey)
        {
            BoundaryChunkDraftSet drafts = pCompletion.Draft;
            if (drafts == null) return false;
            BoundaryHeightDraft heightDraft = drafts.CountryHeightDraft;
            HeightResource height = GetHeight(pKey.ChunkKey);
            height.TryGetHeight(heightDraft, out Texture2D texture,
                out Vector4 uvTransform);

            RenderPair pair = GetPair(pKey);
            BoundaryMeshDraft fill = pKey.Layer == BoundaryDisplayLayer.Cities
                ? drafts.CityFill : drafts.CountryFill;
            BoundaryMeshDraft boundary = pKey.Layer == BoundaryDisplayLayer.Cities
                ? drafts.CityRibbons : drafts.CountryRibbons;
            if (!UploadFill(pair, fill)) return false;
            if (!UploadBoundary(pair, boundary)) return false;
            ApplyHeight(pair, height, texture, uvTransform);
            pair.FillRenderer.enabled = fill != null;
            pair.BoundaryRenderer.enabled = boundary != null;
            return true;
        }

        private static bool UploadFill(RenderPair pPair,
            BoundaryMeshDraft pDraft)
        {
            pPair.FillVertices.Clear();
            pPair.FillColors.Clear();
            pPair.FillUvs.Clear();
            pPair.FillIndices.Clear();
            if (pDraft == null) return true;
            int count = pDraft.VertexCount;
            if (!HasLength(pDraft.PositionY, count) ||
                !HasLength(pDraft.LeftRgba, count)) return false;
            for (int i = 0; i < count; i++)
            {
                pPair.FillVertices.Add(new Vector3(
                    pDraft.PositionX[i], pDraft.PositionY[i], FillZ));
                pPair.FillColors.Add(ToColor32(pDraft.LeftRgba[i]));
                pPair.FillUvs.Add(new Vector2(
                    pDraft.PositionX[i], pDraft.PositionY[i]));
            }
            AddIndices(pPair.FillIndices, pDraft.CityIndices, count);
            AddIndices(pPair.FillIndices, pDraft.VassalRealmIndices, count);
            AddIndices(pPair.FillIndices, pDraft.SuzerainSystemIndices, count);
            try
            {
                pPair.FillMesh.Clear(false);
                pPair.FillMesh.SetVertices(pPair.FillVertices);
                pPair.FillMesh.SetColors(pPair.FillColors);
                pPair.FillMesh.SetUVs(0, pPair.FillUvs);
                pPair.FillMesh.SetTriangles(pPair.FillIndices, 0, false);
                pPair.FillMesh.RecalculateBounds();
                pPair.HasFill = true;
                return true;
            }
            catch { return pPair.HasFill; }
        }

        private static bool UploadBoundary(RenderPair pPair,
            BoundaryMeshDraft pDraft)
        {
            pPair.BoundaryVertices.Clear();
            pPair.BoundaryColors.Clear();
            pPair.BoundaryUv0.Clear();
            pPair.BoundaryUv1.Clear();
            pPair.BoundaryIndices.Clear();
            if (pDraft == null) return true;
            int count = pDraft.VertexCount;
            if (!HasLength(pDraft.PositionY, count) ||
                !HasLength(pDraft.NormalX, count) ||
                !HasLength(pDraft.NormalY, count) ||
                !HasLength(pDraft.SignedDistance, count) ||
                !HasLength(pDraft.Tiers, count) ||
                !HasLength(pDraft.Flags, count) ||
                !HasLength(pDraft.PoliticalAlpha, count) ||
                !HasLength(pDraft.LeftRgba, count) ||
                !HasLength(pDraft.RightRgba, count) ||
                !HasLength(pDraft.LocalHalfWidths, count)) return false;
            for (int i = 0; i < count; i++)
            {
                pPair.BoundaryVertices.Add(new Vector3(
                    pDraft.PositionX[i], pDraft.PositionY[i], BoundaryZ));
                Color32 left = ToColor32(pDraft.LeftRgba[i]);
                left.a = pDraft.PoliticalAlpha[i];
                pPair.BoundaryColors.Add(left);
                pPair.BoundaryUv0.Add(new Vector2(
                    pDraft.SignedDistance[i], pDraft.LocalHalfWidths[i]));
                float coast = (pDraft.Flags[i] &
                    (byte)BoundaryRibbonFlags.Coast) != 0 ? 1f : 0f;
                pPair.BoundaryUv1.Add(new Vector4(
                    pDraft.Tiers[i], coast,
                    pDraft.PositionX[i], pDraft.PositionY[i]));
            }
            AddIndices(pPair.BoundaryIndices, pDraft.CityIndices, count);
            AddIndices(pPair.BoundaryIndices, pDraft.VassalRealmIndices, count);
            AddIndices(pPair.BoundaryIndices, pDraft.SuzerainSystemIndices, count);
            try
            {
                pPair.BoundaryMesh.Clear(false);
                pPair.BoundaryMesh.SetVertices(pPair.BoundaryVertices);
                pPair.BoundaryMesh.SetColors(pPair.BoundaryColors);
                pPair.BoundaryMesh.SetUVs(0, pPair.BoundaryUv0);
                pPair.BoundaryMesh.SetUVs(1, pPair.BoundaryUv1);
                pPair.BoundaryMesh.SetTriangles(
                    pPair.BoundaryIndices, 0, false);
                pPair.BoundaryMesh.RecalculateBounds();
                pPair.HasBoundary = true;
                if (count > 0)
                {
                    pPair.LeftColor = ToColor(pDraft.LeftRgba[0]);
                    pPair.RightColor = ToColor(pDraft.RightRgba[0]);
                }
                return true;
            }
            catch { return pPair.HasBoundary; }
        }

        private static void ApplyHeight(RenderPair pPair,
            HeightResource pHeight, Texture2D pTexture, Vector4 pUvTransform)
        {
            MaterialPropertyBlock block = pHeight.Block;
            block.Clear();
            block.SetTexture(HeightTexId, pTexture);
            block.SetVector(HeightTransformId, pUvTransform);
            pPair.FillRenderer.SetPropertyBlock(block);
            block.SetColor(LeftColorId, pPair.LeftColor);
            block.SetColor(RightColorId, pPair.RightColor);
            pPair.BoundaryRenderer.SetPropertyBlock(block);
        }

        private static void UpdateCameraWorldPerPixel()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                try { camera = MoveCamera.instance?.main_camera; }
                catch { camera = null; }
            }
            float value = 0.01f;
            float orthographicSize = float.NaN;
            int pixelHeight = 0;
            if (camera != null && camera.orthographic)
            {
                orthographicSize = camera.orthographicSize;
                pixelHeight = camera.pixelHeight;
                value = 2f * camera.orthographicSize /
                    Mathf.Max(1, camera.pixelHeight);
            }
            bool cameraScaleChanged =
                float.IsNaN(_cameraOrthographicSize) ||
                _cameraOrthographicSize != orthographicSize ||
                _cameraPixelHeight != pixelHeight;
            if (float.IsNaN(_cameraWorldPerPixel) ||
                cameraScaleChanged ||
                Mathf.Abs(value - _cameraWorldPerPixel) > 0.000001f)
            {
                _cameraWorldPerPixel = value;
                _cameraOrthographicSize = orthographicSize;
                _cameraPixelHeight = pixelHeight;
                foreach (RenderPair pair in Pairs.Values)
                    pair.SetCameraWorldPerPixel(value);
            }
        }

        private static HeightResource GetHeight(BoundaryChunkKey pKey)
        {
            if (!Heights.TryGetValue(pKey, out HeightResource height))
            {
                height = new HeightResource();
                Heights.Add(pKey, height);
            }
            return height;
        }

        private static RenderPair GetPair(RenderKey pKey)
        {
            if (Pairs.TryGetValue(pKey, out RenderPair pair)) return pair;
            EnsureRoots();
            HeightResource height = GetHeight(pKey.ChunkKey);
            pair = new RenderPair(pKey, _fillRoot.transform,
                _boundaryRoot.transform, height);
            Pairs.Add(pKey, pair);
            return pair;
        }

        private static void EnsureRoots()
        {
            if (_fillRoot != null && _boundaryRoot != null) return;
            _fillRoot = new GameObject("AW3_HierarchicalVassalBoundary_Fill");
            _boundaryRoot = new GameObject(
                "AW3_HierarchicalVassalBoundary_Boundary");
            if (World.world != null)
            {
                _fillRoot.transform.SetParent(World.world.transform, false);
                _boundaryRoot.transform.SetParent(World.world.transform, false);
            }
        }

        private static void SetRootsActive(bool pFill, bool pBoundary)
        {
            SetRootActive(_fillRoot, pFill);
            SetRootActive(_boundaryRoot, pBoundary);
        }

        private static void SetRootActive(GameObject pRoot, bool pActive)
        {
            if (pRoot != null && pRoot.activeSelf != pActive)
                pRoot.SetActive(pActive);
        }

        private static void DestroyResources()
        {
            foreach (RenderPair pair in Pairs.Values) pair.Destroy();
            Pairs.Clear();
            foreach (HeightResource height in Heights.Values) height.Destroy();
            Heights.Clear();
            DestroyObject(ref _fillRoot);
            DestroyObject(ref _boundaryRoot);
        }

        private static void AddIndices(List<int> pTarget, int[] pSource,
            int pVertexCount)
        {
            if (pSource == null) return;
            for (int i = 0; i < pSource.Length; i++)
            {
                int value = pSource[i];
                if (value >= 0 && value < pVertexCount) pTarget.Add(value);
            }
        }

        private static bool HasLength<T>(T[] pValues, int pLength)
        {
            return pValues != null && pValues.Length >= pLength;
        }

        private static Color32 ToColor32(uint pRgba)
        {
            return new Color32((byte)(pRgba >> 24), (byte)(pRgba >> 16),
                (byte)(pRgba >> 8), (byte)pRgba);
        }

        private static Color ToColor(uint pRgba)
        {
            Color32 color = ToColor32(pRgba);
            return color;
        }

        private static void WarnOnce(string pMessage)
        {
            if (_warningWritten) return;
            _warningWritten = true;
            try { ModClass.LogWarning("[AW3 hierarchical boundary] " + pMessage); }
            catch { }
        }

        private static string BoundedMessage(Exception pError)
        {
            string message = pError?.Message ?? "unknown error";
            return message.Length <= 256 ? message : message.Substring(0, 256);
        }

        private static void DestroyObject<T>(ref T pObject) where T : UnityEngine.Object
        {
            if (pObject == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(pObject);
            else UnityEngine.Object.DestroyImmediate(pObject);
            pObject = null;
        }

        private readonly struct RenderKey : IEquatable<RenderKey>
        {
            internal RenderKey(BoundaryChunkKey pChunkKey,
                BoundaryDisplayLayer pLayer)
            {
                ChunkKey = pChunkKey;
                Layer = pLayer;
            }

            internal BoundaryChunkKey ChunkKey { get; }
            internal BoundaryDisplayLayer Layer { get; }

            public bool Equals(RenderKey pOther)
            {
                return ChunkKey.Equals(pOther.ChunkKey) && Layer == pOther.Layer;
            }

            public override bool Equals(object pValue)
            {
                return pValue is RenderKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return unchecked((ChunkKey.GetHashCode() * 397) ^
                    (int)Layer);
            }

            public override string ToString()
            {
                return ChunkKey + "/" + Layer;
            }
        }

        private sealed class RenderPair
        {
            internal GameObject FillObject;
            internal GameObject BoundaryObject;
            internal MeshFilter FillFilter;
            internal MeshFilter BoundaryFilter;
            internal MeshRenderer FillRenderer;
            internal MeshRenderer BoundaryRenderer;
            internal Mesh FillMesh;
            internal Mesh BoundaryMesh;
            internal readonly List<Vector3> FillVertices =
                new List<Vector3>();
            internal readonly List<Color32> FillColors =
                new List<Color32>();
            internal readonly List<Vector2> FillUvs = new List<Vector2>();
            internal readonly List<int> FillIndices = new List<int>();
            internal readonly List<Vector3> BoundaryVertices =
                new List<Vector3>();
            internal readonly List<Color32> BoundaryColors =
                new List<Color32>();
            internal readonly List<Vector2> BoundaryUv0 =
                new List<Vector2>();
            internal readonly List<Vector4> BoundaryUv1 =
                new List<Vector4>();
            internal readonly List<int> BoundaryIndices = new List<int>();
            internal bool HasFill;
            internal bool HasBoundary;
            internal Color LeftColor = Color.white;
            internal Color RightColor = Color.white;
            internal readonly HeightResource Height;

            internal RenderPair(RenderKey pKey, Transform pFillParent,
                Transform pBoundaryParent, HeightResource pHeight)
            {
                Height = pHeight;
                string suffix = pKey.ChunkKey.X + "_" + pKey.ChunkKey.Y + "_" +
                    (int)pKey.Layer;
                FillObject = new GameObject("AW3_BoundaryChunk_Fill_" + suffix);
                BoundaryObject = new GameObject(
                    "AW3_BoundaryChunk_Boundary_" + suffix);
                FillObject.transform.SetParent(pFillParent, false);
                BoundaryObject.transform.SetParent(pBoundaryParent, false);
                FillFilter = FillObject.AddComponent<MeshFilter>();
                FillRenderer = FillObject.AddComponent<MeshRenderer>();
                BoundaryFilter = BoundaryObject.AddComponent<MeshFilter>();
                BoundaryRenderer = BoundaryObject.AddComponent<MeshRenderer>();
                FillMesh = new Mesh { name = "AW3_BoundaryFill_" + suffix };
                BoundaryMesh = new Mesh {
                    name = "AW3_BoundaryRibbon_" + suffix
                };
                FillMesh.indexFormat = IndexFormat.UInt32;
                BoundaryMesh.indexFormat = IndexFormat.UInt32;
                FillMesh.MarkDynamic();
                BoundaryMesh.MarkDynamic();
                FillFilter.sharedMesh = FillMesh;
                BoundaryFilter.sharedMesh = BoundaryMesh;
                FillRenderer.sharedMaterial =
                    HierarchicalVassalBoundaryMaterialLibrary.SharedFill;
                BoundaryRenderer.sharedMaterial =
                    HierarchicalVassalBoundaryMaterialLibrary.ForTier(
                        pKey.Layer == BoundaryDisplayLayer.Cities
                            ? BoundaryTier.City : BoundaryTier.VassalRealm);
            }

            internal void SetCameraWorldPerPixel(float pValue)
            {
                Height.Block.SetFloat(CameraWorldPerPixelId, pValue);
                // The block is shared by both layer entries for this chunk;
                // restore this pair's side colors before copying it to the
                // renderer so a city update cannot recolor the country mesh.
                Height.Block.SetColor(LeftColorId, LeftColor);
                Height.Block.SetColor(RightColorId, RightColor);
                BoundaryRenderer.SetPropertyBlock(Height.Block);
            }

            internal void Destroy()
            {
                DestroyObject(ref FillMesh);
                DestroyObject(ref BoundaryMesh);
                DestroyObject(ref FillObject);
                DestroyObject(ref BoundaryObject);
            }
        }

        private sealed class HeightResource
        {
            private Texture2D _texture;
            private Texture2D _boundTexture;
            private byte[] _uploadBuffer;
            private long _revision = long.MinValue;
            internal readonly MaterialPropertyBlock Block =
                new MaterialPropertyBlock();

            internal HeightResource()
            {
                try
                {
                    _texture = CreateHeightTexture(HeightDimension,
                        HeightDimension, TextureFormat.R8);
                    if (_texture == null)
                        _texture = CreateHeightTexture(HeightDimension,
                            HeightDimension, TextureFormat.Alpha8);
                    if (_texture == null)
                        throw new InvalidOperationException(
                            "no supported single-channel height format");
                    _uploadBuffer = new byte[HeightDimension * HeightDimension];
                    for (int i = 0; i < _uploadBuffer.Length; i++)
                        _uploadBuffer[i] = 128;
                    _texture.LoadRawTextureData(_uploadBuffer);
                    _texture.Apply(false, false);
                    _boundTexture = _texture;
                }
                catch
                {
                    _texture = null;
                    _boundTexture = NeutralTexture();
                }
            }

            internal bool TryGetHeight(BoundaryHeightDraft pDraft,
                out Texture2D pTexture, out Vector4 pUvTransform)
            {
                pTexture = _boundTexture ?? NeutralTexture();
                pUvTransform = Vector4.one;
                if (pDraft == null) return false;
                pUvTransform = new Vector4(
                    1f / Mathf.Max(1, pDraft.Width),
                    1f / Mathf.Max(1, pDraft.Height),
                    -pDraft.CaptureWorldOriginX /
                        (float)Mathf.Max(1, pDraft.Width),
                    -pDraft.CaptureWorldOriginY /
                        (float)Mathf.Max(1, pDraft.Height));
                if (pDraft.TerrainRevision == _revision)
                {
                    pTexture = _boundTexture ?? NeutralTexture();
                    return true;
                }
                if (_texture == null || _uploadBuffer == null)
                {
                    pTexture = NeutralTexture();
                    _boundTexture = pTexture;
                    _revision = pDraft.TerrainRevision;
                    return false;
                }
                try
                {
                    Array.Clear(_uploadBuffer, 0, _uploadBuffer.Length);
                    int width = Mathf.Min(HeightDimension, pDraft.Width);
                    int height = Mathf.Min(HeightDimension, pDraft.Height);
                    for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        _uploadBuffer[y * HeightDimension + x] =
                            pDraft.Samples[pDraft.Index(x, y)];
                    }
                    _texture.LoadRawTextureData(_uploadBuffer);
                    _texture.Apply(false, false);
                    _boundTexture = _texture;
                    _revision = pDraft.TerrainRevision;
                    pTexture = _boundTexture;
                    return true;
                }
                catch
                {
                    _boundTexture = NeutralTexture();
                    _revision = pDraft.TerrainRevision;
                    pTexture = _boundTexture;
                    return false;
                }
            }

            internal void Destroy()
            {
                DestroyObject(ref _texture);
                _boundTexture = null;
                _uploadBuffer = null;
            }

            private static Texture2D _neutral;

            private static Texture2D NeutralTexture()
            {
                if (_neutral != null) return _neutral;
                try
                {
                    _neutral = CreateHeightTexture(1, 1,
                        TextureFormat.R8);
                    if (_neutral == null)
                        _neutral = CreateHeightTexture(1, 1,
                            TextureFormat.Alpha8);
                    if (_neutral == null) return null;
                    _neutral.LoadRawTextureData(new byte[] { 128 });
                    _neutral.Apply(false, false);
                }
                catch { }
                return _neutral;
            }

            private static Texture2D CreateHeightTexture(int pWidth,
                int pHeight, TextureFormat pFormat)
            {
                try
                {
                    Texture2D texture = new Texture2D(pWidth, pHeight,
                        pFormat, false, true)
                    {
                        name = pWidth == 1
                            ? "AW3_HierarchicalVassal_Height_Neutral"
                            : "AW3_HierarchicalVassal_Height_36x36",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.DontSave
                    };
                    return texture;
                }
                catch { return null; }
            }
        }
    }
}
