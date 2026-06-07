using UnityEngine;

public class TerrainTestBootstrap : MonoBehaviour
{
    [SerializeField] private Texture2D terrainTexture = null;
    [SerializeField] private Vector2 terrainOriginWorld = new Vector2(-16f, -8f);
    [SerializeField] private int pixelsPerUnit = 32;
    [SerializeField] private int chunkSizePx = 64;
    [SerializeField] private int collisionCellSizePx = 8;
    [SerializeField] private bool spawnTestBoxes = true;

    private void Awake()
    {
        Camera camera = EnsureCamera();
        Texture2D source = terrainTexture != null ? terrainTexture : GenerateFallbackTerrainTexture();

        GameObject terrainRoot = new GameObject("TerrainRoot");
        terrainRoot.transform.position = Vector3.zero;

        SpriteRenderer renderer = terrainRoot.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 0;

        GameObject chunkRootObject = new GameObject("TerrainChunkRoot");
        chunkRootObject.transform.SetParent(terrainRoot.transform, false);

        TerrainManager manager = terrainRoot.AddComponent<TerrainManager>();
        manager.Configure(source, renderer, chunkRootObject.transform, terrainOriginWorld, pixelsPerUnit, chunkSizePx, collisionCellSizePx);

        TerrainEditBrush brush = terrainRoot.AddComponent<TerrainEditBrush>();
        brush.Configure(manager, camera);

        if (spawnTestBoxes)
        {
            SpawnTestBox(new Vector2(-7f, 0f), new Color32(90, 170, 255, 255), "TestBox_A");
            SpawnTestBox(new Vector2(2f, 1.5f), new Color32(255, 215, 95, 255), "TestBox_B");
        }

        CreateDeathZone();
    }

    private Camera EnsureCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        camera.orthographic = true;
        camera.orthographicSize = 9.5f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(63, 94, 137, 255);
        return camera;
    }

    private void SpawnTestBox(Vector2 position, Color color, string objectName)
    {
        GameObject box = new GameObject(objectName);
        box.transform.position = position;

        SpriteRenderer renderer = box.AddComponent<SpriteRenderer>();
        renderer.sprite = CreateSolidSprite(color);
        renderer.sortingOrder = 2;

        Rigidbody2D rigidbody = box.AddComponent<Rigidbody2D>();
        rigidbody.gravityScale = 1.4f;

        BoxCollider2D collider = box.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.75f, 0.75f);
    }

    private Sprite CreateSolidSprite(Color color)
    {
        Texture2D texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[16 * 16];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply(false);
        texture.filterMode = FilterMode.Point;
        return Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
    }

    private void CreateDeathZone()
    {
        GameObject deathZone = new GameObject("DeathZone");
        deathZone.transform.position = new Vector2(0f, terrainOriginWorld.y - 2f);

        BoxCollider2D collider = deathZone.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(40f, 1f);

        deathZone.AddComponent<DeathZone>();
    }

    private Texture2D GenerateFallbackTerrainTexture()
    {
        const int width = 1024;
        const int height = 512;

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[width * height];
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 baseGround = new Color32(126, 82, 45, 255);
        Color32 rock = new Color32(118, 122, 128, 255);

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = clear;
        }

        for (int x = 0; x < width; x++)
        {
            float wave = Mathf.Sin(x * 0.017f) * 20f + Mathf.Sin(x * 0.006f + 1.3f) * 32f;
            int groundTop = Mathf.Clamp(112 + Mathf.RoundToInt(wave), 72, 174);

            for (int y = 0; y <= groundTop; y++)
            {
                pixels[y * width + x] = baseGround;
            }
        }

        FillRect(pixels, width, 420, 115, 36, 170, rock);
        FillRect(pixels, width, 690, 105, 44, 145, rock);
        FillRect(pixels, width, 185, 235, 210, 30, baseGround);
        FillRect(pixels, width, 610, 275, 190, 28, baseGround);

        texture.SetPixels32(pixels);
        texture.Apply(false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.name = "generated_terrain_map_01";
        return texture;
    }

    private void FillRect(Color32[] pixels, int textureWidth, int x, int y, int width, int height, Color32 color)
    {
        for (int py = y; py < y + height; py++)
        {
            for (int px = x; px < x + width; px++)
            {
                if (px >= 0 && px < textureWidth && py >= 0 && py * textureWidth + px < pixels.Length)
                {
                    pixels[py * textureWidth + px] = color;
                }
            }
        }
    }
}
