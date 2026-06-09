using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CommonHeadItemSpawner : MonoBehaviour
{
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private TerrainManager terrain;
    [SerializeField] private int deterministicSeed = 6975;
    [SerializeField, Min(1)] private int spawnEveryTurns = 3;
    [SerializeField, Min(1)] private int targetCountPerType = 2;
    [SerializeField, Min(0f)] private float mapEdgePaddingWorld = 2f;
    [SerializeField, Min(0f)] private float waterPaddingWorld = 1.5f;
    [SerializeField, Min(0f)] private float minCharacterDistance = 2f;
    [SerializeField, Min(0f)] private float minItemDistance = 1.5f;
    [SerializeField, Min(0f)] private float minHazardDistance = 1.5f;
    [SerializeField, Min(0.05f)] private float minimumSpawnHeight = 0.3f;
    [SerializeField, Min(0.05f)] private float maximumSpawnHeight = 0.6f;
    [SerializeField, Min(0.05f)] private float maximumSurfaceHeightDifference = 0.35f;
    [SerializeField, Min(1)] private int maxSpawnAttempts = 30;
    [SerializeField] private Sprite attackSprite;
    [SerializeField] private Sprite mobilitySprite;
    [SerializeField] private Sprite terrainCreationSprite;

    private int turnsObserved;
    private System.Random random;

    public int TargetTotalItems => targetCountPerType * 3;

    public void Configure(TurnManager manager, TerrainManager terrainManager, int seed)
    {
        Unsubscribe();
        turnManager = manager;
        terrain = terrainManager;
        deterministicSeed = seed;
        random = new System.Random(deterministicSeed);
        LoadFallbackSprites();
        Subscribe();
        RefillMissingItems();
    }

    public void Configure(TurnManager manager, TerrainManager terrainManager)
    {
        Configure(manager, terrainManager, deterministicSeed);
    }

    private void OnEnable()
    {
        if (random == null)
        {
            random = new System.Random(deterministicSeed);
        }
        LoadFallbackSprites();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (turnManager == null)
        {
            return;
        }

        turnManager.TurnStarted -= HandleTurnStarted;
        turnManager.TurnStarted += HandleTurnStarted;
    }

    private void Unsubscribe()
    {
        if (turnManager != null)
        {
            turnManager.TurnStarted -= HandleTurnStarted;
        }
    }

    private void HandleTurnStarted(TurnCharacterController currentCharacter)
    {
        turnsObserved++;
        if (turnsObserved % Mathf.Max(1, spawnEveryTurns) == 0)
        {
            RefillMissingItems();
        }
    }

    public void RefillMissingItems()
    {
        SpawnMissingType(CommonHeadType.Attack);
        SpawnMissingType(CommonHeadType.Mobility);
        SpawnMissingType(CommonHeadType.TerrainCreation);
    }

    private void SpawnMissingType(CommonHeadType type)
    {
        int missing = Mathf.Max(0, targetCountPerType - CommonHeadItem.GetActiveCount(type));
        for (int i = 0; i < missing; i++)
        {
            if (!TrySpawn(type))
            {
                Debug.Log($"Common head spawn skipped for {type}: no valid terrain position.");
                break;
            }
        }
    }

    private bool TrySpawn(CommonHeadType type)
    {
        if (terrain == null)
        {
            terrain = FindAny<TerrainManager>();
        }
        if (terrain == null || random == null)
        {
            return false;
        }

        for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
        {
            GetWeightedHorizontalBand(out float minX, out float maxX);
            TerrainItemSpawnRequest request = new TerrainItemSpawnRequest
            {
                minWorldX = minX,
                maxWorldX = maxX,
                halfExtents = new Vector2(0.38f, 0.45f),
                minimumHeightAboveSurface = minimumSpawnHeight,
                maximumHeightAboveSurface = maximumSpawnHeight,
                waterPaddingWorld = waterPaddingWorld,
                maximumSurfaceHeightDifference = maximumSurfaceHeightDifference,
                clearanceSampleStepWorld = 0.16f,
                randomAttempts = 8,
                exclusions = BuildExclusions()
            };

            if (!terrain.FindValidItemSpawn(request, random, out Vector2 spawnPosition))
            {
                continue;
            }

            CommonHeadItem spawnedItem = CommonHeadItem.Create(type, spawnPosition, GetSprite(type));
            spawnedItem.RefreshIgnoredCharacterCollisions();
            Debug.Log(
                $"Spawned {type} common head at {spawnPosition}. " +
                $"{CommonHeadItem.GetActiveCount(type)}/{targetCountPerType} for this type, " +
                $"{CommonHeadItem.ActiveCount}/{TargetTotalItems} total.");
            return true;
        }

        return false;
    }

    private List<TerrainSpawnExclusion> BuildExclusions()
    {
        List<TerrainSpawnExclusion> exclusions = new List<TerrainSpawnExclusion>();
        AddExclusions<TurnCharacterController>(exclusions, minCharacterDistance);
        AddExclusions<CommonHeadItem>(exclusions, minItemDistance);
        AddExclusions<GroundHazardZone>(exclusions, minHazardDistance);
        return exclusions;
    }

    private void GetWeightedHorizontalBand(out float minX, out float maxX)
    {
        Bounds bounds = terrain.GetTerrainBounds();
        float left = bounds.min.x + mapEdgePaddingWorld;
        float right = bounds.max.x - mapEdgePaddingWorld;
        float width = Mathf.Max(1f, right - left);
        float roll = (float)random.NextDouble();

        if (roll < 0.5f)
        {
            minX = left + width * 0.35f;
            maxX = left + width * 0.65f;
            return;
        }

        bool useLeft = random.NextDouble() < 0.5;
        if (roll < 0.8f)
        {
            minX = left + width * (useLeft ? 0.15f : 0.65f);
            maxX = left + width * (useLeft ? 0.35f : 0.85f);
            return;
        }

        minX = left + width * (useLeft ? 0f : 0.85f);
        maxX = left + width * (useLeft ? 0.15f : 1f);
    }

    private static void AddExclusions<T>(
        List<TerrainSpawnExclusion> exclusions,
        float minimumDistance) where T : Component
    {
        if (minimumDistance <= 0f)
        {
            return;
        }

#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        T[] objects = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        T[] objects = Object.FindObjectsOfType<T>();
#endif
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
            {
                exclusions.Add(new TerrainSpawnExclusion(objects[i].transform.position, minimumDistance));
            }
        }
    }

    private Sprite GetSprite(CommonHeadType type)
    {
        switch (type)
        {
            case CommonHeadType.Attack: return attackSprite;
            case CommonHeadType.Mobility: return mobilitySprite;
            default: return terrainCreationSprite;
        }
    }

    private void LoadFallbackSprites()
    {
        if (attackSprite == null) attackSprite = Resources.Load<Sprite>("Sprites/Heads/common_head_attack_bomb");
        if (mobilitySprite == null) mobilitySprite = Resources.Load<Sprite>("Sprites/Heads/common_head_mobility_wing");
        if (terrainCreationSprite == null) terrainCreationSprite = Resources.Load<Sprite>("Sprites/Heads/common_head_terrain_cloud");
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
