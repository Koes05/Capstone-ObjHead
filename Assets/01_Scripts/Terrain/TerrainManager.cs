using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class TerrainManager : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private Texture2D sourceTexture;
    [SerializeField] private SpriteRenderer terrainRenderer;
    [SerializeField] private Transform chunkRoot;

    [Header("Coordinates")]
    [SerializeField] private Vector2 terrainOriginWorld = Vector2.zero;
    [SerializeField] private int pixelsPerUnit = 32;

    [Header("Collision")]
    [SerializeField] private int chunkSizePx = 64;
    [SerializeField] private int collisionCellSizePx = 8;
    [SerializeField, Range(0f, 1f)] private float collisionSolidRatioThreshold = 0.4f;
    [SerializeField] private bool buildCollidersOnStart = true;

    [Header("Terrain Colors")]
    [SerializeField] private Color baseTerrainColor = new Color32(126, 82, 45, 255);
    [SerializeField] private Color createdTerrainColor = new Color32(105, 190, 84, 255);
    [SerializeField] private Color indestructibleTerrainColor = new Color32(118, 122, 128, 255);
    [SerializeField, Range(0f, 1f)] private float alphaThreshold = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool drawChunkGizmos = true;

    private Texture2D runtimeTexture;
    private Sprite runtimeSprite;
    private bool[,] solidMask;
    private TerrainType[,] terrainTypeMask;
    private TerrainChunk[,] chunks;
    private readonly HashSet<TerrainChunk> dirtyChunks = new HashSet<TerrainChunk>();
    private bool initialized;

    public int WidthPx
    {
        get { return runtimeTexture != null ? runtimeTexture.width : sourceTexture != null ? sourceTexture.width : 0; }
    }

    public int HeightPx
    {
        get { return runtimeTexture != null ? runtimeTexture.height : sourceTexture != null ? sourceTexture.height : 0; }
    }

    public int PixelsPerUnit
    {
        get { return Mathf.Max(1, pixelsPerUnit); }
    }

    public Vector2 TerrainOriginWorld
    {
        get { return terrainOriginWorld; }
    }

    public bool IsInitialized
    {
        get { return initialized; }
    }

    private void Reset()
    {
        terrainRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        if (!initialized && sourceTexture != null)
        {
            InitializeTerrain();
        }
    }

    public void Configure(Texture2D terrainTexture, SpriteRenderer renderer, Transform terrainChunkRoot, Vector2 originWorld, int ppu, int chunkSize, int collisionCellSize)
    {
        sourceTexture = terrainTexture;
        terrainRenderer = renderer;
        chunkRoot = terrainChunkRoot;
        terrainOriginWorld = originWorld;
        pixelsPerUnit = Mathf.Max(1, ppu);
        chunkSizePx = Mathf.Max(1, chunkSize);
        collisionCellSizePx = Mathf.Max(1, collisionCellSize);

        InitializeTerrain();
    }

    public void InitializeTerrain()
    {
        if (sourceTexture == null)
        {
            Debug.LogError("TerrainManager needs a source texture.");
            return;
        }

        if (terrainRenderer == null)
        {
            terrainRenderer = GetComponent<SpriteRenderer>();
        }

        runtimeTexture = CreateReadableRuntimeTexture(sourceTexture);
        runtimeTexture.name = sourceTexture.name + "_Runtime";
        runtimeTexture.filterMode = FilterMode.Point;
        runtimeTexture.wrapMode = TextureWrapMode.Clamp;

        BuildMasksFromRuntimeTexture();
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
            Mathf.FloorToInt(local.y * PixelsPerUnit)
        );
    }

    public Vector2 PixelToWorld(Vector2Int pixel)
    {
        return terrainOriginWorld + new Vector2(
            (pixel.x + 0.5f) / PixelsPerUnit,
            (pixel.y + 0.5f) / PixelsPerUnit
        );
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
            float t = i / (float)steps;
            Vector2 sample = Vector2.Lerp(previousWorld, currentWorld, t);
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
        if (!EnsureInitialized())
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

    public bool CreateCircle(Vector2 worldCenter, int radiusPx, TerrainType terrainType, IEnumerable<Collider2D> blockedColliders)
    {
        if (!EnsureInitialized())
        {
            return false;
        }

        bool changed = CreateCircleInternal(worldCenter, radiusPx, terrainType, blockedColliders);
        ApplyTextureAndDirtyChunks(changed);
        return changed;
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
        int steps = Mathf.Max(1, Mathf.CeilToInt(length * PixelsPerUnit / Mathf.Max(1, brushRadius)));
        bool changed = false;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 point = startWorld + direction * (length * t);
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
                if (chunks[x, y] != null)
                {
                    chunks[x, y].RebuildColliders();
                }
            }
        }

        dirtyChunks.Clear();
    }

    public void RebuildDirtyChunks()
    {
        foreach (TerrainChunk chunk in dirtyChunks)
        {
            if (chunk != null)
            {
                chunk.RebuildColliders();
            }
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
        return new Vector2(
            (x + width * 0.5f) / PixelsPerUnit,
            (y + height * 0.5f) / PixelsPerUnit
        );
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
                runtimeTexture.SetPixel(x, y, Color.clear);
                MarkDirtyPixel(x, y);
                changed = true;
            }
        }

        return changed;
    }

    private bool CreateCircleInternal(Vector2 worldCenter, int radiusPx, TerrainType terrainType, IEnumerable<Collider2D> blockedColliders)
    {
        Vector2Int center = WorldToPixel(worldCenter);
        int radius = Mathf.Max(1, radiusPx);
        int radiusSquared = radius * radius;
        Color fillColor = ColorForTerrainType(terrainType);
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
                runtimeTexture.SetPixel(x, y, fillColor);
                MarkDirtyPixel(x, y);
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
            if (blockedCollider != null && blockedCollider.OverlapPoint(worldPoint))
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

        runtimeTexture.Apply(false);
        RebuildDirtyChunks();
    }

    private void BuildMasksFromRuntimeTexture()
    {
        int width = runtimeTexture.width;
        int height = runtimeTexture.height;
        solidMask = new bool[width, height];
        terrainTypeMask = new TerrainType[width, height];

        Color32[] pixels = runtimeTexture.GetPixels32();
        int alphaCutoff = Mathf.RoundToInt(alphaThreshold * 255f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color32 pixel = pixels[index];

                if (pixel.a <= alphaCutoff)
                {
                    solidMask[x, y] = false;
                    terrainTypeMask[x, y] = TerrainType.Empty;
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                solidMask[x, y] = true;
                terrainTypeMask[x, y] = IsIndestructibleColor(pixel) ? TerrainType.Indestructible : TerrainType.Base;
            }
        }

        runtimeTexture.SetPixels32(pixels);
        runtimeTexture.Apply(false);
    }

    private Texture2D CreateReadableRuntimeTexture(Texture2D texture)
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

    private bool IsIndestructibleColor(Color32 color)
    {
        int max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        int min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        bool looksGray = max - min <= 18;
        bool brightEnough = max >= 70;
        return looksGray && brightEnough;
    }

    private Color ColorForTerrainType(TerrainType terrainType)
    {
        switch (terrainType)
        {
            case TerrainType.Created:
                return createdTerrainColor;
            case TerrainType.Indestructible:
                return indestructibleTerrainColor;
            case TerrainType.Base:
                return baseTerrainColor;
            default:
                return Color.clear;
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
            if (Application.isPlaying)
            {
                Destroy(runtimeSprite);
            }
            else
            {
                DestroyImmediate(runtimeSprite);
            }
        }

        runtimeSprite = Sprite.Create(
            runtimeTexture,
            new Rect(0, 0, runtimeTexture.width, runtimeTexture.height),
            Vector2.zero,
            PixelsPerUnit,
            0,
            SpriteMeshType.FullRect
        );

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

                GameObject chunkObject = new GameObject("TerrainChunk_" + x + "_" + y);
                chunkObject.transform.SetParent(chunkRoot, false);
                chunkObject.transform.localPosition = Vector3.zero;

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
        for (int i = chunkRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = chunkRoot.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private void MarkDirtyPixel(int x, int y)
    {
        if (chunks == null || chunks.Length == 0)
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
        if (!initialized && sourceTexture != null)
        {
            InitializeTerrain();
        }

        return initialized && runtimeTexture != null && solidMask != null && terrainTypeMask != null;
    }

    private void OnValidate()
    {
        pixelsPerUnit = Mathf.Max(1, pixelsPerUnit);
        chunkSizePx = Mathf.Max(1, chunkSizePx);
        collisionCellSizePx = Mathf.Max(1, collisionCellSizePx);
        collisionSolidRatioThreshold = Mathf.Clamp01(collisionSolidRatioThreshold);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawChunkGizmos)
        {
            return;
        }

        int width = WidthPx;
        int height = HeightPx;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        float worldWidth = width / (float)PixelsPerUnit;
        float worldHeight = height / (float)PixelsPerUnit;
        Vector3 origin = new Vector3(terrainOriginWorld.x, terrainOriginWorld.y, 0f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(origin + new Vector3(worldWidth * 0.5f, worldHeight * 0.5f, 0f), new Vector3(worldWidth, worldHeight, 0f));

        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        float chunkWorldSize = chunkSizePx / (float)PixelsPerUnit;

        for (float x = 0f; x <= worldWidth + 0.001f; x += chunkWorldSize)
        {
            Gizmos.DrawLine(origin + new Vector3(x, 0f, 0f), origin + new Vector3(x, worldHeight, 0f));
        }

        for (float y = 0f; y <= worldHeight + 0.001f; y += chunkWorldSize)
        {
            Gizmos.DrawLine(origin + new Vector3(0f, y, 0f), origin + new Vector3(worldWidth, y, 0f));
        }
    }
}
