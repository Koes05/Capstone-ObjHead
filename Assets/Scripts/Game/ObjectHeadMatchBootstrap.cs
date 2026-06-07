using System.Collections.Generic;
using UnityEngine;

public class ObjectHeadMatchBootstrap : MonoBehaviour
{
    private const string TerrainResourcePath = "Sprites/Map/terrain_map_object_head_1536x512";
    private const string BackgroundResourcePath = "Sprites/Map/background_ocean_sky";

    [SerializeField, Range(2, 4)] private int playerCount = 2;
    [SerializeField] private Vector2 terrainOriginWorld = new Vector2(-24f, -8f);
    [SerializeField] private int pixelsPerUnit = 32;
    [SerializeField] private int chunkSizePx = 64;
    [SerializeField] private int collisionCellSizePx = 4;
    [SerializeField] private int deterministicSpawnSeed = 6974;
    [SerializeField] private bool addTerrainEditBrush = true;
    [SerializeField] private bool cleanupExistingPrototypeScene = true;

    private TerrainManager terrain;
    private TurnManager turnManager;
    private bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateForPlayableScene()
    {
        if (FindAny<TerrainTestBootstrap>() != null || FindAny<ObjectHeadMatchBootstrap>() != null)
        {
            return;
        }

        new GameObject("ObjectHeadMatchBootstrap").AddComponent<ObjectHeadMatchBootstrap>();
    }

    private void Awake()
    {
        BuildMatch();
    }

    public void BuildMatch()
    {
        if (built)
        {
            return;
        }

        built = true;

        if (cleanupExistingPrototypeScene)
        {
            CleanupExistingPrototypeScene();
        }

        Camera camera = EnsureCamera();
        terrain = BuildTerrain(camera);
        BuildBackground(terrain);
        BuildZones(terrain);

        turnManager = new GameObject("TurnManager").AddComponent<TurnManager>();
        PlayerInventoryManager inventoryManager = new GameObject("PlayerInventoryManager").AddComponent<PlayerInventoryManager>();
        inventoryManager.ConfigurePlayers(playerCount);
        TurnCharacterController[] orderedCharacters = SpawnDefaultTeams(terrain);

        TerrainRandomSpawner randomSpawner = new GameObject("TerrainRandomSpawner").AddComponent<TerrainRandomSpawner>();
        randomSpawner.Configure(terrain, orderedCharacters, deterministicSpawnSeed);
        randomSpawner.SpawnCharacters();
        CreateSpawnMarkers(orderedCharacters);

        BuildCommonHeadSystem(terrain, turnManager);
        turnManager.SetCharacters(orderedCharacters);

        ObjectHeadCameraController cameraController = camera.GetComponent<ObjectHeadCameraController>();
        if (cameraController == null)
        {
            cameraController = camera.gameObject.AddComponent<ObjectHeadCameraController>();
        }
        cameraController.Configure(terrain, turnManager);

        if (FindAny<ObjectHeadHUD>() == null)
        {
            new GameObject("ObjectHeadHUD").AddComponent<ObjectHeadHUD>();
        }
    }

    private void CleanupExistingPrototypeScene()
    {
        DestroyComponents<TerrainCameraFitter>();
        DestroyComponents<ObjectHeadCameraController>();
        DestroyObjects<TurnManager>();
        DestroyObjects<ObjectHeadHUD>();
        DestroyRootObjects<TerrainManager>();
        DestroyRootObjects<TerrainRandomSpawner>();
        DestroyRootObjects<CommonHeadItemSpawner>();
        DestroyRootObjects<CommonHeadItem>();
        DestroyRootObjects<PlayerInventoryManager>();
        DestroyRootObjects<GroundHazardZone>();
        DestroyNamedRoots("PlayerCube_", "Ground", "TerrainRoot", "SpawnGroup_", "ItemSpawnRoot", "Background_OceanSky", "WaterZone", "DeathZone");
    }

