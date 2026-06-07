using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class TerrainManager : MonoBehaviour
{
    [Header("Source")]
    [FormerlySerializedAs("sourceTexture")]
    [SerializeField] private Texture2D visualSourceTexture;
    [SerializeField] private Texture2D collisionMaskTexture;
    [SerializeField] private SpriteRenderer terrainRenderer;
    [SerializeField] private Transform chunkRoot;

    [Header("Coordinates")]
    [SerializeField] private Vector2 terrainOriginWorld = Vector2.zero;
    [SerializeField] private int pixelsPerUnit = 32;
    [SerializeField, Range(1f, 2.5f)] private float horizontalExpansion = 1f;

    [Header("Collision")]
    [SerializeField] private int chunkSizePx = 64;
    [SerializeField] private int collisionCellSizePx = 4;
    [SerializeField, Range(0f, 1f)] private float collisionSolidRatioThreshold = 0.4f;
    [FormerlySerializedAs("alphaThreshold")]
    [SerializeField, Range(0f, 1f)] private float maskAlphaThreshold = 0.1f;
    [SerializeField] private bool mergeVisibleTerrainIntoCollision = true;
    [SerializeField] private bool buildCollidersOnStart = true;
    [SerializeField, Min(0f)] private float characterBuildClearanceWorld = 0.08f;

    [Header("Terrain Rules")]
    [SerializeField] private bool useIndestructibleTerrain;
    [SerializeField] private Color baseTerrainColor = new Color32(126, 82, 45, 255);
    [SerializeField] private Color createdTerrainColor = new Color32(105, 190, 84, 255);
    [SerializeField] private Color indestructibleTerrainColor = new Color32(118, 122, 128, 255);

    [Header("Debug")]
    [SerializeField] private bool drawChunkGizmos = true;

    private Texture2D runtimeVisualTexture;
    private Texture2D runtimeCollisionTexture;
    private Sprite runtimeSprite;
    private bool[,] solidMask;
    private TerrainType[,] terrainTypeMask;
    private TerrainChunk[,] chunks;
    private readonly HashSet<TerrainChunk> dirtyChunks = new HashSet<TerrainChunk>();
    private bool initialized;

    public event Action TerrainChanged;

    public int WidthPx => runtimeVisualTexture != null
        ? runtimeVisualTexture.width
        : visualSourceTexture != null ? visualSourceTexture.width : 0;
    public int HeightPx => runtimeVisualTexture != null
        ? runtimeVisualTexture.height
        : visualSourceTexture != null ? visualSourceTexture.height : 0;
    public int PixelsPerUnit => Mathf.Max(1, pixelsPerUnit);
    public float HorizontalExpansion => Mathf.Max(1f, horizontalExpansion);
    public Vector2 TerrainOriginWorld => terrainOriginWorld;
    public bool IsInitialized => initialized;
    public Texture2D RuntimeVisualTexture => runtimeVisualTexture;
    public Texture2D RuntimeCollisionTexture => runtimeCollisionTexture;

    public Bounds GetTerrainBounds()
    {
        float width = WidthPx / (float)PixelsPerUnit;
        float height = HeightPx / (float)PixelsPerUnit;
        Vector3 center = new Vector3(
            terrainOriginWorld.x + width * 0.5f,
            terrainOriginWorld.y + height * 0.5f,
            0f);
        return new Bounds(center, new Vector3(width, height, 0f));
    }

    public bool TryGetLowestSolidWorldY(out float lowestWorldY)
    {
        lowestWorldY = terrainOriginWorld.y;
        if (!EnsureInitialized())
        {
            return false;
        }

        for (int y = 0; y < HeightPx; y++)
        {
            for (int x = 0; x < WidthPx; x++)
            {
                if (!solidMask[x, y])
                {
                    continue;
                }

                lowestWorldY = terrainOriginWorld.y + y / (float)PixelsPerUnit;
                return true;
            }
        }

        return false;
    }

    public void SetTerrainOriginWorld(Vector2 originWorld)
    {
        terrainOriginWorld = originWorld;

        if (terrainRenderer != null)
        {
            Vector3 rendererPosition = terrainRenderer.transform.position;
            terrainRenderer.transform.position =
                new Vector3(originWorld.x, originWorld.y, rendererPosition.z);
        }

        if (chunkRoot != null)
        {
            if (terrainRenderer != null && chunkRoot.IsChildOf(terrainRenderer.transform))
            {
                Vector3 localPosition = chunkRoot.localPosition;
                chunkRoot.localPosition = new Vector3(0f, 0f, localPosition.z);
            }
            else
            {
                Vector3 chunkPosition = chunkRoot.position;
                chunkRoot.position = new Vector3(originWorld.x, originWorld.y, chunkPosition.z);
            }
        }
    }

    private void Reset()
    {
        terrainRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        if (!initialized && visualSourceTexture != null)
        {
            InitializeTerrain();
        }
    }

    public void Configure(
        Texture2D terrainTexture,
        SpriteRenderer renderer,
        Transform terrainChunkRoot,
        Vector2 originWorld,
        int ppu,
        int chunkSize,
        int collisionCellSize)
    {
        Configure(terrainTexture, null, renderer, terrainChunkRoot, originWorld, ppu, chunkSize, collisionCellSize);
    }

    public void Configure(
        Texture2D terrainVisualTexture,
        Texture2D terrainCollisionMask,
        SpriteRenderer renderer,
        Transform terrainChunkRoot,
        Vector2 originWorld,
        int ppu,
        int chunkSize,
        int collisionCellSize)
    {
        Configure(
            terrainVisualTexture,
            terrainCollisionMask,
            renderer,
            terrainChunkRoot,
            originWorld,
            ppu,
            chunkSize,
            collisionCellSize,
            1f);
    }

    public void Configure(
        Texture2D terrainVisualTexture,
        Texture2D terrainCollisionMask,
        SpriteRenderer renderer,
        Transform terrainChunkRoot,
        Vector2 originWorld,
        int ppu,
        int chunkSize,
        int collisionCellSize,
        float horizontalScale)
    {
        visualSourceTexture = terrainVisualTexture;
        collisionMaskTexture = terrainCollisionMask;
        terrainRenderer = renderer;
        chunkRoot = terrainChunkRoot;
        terrainOriginWorld = originWorld;
        pixelsPerUnit = Mathf.Max(1, ppu);
        chunkSizePx = Mathf.Max(1, chunkSize);
        collisionCellSizePx = NormalizeCollisionCellSize(collisionCellSize);
        horizontalExpansion = Mathf.Clamp(horizontalScale, 1f, 2.5f);
        InitializeTerrain();
    }

    public void InitializeTerrain()
    {
        if (visualSourceTexture == null)
        {
            Debug.LogError("TerrainManager needs a visual source texture.");
            return;
        }

        Texture2D maskSource = collisionMaskTexture;
        if (maskSource != null &&
            (maskSource.width != visualSourceTexture.width || maskSource.height != visualSourceTexture.height))
        {
            Debug.LogWarning("Collision mask resolution differs from the visual texture. Falling back to visual alpha.");
            maskSource = null;
        }

        if (terrainRenderer == null)
        {
            terrainRenderer = GetComponent<SpriteRenderer>();
        }

        runtimeVisualTexture = CreateHorizontallyExpandedRuntimeTexture(
            visualSourceTexture,
            HorizontalExpansion);
        runtimeVisualTexture.name = visualSourceTexture.name + "_RuntimeVisual";
        runtimeVisualTexture.filterMode = FilterMode.Point;
        runtimeVisualTexture.wrapMode = TextureWrapMode.Clamp;

        runtimeCollisionTexture = CreateHorizontallyExpandedRuntimeTexture(
            maskSource != null ? maskSource : visualSourceTexture,
            HorizontalExpansion);
        runtimeCollisionTexture.name = (maskSource != null ? maskSource.name : visualSourceTexture.name) + "_RuntimeCollision";
        runtimeCollisionTexture.filterMode = FilterMode.Point;
        runtimeCollisionTexture.wrapMode = TextureWrapMode.Clamp;

        BuildMasks();
        ApplyRuntimeSprite();
        BuildChunks();
        initialized = true;

        if (buildCollidersOnStart)
        {
            RebuildAllColliders();
        }
    }

    public void ResetTerrain()
    {
        InitializeTerrain();
    }

    public Vector2Int WorldToPixel(Vector2 worldPosition)
    {
        Vector2 local = worldPosition - terrainOriginWorld;
        return new Vector2Int(
            Mathf.FloorToInt(local.x * PixelsPerUnit),
            Mathf.FloorToInt(local.y * PixelsPerUnit));
    }

    public Vector2 PixelToWorld(Vector2Int pixel)
    {
        return terrainOriginWorld + new Vector2(
            (pixel.x + 0.5f) / PixelsPerUnit,
            (pixel.y + 0.5f) / PixelsPerUnit);
    }

    public bool IsPixelInBounds(Vector2Int pixel)
    {
        return pixel.x >= 0 && pixel.y >= 0 && pixel.x < WidthPx && pixel.y < HeightPx;
    }

    public bool IsSolidWorld(Vector2 worldPosition)
    {
        if (!EnsureInitialized())
        {
            return false;
        }

        Vector2Int pixel = WorldToPixel(worldPosition);
        return IsPixelInBounds(pixel) && solidMask[pixel.x, pixel.y];
    }

    public bool FindTerrainSurface(float worldX, out Vector2 surfaceWorld)
    {
        surfaceWorld = Vector2.zero;
        if (!EnsureInitialized())
        {
            return false;
        }

        Vector2Int pixel = WorldToPixel(new Vector2(worldX, terrainOriginWorld.y));
        if (pixel.x < 0 || pixel.x >= WidthPx)
        {
            return false;
        }

        for (int y = HeightPx - 2; y >= 0; y--)
        {
            Vector2 solidPoint = PixelToWorld(new Vector2Int(pixel.x, y));
            Vector2 abovePoint = PixelToWorld(new Vector2Int(pixel.x, y + 1));
            if (IsSolidWorld(solidPoint) && !IsSolidWorld(abovePoint))
            {
                surfaceWorld = abovePoint;
                return true;
            }
        }

        return false;
    }

    public bool FindValidCharacterSpawn(
        TerrainCharacterSpawnRequest request,
        System.Random random,
        out Vector2 spawnWorld)
    {
        return FindValidCharacterSpawn(request, random, out spawnWorld, out _);
    }

    public bool FindValidCharacterSpawn(
        TerrainCharacterSpawnRequest request,
        System.Random random,
        out Vector2 spawnWorld,
        out bool usedFallback)
    {
        spawnWorld = Vector2.zero;
        usedFallback = false;
        if (!EnsureInitialized() || random == null)
        {
            return false;
        }

        float minX = Mathf.Min(request.minWorldX, request.maxWorldX);
        float maxX = Mathf.Max(request.minWorldX, request.maxWorldX);
        int attempts = Mathf.Max(1, request.randomAttempts);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float x = Mathf.Lerp(minX, maxX, (float)random.NextDouble());
            if (TryBuildCharacterSpawnAtX(request, x, out spawnWorld))
            {
                return true;
            }
        }

        const int scanSteps = 60;
        List<int> shuffledSteps = new List<int>(scanSteps + 1);
        for (int step = 0; step <= scanSteps; step++)
        {
            shuffledSteps.Add(step);
        }

        for (int i = shuffledSteps.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            int value = shuffledSteps[i];
            shuffledSteps[i] = shuffledSteps[swapIndex];
            shuffledSteps[swapIndex] = value;
        }

        for (int index = 0; index < shuffledSteps.Count; index++)
        {
            int step = shuffledSteps[index];
            float x = Mathf.Lerp(minX, maxX, step / (float)scanSteps);
            if (TryBuildCharacterSpawnAtX(request, x, out spawnWorld))
            {
                usedFallback = true;
                return true;
            }
        }

        return false;
    }

    public bool FindValidItemSpawn(
        TerrainItemSpawnRequest request,
        System.Random random,
        out Vector2 spawnWorld)
    {
        spawnWorld = Vector2.zero;
        if (!EnsureInitialized() || random == null)
        {
            return false;
        }

        float minX = Mathf.Min(request.minWorldX, request.maxWorldX);
        float maxX = Mathf.Max(request.minWorldX, request.maxWorldX);
        int attempts = Mathf.Max(1, request.randomAttempts);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float x = Mathf.Lerp(minX, maxX, (float)random.NextDouble());
            if (!FindTerrainSurface(x, out Vector2 surface))
            {
                continue;
            }

            float height = Mathf.Lerp(
                Mathf.Max(0.05f, request.minimumHeightAboveSurface),
                Mathf.Max(request.minimumHeightAboveSurface, request.maximumHeightAboveSurface),
                (float)random.NextDouble());
            Vector2 candidate = new Vector2(x, surface.y + height);
            if (IsValidItemSpawnCandidate(request, candidate, surface.y))
            {
                spawnWorld = candidate;
                return true;
            }
        }

        return false;
    }

    public TerrainType GetTerrainTypeWorld(Vector2 worldPosition)
    {
        if (!EnsureInitialized())
        {
            return TerrainType.Empty;
        }

        Vector2Int pixel = WorldToPixel(worldPosition);
        return IsPixelInBounds(pixel) ? terrainTypeMask[pixel.x, pixel.y] : TerrainType.Empty;
    }

    public TerrainHit CheckTerrainHit(Vector2 previousWorld, Vector2 currentWorld)
    {
        if (!EnsureInitialized())
        {
            return TerrainHit.Miss;
        }

        float distance = Vector2.Distance(previousWorld, currentWorld);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance * PixelsPerUnit * 2f));
        for (int i = 0; i <= steps; i++)
        {
            Vector2 sample = Vector2.Lerp(previousWorld, currentWorld, i / (float)steps);
            Vector2Int pixel = WorldToPixel(sample);
            if (IsPixelInBounds(pixel) && solidMask[pixel.x, pixel.y])
            {
                return new TerrainHit(true, sample, pixel, terrainTypeMask[pixel.x, pixel.y]);
            }
        }

        return TerrainHit.Miss;
    }

    public bool TryCheckTerrainHit(Vector2 previousWorld, Vector2 currentWorld, out TerrainHit hit)
    {
        hit = CheckTerrainHit(previousWorld, currentWorld);
        return hit.hit;
    }

    public bool DestroyCircle(Vector2 worldCenter, int radiusPx)
    {
        if (!EnsureInitialized() || radiusPx <= 0)
        {
            return false;
        }

        bool changed = DestroyCircleInternal(worldCenter, radiusPx);
        ApplyTextureAndDirtyChunks(changed);
        return changed;
    }

    public bool CreateCircle(Vector2 worldCenter, int radiusPx)
    {
        return CreateCircle(worldCenter, radiusPx, TerrainType.Created, null);
    }

    public bool CreateCircle(Vector2 worldCenter, int radiusPx, TerrainType terrainType)
    {
        return CreateCircle(worldCenter, radiusPx, terrainType, null);
    }

    public bool CreateCircle(
        Vector2 worldCenter,
        int radiusPx,
        TerrainType terrainType,
        IEnumerable<Collider2D> blockedColliders)
    {
        if (!EnsureInitialized() || radiusPx <= 0)
        {
            return false;
        }

        bool changed = CreateCircleInternal(worldCenter, radiusPx, terrainType, blockedColliders);
        ApplyTextureAndDirtyChunks(changed);
        return changed;
    }

    public bool CreateCircleDeferred(
        Vector2 worldCenter,
        int radiusPx,
        TerrainType terrainType,
        IEnumerable<Collider2D> blockedColliders)
    {
        return EnsureInitialized() &&
               radiusPx > 0 &&
               CreateEllipseInternal(
                   worldCenter,
                   radiusPx,
                   radiusPx,
                   terrainType,
                   blockedColliders);
    }

    public bool CreateEllipseDeferred(
        Vector2 worldCenter,
        int radiusXPx,
        int radiusYPx,
        TerrainType terrainType,
        IEnumerable<Collider2D> blockedColliders)
    {
        return EnsureInitialized() &&
               radiusXPx > 0 &&
               radiusYPx > 0 &&
               CreateEllipseInternal(
                   worldCenter,
                   radiusXPx,
                   radiusYPx,
                   terrainType,
                   blockedColliders);
    }

    public void FlushDeferredTerrainChanges()
    {
        if (!EnsureInitialized() || dirtyChunks.Count == 0)
        {
            return;
        }

        runtimeVisualTexture.Apply(false);
        runtimeCollisionTexture.Apply(false);
        RebuildDirtyChunks();
        TerrainChanged?.Invoke();
    }

    public bool CreateBridge(Vector2 startWorld, Vector2 direction, float lengthWorldUnits, int thicknessPx)
    {
        if (!EnsureInitialized())
        {
            return false;
        }

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector2.right;
        }

        direction.Normalize();
        int brushRadius = Mathf.Max(1, thicknessPx);
        float length = Mathf.Max(0f, lengthWorldUnits);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length * PixelsPerUnit / brushRadius));
        bool changed = false;
        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = startWorld + direction * (length * i / steps);
            changed |= CreateCircleInternal(point, brushRadius, TerrainType.Created, null);
        }

        ApplyTextureAndDirtyChunks(changed);
        return changed;
    }

    public void RebuildAllColliders()
    {
        if (chunks == null)
        {
            return;
        }

        for (int x = 0; x < chunks.GetLength(0); x++)
        {
            for (int y = 0; y < chunks.GetLength(1); y++)
            {
                chunks[x, y]?.RebuildColliders();
            }
        }

        dirtyChunks.Clear();
    }

    public void RebuildDirtyChunks()
    {
        foreach (TerrainChunk chunk in dirtyChunks)
        {
            chunk?.RebuildColliders();
        }

        dirtyChunks.Clear();
    }

    internal float SolidRatioInPixelRect(int x, int y, int width, int height)
    {
        int minX = Mathf.Clamp(x, 0, WidthPx);
        int minY = Mathf.Clamp(y, 0, HeightPx);
        int maxX = Mathf.Clamp(x + width, 0, WidthPx);
        int maxY = Mathf.Clamp(y + height, 0, HeightPx);
        int totalPixels = Mathf.Max(0, maxX - minX) * Mathf.Max(0, maxY - minY);
        if (totalPixels <= 0)
        {
            return 0f;
        }

        int solidPixels = 0;
        for (int py = minY; py < maxY; py++)
        {
            for (int px = minX; px < maxX; px++)
            {
                if (solidMask[px, py])
                {
                    solidPixels++;
                }
            }
        }

        return solidPixels / (float)totalPixels;
    }

    internal Vector2 PixelRectCenterToLocal(int x, int y, int width, int height)
    {
        return new Vector2((x + width * 0.5f) / PixelsPerUnit, (y + height * 0.5f) / PixelsPerUnit);
    }

    internal Vector2 PixelSizeToWorldSize(int width, int height)
    {
        return new Vector2(width / (float)PixelsPerUnit, height / (float)PixelsPerUnit);
    }

    private bool DestroyCircleInternal(Vector2 worldCenter, int radiusPx)
    {
        Vector2Int center = WorldToPixel(worldCenter);
        int radius = Mathf.Max(1, radiusPx);
        int radiusSquared = radius * radius;
        bool changed = false;

        for (int y = center.y - radius; y <= center.y + radius; y++)
        {
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                int dx = x - center.x;
                int dy = y - center.y;
                if (dx * dx + dy * dy > radiusSquared)
                {
                    continue;
                }

                Vector2Int pixel = new Vector2Int(x, y);
                if (!IsPixelInBounds(pixel))
                {
                    continue;
                }

                TerrainType currentType = terrainTypeMask[x, y];
                if (currentType != TerrainType.Base && currentType != TerrainType.Created)
                {
                    continue;
                }

                solidMask[x, y] = false;
                terrainTypeMask[x, y] = TerrainType.Empty;
                runtimeVisualTexture.SetPixel(x, y, Color.clear);
                runtimeCollisionTexture.SetPixel(x, y, Color.clear);
                MarkDirtyPixelAndNeighbors(x, y);
                changed = true;
            }
        }

        return changed;
    }

    private bool CreateCircleInternal(
        Vector2 worldCenter,
        int radiusPx,
        TerrainType terrainType,
        IEnumerable<Collider2D> blockedColliders)
    {
        return CreateEllipseInternal(
            worldCenter,
            radiusPx,
            radiusPx,
            terrainType,
            blockedColliders);
    }

    private bool CreateEllipseInternal(
        Vector2 worldCenter,
        int radiusXPx,
        int radiusYPx,
        TerrainType terrainType,
        IEnumerable<Collider2D> blockedColliders)
    {
        Vector2Int center = WorldToPixel(worldCenter);
        int radiusX = Mathf.Max(1, radiusXPx);
        int radiusY = Mathf.Max(1, radiusYPx);
        Color fillColor = ColorForTerrainType(terrainType);
        bool changed = false;

        for (int y = center.y - radiusY; y <= center.y + radiusY; y++)
        {
            for (int x = center.x - radiusX; x <= center.x + radiusX; x++)
            {
                float normalizedX = (x - center.x) / (float)radiusX;
                float normalizedY = (y - center.y) / (float)radiusY;
                if (normalizedX * normalizedX + normalizedY * normalizedY > 1f)
                {
                    continue;
                }

                Vector2Int pixel = new Vector2Int(x, y);
                if (!IsPixelInBounds(pixel) || terrainTypeMask[x, y] != TerrainType.Empty)
                {
                    continue;
                }

                Vector2 worldPoint = PixelToWorld(pixel);
                if (IsBlockedByCollider(worldPoint, blockedColliders))
                {
                    continue;
                }

                solidMask[x, y] = true;
                terrainTypeMask[x, y] = terrainType;
                runtimeVisualTexture.SetPixel(x, y, fillColor);
                runtimeCollisionTexture.SetPixel(x, y, Color.white);
                MarkDirtyPixelAndNeighbors(x, y);
                changed = true;
            }
        }

        return changed;
    }

    private bool IsBlockedByCollider(Vector2 worldPoint, IEnumerable<Collider2D> blockedColliders)
    {
        if (blockedColliders == null)
        {
            return false;
        }

        foreach (Collider2D blockedCollider in blockedColliders)
        {
            if (blockedCollider == null || !blockedCollider.enabled)
            {
                continue;
            }

            Vector2 closestPoint = blockedCollider.ClosestPoint(worldPoint);
            if ((closestPoint - worldPoint).sqrMagnitude <=
                characterBuildClearanceWorld * characterBuildClearanceWorld)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyTextureAndDirtyChunks(bool changed)
    {
        if (!changed)
        {
            return;
        }

        runtimeVisualTexture.Apply(false);
        runtimeCollisionTexture.Apply(false);
        RebuildDirtyChunks();
        TerrainChanged?.Invoke();
    }

    private void BuildMasks()
    {
        int width = runtimeVisualTexture.width;
        int height = runtimeVisualTexture.height;
        solidMask = new bool[width, height];
        terrainTypeMask = new TerrainType[width, height];
        Color32[] collisionPixels = runtimeCollisionTexture.GetPixels32();
        Color32[] visualPixels = runtimeVisualTexture.GetPixels32();
        int alphaCutoff = Mathf.RoundToInt(maskAlphaThreshold * 255f);
        bool collisionTextureChanged = false;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = y * width + x;
                Color32 collisionPixel = collisionPixels[pixelIndex];
                Color32 visualPixel = visualPixels[pixelIndex];
                bool collisionSolid = collisionPixel.a > alphaCutoff;
                bool visibleTerrainSolid =
                    mergeVisibleTerrainIntoCollision &&
                    visualPixel.a > alphaCutoff;

                if (!collisionSolid && !visibleTerrainSolid)
                {
                    solidMask[x, y] = false;
                    terrainTypeMask[x, y] = TerrainType.Empty;
                    continue;
                }

                solidMask[x, y] = true;
                terrainTypeMask[x, y] = useIndestructibleTerrain &&
                    IsIndestructibleColor(collisionSolid ? collisionPixel : visualPixel)
                    ? TerrainType.Indestructible
                    : TerrainType.Base;

                if (!collisionSolid && visibleTerrainSolid)
                {
                    runtimeCollisionTexture.SetPixel(x, y, Color.white);
                    collisionTextureChanged = true;
                }
            }
        }

        if (collisionTextureChanged)
        {
            runtimeCollisionTexture.Apply(false);
            Debug.Log("Visible terrain pixels missing from the collision mask were merged as destructible solid terrain.");
        }
    }

    private static Texture2D CreateReadableRuntimeTexture(Texture2D texture)
    {
        Texture2D copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        if (texture.isReadable)
        {
            copy.SetPixels32(texture.GetPixels32());
            copy.Apply(false);
            return copy;
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(texture, temporary);
        RenderTexture.active = temporary;
        copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        copy.Apply(false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temporary);
        return copy;
    }

    private static Texture2D CreateHorizontallyExpandedRuntimeTexture(
        Texture2D texture,
        float horizontalScale)
    {
        Texture2D readable = CreateReadableRuntimeTexture(texture);
        int targetWidth = Mathf.Max(1, Mathf.RoundToInt(readable.width * Mathf.Max(1f, horizontalScale)));
        if (targetWidth == readable.width)
        {
            return readable;
        }

        Texture2D expanded = new Texture2D(targetWidth, readable.height, TextureFormat.RGBA32, false);
        Color32[] sourcePixels = readable.GetPixels32();
        Color32[] expandedPixels = new Color32[targetWidth * readable.height];

        for (int y = 0; y < readable.height; y++)
        {
            int sourceRow = y * readable.width;
            int targetRow = y * targetWidth;
            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = Mathf.Clamp(
                    Mathf.FloorToInt((x + 0.5f) * readable.width / targetWidth),
                    0,
                    readable.width - 1);
                expandedPixels[targetRow + x] = sourcePixels[sourceRow + sourceX];
            }
        }

        expanded.SetPixels32(expandedPixels);
        expanded.Apply(false);
        if (Application.isPlaying) Destroy(readable);
        else DestroyImmediate(readable);
        return expanded;
    }

    private static bool IsIndestructibleColor(Color32 color)
    {
        int max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        int min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        return max - min <= 18 && max >= 70;
    }

    private Color ColorForTerrainType(TerrainType terrainType)
    {
        switch (terrainType)
        {
            case TerrainType.Created: return createdTerrainColor;
            case TerrainType.Indestructible: return indestructibleTerrainColor;
            case TerrainType.Base: return baseTerrainColor;
            default: return Color.clear;
        }
    }

    private void ApplyRuntimeSprite()
    {
        if (terrainRenderer == null)
        {
            terrainRenderer = GetComponent<SpriteRenderer>();
        }

        if (runtimeSprite != null)
        {
            if (Application.isPlaying) Destroy(runtimeSprite);
            else DestroyImmediate(runtimeSprite);
        }

        runtimeSprite = Sprite.Create(
            runtimeVisualTexture,
            new Rect(0, 0, runtimeVisualTexture.width, runtimeVisualTexture.height),
            Vector2.zero,
            PixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        terrainRenderer.sprite = runtimeSprite;
        terrainRenderer.transform.position = new Vector3(terrainOriginWorld.x, terrainOriginWorld.y, terrainRenderer.transform.position.z);
    }

    private void BuildChunks()
    {
        if (chunkRoot == null)
        {
            GameObject chunkRootObject = new GameObject("TerrainChunkRoot");
            chunkRootObject.transform.SetParent(transform, false);
            chunkRoot = chunkRootObject.transform;
        }

        chunkRoot.position = new Vector3(terrainOriginWorld.x, terrainOriginWorld.y, 0f);
        ClearChunkChildren();
        int columns = Mathf.CeilToInt(WidthPx / (float)chunkSizePx);
        int rows = Mathf.CeilToInt(HeightPx / (float)chunkSizePx);
        chunks = new TerrainChunk[columns, rows];

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int pixelX = x * chunkSizePx;
                int pixelY = y * chunkSizePx;
                int width = Mathf.Min(chunkSizePx, WidthPx - pixelX);
                int height = Mathf.Min(chunkSizePx, HeightPx - pixelY);
                GameObject chunkObject = new GameObject($"TerrainChunk_{x}_{y}");
                chunkObject.transform.SetParent(chunkRoot, false);
                TerrainChunk chunk = chunkObject.AddComponent<TerrainChunk>();
                chunk.Initialize(
                    this,
                    new Vector2Int(x, y),
                    new RectInt(pixelX, pixelY, width, height),
                    collisionCellSizePx,
                    collisionSolidRatioThreshold);
                chunks[x, y] = chunk;
            }
        }
    }

    private void ClearChunkChildren()
    {
        if (chunkRoot == null)
        {
            return;
        }

        for (int i = chunkRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = chunkRoot.GetChild(i);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }

    private bool TryBuildCharacterSpawnAtX(
        TerrainCharacterSpawnRequest request,
        float worldX,
        out Vector2 spawnWorld)
    {
        spawnWorld = Vector2.zero;
        if (!FindTerrainSurface(worldX, out Vector2 surface))
        {
            return false;
        }

        Vector2 extents = new Vector2(
            Mathf.Max(0.1f, request.colliderExtents.x),
            Mathf.Max(0.1f, request.colliderExtents.y));
        Vector2 candidate = new Vector2(
            worldX,
            surface.y + extents.y + Mathf.Clamp(request.spawnLiftWorld, 0.03f, 0.1f));

        if (candidate.y <= terrainOriginWorld.y + Mathf.Max(0f, request.waterPaddingWorld) ||
            !HasStableSurface(worldX, extents.x + 0.12f, request.maximumSurfaceHeightDifference) ||
            !IsTerrainAreaClear(candidate, extents, request.clearanceSampleStepWorld) ||
            IsExcluded(candidate, request.exclusions))
        {
            return false;
        }

        spawnWorld = candidate;
        return true;
    }

    private bool IsValidItemSpawnCandidate(
        TerrainItemSpawnRequest request,
        Vector2 candidate,
        float surfaceY)
    {
        Vector2 extents = new Vector2(
            Mathf.Max(0.1f, request.halfExtents.x),
            Mathf.Max(0.1f, request.halfExtents.y));
        if (surfaceY <= terrainOriginWorld.y + Mathf.Max(0f, request.waterPaddingWorld) ||
            !HasStableSurface(candidate.x, extents.x + 0.08f, request.maximumSurfaceHeightDifference) ||
            !IsTerrainAreaClear(candidate, extents, request.clearanceSampleStepWorld) ||
            IsExcluded(candidate, request.exclusions))
        {
            return false;
        }

        return true;
    }

    private bool HasStableSurface(float centerX, float halfWidth, float maximumHeightDifference)
    {
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        const int sampleCount = 5;
        for (int i = 0; i < sampleCount; i++)
        {
            float x = centerX + Mathf.Lerp(-halfWidth, halfWidth, i / (float)(sampleCount - 1));
            if (!FindTerrainSurface(x, out Vector2 surface))
            {
                return false;
            }

            minY = Mathf.Min(minY, surface.y);
            maxY = Mathf.Max(maxY, surface.y);
        }

        return maxY - minY <= Mathf.Max(0.05f, maximumHeightDifference);
    }

    private bool IsTerrainAreaClear(Vector2 center, Vector2 extents, float sampleStep)
    {
        float step = Mathf.Max(0.08f, sampleStep);
        float left = center.x - extents.x;
        float right = center.x + extents.x;
        float bottom = center.y - extents.y + 0.02f;
        float top = center.y + extents.y;

        for (float y = bottom; y <= top + 0.001f; y += step)
        {
            for (float x = left; x <= right + 0.001f; x += step)
            {
                if (IsSolidWorld(new Vector2(x, y)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsExcluded(Vector2 candidate, List<TerrainSpawnExclusion> exclusions)
    {
        if (exclusions == null)
        {
            return false;
        }

        for (int i = 0; i < exclusions.Count; i++)
        {
            float distance = Mathf.Max(0f, exclusions[i].minimumDistance);
            if ((candidate - exclusions[i].position).sqrMagnitude < distance * distance)
            {
                return true;
            }
        }

        return false;
    }

    private void MarkDirtyPixelAndNeighbors(int x, int y)
    {
        MarkDirtyChunkAtPixel(x, y);
        int localX = ((x % chunkSizePx) + chunkSizePx) % chunkSizePx;
        int localY = ((y % chunkSizePx) + chunkSizePx) % chunkSizePx;
        int margin = collisionCellSizePx;

        if (localX < margin) MarkDirtyChunkAtPixel(x - margin, y);
        if (localX >= chunkSizePx - margin) MarkDirtyChunkAtPixel(x + margin, y);
        if (localY < margin) MarkDirtyChunkAtPixel(x, y - margin);
        if (localY >= chunkSizePx - margin) MarkDirtyChunkAtPixel(x, y + margin);
        if (localX < margin && localY < margin) MarkDirtyChunkAtPixel(x - margin, y - margin);
        if (localX < margin && localY >= chunkSizePx - margin) MarkDirtyChunkAtPixel(x - margin, y + margin);
        if (localX >= chunkSizePx - margin && localY < margin) MarkDirtyChunkAtPixel(x + margin, y - margin);
        if (localX >= chunkSizePx - margin && localY >= chunkSizePx - margin) MarkDirtyChunkAtPixel(x + margin, y + margin);
    }

    private void MarkDirtyChunkAtPixel(int x, int y)
    {
        if (chunks == null || chunks.Length == 0 || x < 0 || y < 0 || x >= WidthPx || y >= HeightPx)
        {
            return;
        }

        int chunkX = Mathf.Clamp(x / chunkSizePx, 0, chunks.GetLength(0) - 1);
        int chunkY = Mathf.Clamp(y / chunkSizePx, 0, chunks.GetLength(1) - 1);
        TerrainChunk chunk = chunks[chunkX, chunkY];
        if (chunk != null)
        {
            dirtyChunks.Add(chunk);
        }
    }

    private bool EnsureInitialized()
    {
        if (!initialized && visualSourceTexture != null)
        {
            InitializeTerrain();
        }

        return initialized && runtimeVisualTexture != null && solidMask != null && terrainTypeMask != null;
    }

    private static int NormalizeCollisionCellSize(int value)
    {
        return value <= 4 ? 4 : 8;
    }

    private void OnValidate()
    {
        pixelsPerUnit = Mathf.Max(1, pixelsPerUnit);
        horizontalExpansion = Mathf.Clamp(horizontalExpansion, 1f, 2.5f);
        chunkSizePx = Mathf.Max(1, chunkSizePx);
        collisionCellSizePx = NormalizeCollisionCellSize(collisionCellSizePx);
        collisionSolidRatioThreshold = Mathf.Clamp01(collisionSolidRatioThreshold);
        maskAlphaThreshold = Mathf.Clamp01(maskAlphaThreshold);
        characterBuildClearanceWorld = Mathf.Max(0f, characterBuildClearanceWorld);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawChunkGizmos || WidthPx <= 0 || HeightPx <= 0)
        {
            return;
        }

        float worldWidth = WidthPx / (float)PixelsPerUnit;
        float worldHeight = HeightPx / (float)PixelsPerUnit;
        Vector3 origin = new Vector3(terrainOriginWorld.x, terrainOriginWorld.y, 0f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(
            origin + new Vector3(worldWidth * 0.5f, worldHeight * 0.5f, 0f),
            new Vector3(worldWidth, worldHeight, 0f));
    }
}