    private TerrainManager BuildTerrain(Camera camera)
    {
        Texture2D terrainTexture = Resources.Load<Texture2D>(TerrainResourcePath);
        if (terrainTexture == null)
        {
            Debug.LogError($"Missing terrain texture at Resources/{TerrainResourcePath}.");
            return null;
        }

        GameObject terrainRoot = new GameObject("TerrainRoot");
        SpriteRenderer renderer = terrainRoot.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 0;

        GameObject chunkRootObject = new GameObject("TerrainChunkRoot");
        chunkRootObject.transform.SetParent(terrainRoot.transform, false);

        TerrainManager manager = terrainRoot.AddComponent<TerrainManager>();
        manager.Configure(terrainTexture, null, renderer, chunkRootObject.transform, terrainOriginWorld, pixelsPerUnit, chunkSizePx, collisionCellSizePx);

        if (addTerrainEditBrush)
        {
            TerrainEditBrush brush = terrainRoot.AddComponent<TerrainEditBrush>();
            brush.Configure(manager, camera);
        }

        return manager;
    }

    private void BuildBackground(TerrainManager manager)
    {
        Sprite background = LoadSpriteResource(BackgroundResourcePath, pixelsPerUnit);
        if (background == null || manager == null)
        {
            return;
        }

        GameObject backgroundObject = new GameObject("Background_OceanSky");
        SpriteRenderer renderer = backgroundObject.AddComponent<SpriteRenderer>();
        renderer.sprite = background;
        renderer.sortingOrder = -20;

        float terrainWidth = manager.WidthPx / (float)manager.PixelsPerUnit;
        float terrainHeight = manager.HeightPx / (float)manager.PixelsPerUnit;
        Vector2 center = manager.TerrainOriginWorld + new Vector2(terrainWidth * 0.5f, terrainHeight * 0.5f);
        backgroundObject.transform.position = new Vector3(center.x, center.y, 8f);

        Vector2 spriteSize = renderer.sprite.bounds.size;
        float scale = Mathf.Max(terrainWidth / Mathf.Max(0.01f, spriteSize.x), terrainHeight / Mathf.Max(0.01f, spriteSize.y));
        backgroundObject.transform.localScale = Vector3.one * scale;
    }

    private void BuildZones(TerrainManager manager)
    {
        if (manager == null)
        {
            return;
        }

        float width = manager.WidthPx / (float)manager.PixelsPerUnit;
        float centerX = manager.TerrainOriginWorld.x + width * 0.5f;

        GameObject water = new GameObject("WaterZone");
        water.transform.position = new Vector2(centerX, manager.TerrainOriginWorld.y - 0.75f);
        BoxCollider2D waterCollider = water.AddComponent<BoxCollider2D>();
        waterCollider.isTrigger = true;
        waterCollider.size = new Vector2(width + 6f, 1f);
        water.AddComponent<WaterZone>();

        GameObject death = new GameObject("DeathZone");
        death.transform.position = new Vector2(centerX, manager.TerrainOriginWorld.y - 4f);
        BoxCollider2D deathCollider = death.AddComponent<BoxCollider2D>();
        deathCollider.isTrigger = true;
        deathCollider.size = new Vector2(width + 12f, 3f);
        death.AddComponent<DeathZone>();
    }

    private Sprite LoadSpriteResource(string resourcePath, float ppu)
    {
        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null)
        {
            return sprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null)
        {
            return null;
        }

        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), Mathf.Max(1f, ppu));
    }

    private TurnCharacterController[] SpawnDefaultTeams(TerrainManager manager)
    {
        int charactersPerPlayer = GetCharactersPerPlayer(playerCount);
        ObjectHeadCharacterKind[][] defaultTeams =
        {
            new[] { ObjectHeadCharacterKind.Bulb, ObjectHeadCharacterKind.Seed, ObjectHeadCharacterKind.Bomb },
            new[] { ObjectHeadCharacterKind.Bomb, ObjectHeadCharacterKind.Bulb, ObjectHeadCharacterKind.Seed },
            new[] { ObjectHeadCharacterKind.Seed, ObjectHeadCharacterKind.Bomb, ObjectHeadCharacterKind.Bulb },
            new[] { ObjectHeadCharacterKind.Bulb, ObjectHeadCharacterKind.Bomb, ObjectHeadCharacterKind.Seed }
        };

        List<TurnCharacterController>[] teams = new List<TurnCharacterController>[playerCount];
        for (int player = 0; player < playerCount; player++)
        {
            teams[player] = new List<TurnCharacterController>();

            for (int slot = 0; slot < charactersPerPlayer; slot++)
            {
                ObjectHeadCharacterKind kind = defaultTeams[player][slot % defaultTeams[player].Length];
                float width = manager != null ? manager.WidthPx / (float)manager.PixelsPerUnit : 10f;
                float height = manager != null ? manager.HeightPx / (float)manager.PixelsPerUnit : 10f;
                Vector2 temporaryPosition = manager != null
                    ? manager.TerrainOriginWorld + new Vector2(width * 0.5f, height + 2f + slot)
                    : new Vector2(player * 2f, 8f + slot);
                teams[player].Add(CreateCharacter(player + 1, slot + 1, kind, temporaryPosition));
            }
        }

        List<TurnCharacterController> ordered = new List<TurnCharacterController>();
        for (int slot = 0; slot < charactersPerPlayer; slot++)
        {
            for (int player = 0; player < playerCount; player++)
            {
                if (slot < teams[player].Count)
                {
                    ordered.Add(teams[player][slot]);
                }
            }
        }

        return ordered.ToArray();
    }

    private void BuildCommonHeadSystem(TerrainManager manager, TurnManager managerForTurns)
    {
        if (manager == null || managerForTurns == null)
        {
            return;
        }

        GameObject spawnRootObject = new GameObject("ItemSpawnRoot");
        float[] normalizedPositions = { 0.1f, 0.25f, 0.4f, 0.6f, 0.75f, 0.9f };

        for (int i = 0; i < normalizedPositions.Length; i++)
        {
            Vector2 position = FindGroundSpawn(manager, normalizedPositions[i]) + Vector2.up * 1.3f;
            GameObject pointObject = new GameObject($"ItemSpawnPoint_{i + 1}");
            pointObject.transform.SetParent(spawnRootObject.transform, false);
            pointObject.transform.position = position;
            pointObject.AddComponent<ItemSpawnPoint>();
        }

        GameObject spawnerObject = new GameObject("CommonHeadItemSpawner");
        CommonHeadItemSpawner spawner = spawnerObject.AddComponent<CommonHeadItemSpawner>();
        spawner.Configure(managerForTurns, manager, spawnRootObject.transform);
    }

    private void CreateSpawnMarkers(TurnCharacterController[] characters)
    {
        Dictionary<int, Transform> roots = new Dictionary<int, Transform>();
        for (int i = 0; i < characters.Length; i++)
        {
            TurnCharacterController character = characters[i];
            ObjectHeadTeamMember member = character != null ? character.GetComponent<ObjectHeadTeamMember>() : null;
            if (character == null || member == null)
            {
                continue;
            }

            if (!roots.TryGetValue(member.PlayerIndex, out Transform root))
            {
                root = new GameObject($"SpawnGroup_P{member.PlayerIndex}").transform;
                roots[member.PlayerIndex] = root;
            }

            GameObject marker = new GameObject($"P{member.PlayerIndex}_Spawn_{member.TeamSlotIndex}");
            marker.transform.SetParent(root, false);
            marker.transform.position = character.transform.position;
        }
    }

    private TurnCharacterController CreateCharacter(int playerIndex, int slotIndex, ObjectHeadCharacterKind kind, Vector2 position)
    {
        GameObject character = new GameObject($"P{playerIndex}_Slot{slotIndex}_{kind}");
        character.transform.position = position;

        SpriteRenderer rootRenderer = character.AddComponent<SpriteRenderer>();
        rootRenderer.enabled = false;

        Rigidbody2D body = character.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        CapsuleCollider2D collider = character.AddComponent<CapsuleCollider2D>();
        collider.size = new Vector2(0.72f, 1.15f);
        collider.offset = new Vector2(0f, 0.02f);

        CharacterVisual visual = character.AddComponent<CharacterVisual>();
        TurnCharacterController controller = character.AddComponent<TurnCharacterController>();
        CharacterCombat combat = character.AddComponent<CharacterCombat>();
        AimController aim = character.AddComponent<AimController>();
        PowerChargeController power = character.AddComponent<PowerChargeController>();
        DemoSkillSelector skill = character.AddComponent<DemoSkillSelector>();
        SkillFireController fire = character.AddComponent<SkillFireController>();
        ObjectHeadTeamMember member = character.AddComponent<ObjectHeadTeamMember>();
        CommonHeadUseController commonHeadUse = character.AddComponent<CommonHeadUseController>();

        skill.SetCharacterKind(kind);
        skill.SetSkillIndex(0);
        member.Configure(playerIndex, slotIndex, kind);
        ApplyStats(kind, combat);

        visual.SetFacingRight(playerIndex == 1);
        _ = controller;
        _ = aim;
        _ = power;
        _ = fire;
        _ = commonHeadUse;
        return character.GetComponent<TurnCharacterController>();
    }

    private void ApplyStats(ObjectHeadCharacterKind kind, CharacterCombat combat)
    {
        if (combat == null)
        {
            return;
        }

        switch (kind)
        {
            case ObjectHeadCharacterKind.Bulb:
                combat.ConfigureStats(85, 1.15f, 0.75f);
                break;
            case ObjectHeadCharacterKind.Seed:
                combat.ConfigureStats(120, 0.88f, 1.25f);
                break;
            case ObjectHeadCharacterKind.Bomb:
                combat.ConfigureStats(100, 1.1f, 0.9f);
                break;
        }
    }

    private Vector2 FindGroundSpawn(TerrainManager manager, float normalizedX)
    {
        if (manager == null)
        {
            return Vector2.zero;
        }

        int x = Mathf.Clamp(Mathf.RoundToInt((manager.WidthPx - 1) * normalizedX), 0, manager.WidthPx - 1);
        for (int y = manager.HeightPx - 1; y >= 0; y--)
        {
            Vector2 world = manager.PixelToWorld(new Vector2Int(x, y));
            if (manager.IsSolidWorld(world))
            {
                return world;
            }
        }

        float width = manager.WidthPx / (float)manager.PixelsPerUnit;
        return manager.TerrainOriginWorld + new Vector2(width * normalizedX, 4f);
    }

    private float GetSpawnNormalizedX(int zeroBasedPlayer, int zeroBasedSlot, int charactersPerPlayer)
    {
        if (playerCount == 2)
        {
            float[] p1 = { 0.14f, 0.27f, 0.40f };
            float[] p2 = { 0.86f, 0.73f, 0.60f };
            return zeroBasedPlayer == 0 ? p1[zeroBasedSlot] : p2[zeroBasedSlot];
        }

        float[] anchors = { 0.16f, 0.84f, 0.38f, 0.62f };
        float offset = charactersPerPlayer > 1 ? (zeroBasedSlot - 0.5f) * 0.08f : 0f;
        return Mathf.Clamp01(anchors[zeroBasedPlayer] + offset);
    }

    private int GetCharactersPerPlayer(int count)
    {
        if (count <= 2) return 3;
        if (count == 3) return 2;
        return 1;
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
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(75, 190, 235, 255);
        camera.transform.position = new Vector3(0f, 0f, -10f);
        return camera;
    }

    private void DestroyNamedRoots(params string[] prefixesOrNames)
    {
        GameObject[] objects = FindAllGameObjects();
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject candidate = objects[i];
            if (candidate == null || candidate == gameObject || candidate.transform.parent != null)
            {
                continue;
            }

            for (int prefixIndex = 0; prefixIndex < prefixesOrNames.Length; prefixIndex++)
            {
                string prefix = prefixesOrNames[prefixIndex];
                if (candidate.name == prefix || candidate.name.StartsWith(prefix))
                {
                    SafeDestroy(candidate);
                    break;
                }
            }
        }
    }

    private void DestroyObjects<T>() where T : Object
    {
        T[] objects = FindAll<T>();
        for (int i = 0; i < objects.Length; i++)
        {
            Component component = objects[i] as Component;
            if (component != null && component.gameObject == gameObject)
            {
                continue;
            }

            SafeDestroy(objects[i]);
        }
    }

    private void DestroyRootObjects<T>() where T : Component
    {
        T[] components = FindAll<T>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null || components[i].gameObject == gameObject)
            {
                continue;
            }

            Transform root = components[i].transform.root;
            if (root != null && root.gameObject != gameObject)
            {
                SafeDestroy(root.gameObject);
            }
        }
    }

    private void DestroyComponents<T>() where T : Component
    {
        T[] components = FindAll<T>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null || components[i].gameObject == gameObject)
            {
                continue;
            }

            SafeDestroy(components[i]);
        }
    }

    private static void SafeDestroy(Object target)
    {
        if (target == null)
        {
            return;
        }

        Object.DestroyImmediate(target);
    }

    private static GameObject[] FindAllGameObjects()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<GameObject>();
#endif
    }

    private static T[] FindAll<T>() where T : Object
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>();
#endif
    }

    private static T FindAny<T>() where T : Object
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
