using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CommonHeadItemSpawner : MonoBehaviour
{
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private TerrainManager terrain;
    [SerializeField] private Transform spawnRoot;
    [SerializeField, Min(1)] private int spawnEveryTurns = 3;
    [SerializeField, Min(1)] private int maxActiveItems = 3;
    [SerializeField, Min(0f)] private float minCharacterDistance = 2f;
    [SerializeField, Min(0f)] private float minItemDistance = 1.5f;
    [SerializeField, Min(0f)] private float minHazardDistance = 1.5f;
    [SerializeField] private Sprite attackSprite;
    [SerializeField] private Sprite mobilitySprite;
    [SerializeField] private Sprite terrainCreationSprite;

    private int turnsObserved;

    public void Configure(TurnManager manager, TerrainManager terrainManager, Transform itemSpawnRoot)
    {
        Unsubscribe();
        turnManager = manager;
        terrain = terrainManager;
        spawnRoot = itemSpawnRoot;
        LoadFallbackSprites();
        Subscribe();
    }

    public void Configure(TurnManager manager, Transform itemSpawnRoot)
    {
        Configure(manager, FindAny<TerrainManager>(), itemSpawnRoot);
    }

    private void OnEnable()
    {
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
            TrySpawn();
        }
    }

    public bool TrySpawn()
    {
        if (CommonHeadItem.ActiveCount >= maxActiveItems || spawnRoot == null || spawnRoot.childCount == 0)
        {
            return false;
        }

        List<Transform> candidates = new List<Transform>();
        for (int i = 0; i < spawnRoot.childCount; i++)
        {
            Transform point = spawnRoot.GetChild(i);
            if (point != null && IsValidSpawnPoint(point.position))
            {
                candidates.Add(point);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.Log("Common head spawn skipped: no valid ItemSpawnPoint.");
            return false;
        }

        Transform selected = candidates[Random.Range(0, candidates.Count)];
        CommonHeadType type = (CommonHeadType)Random.Range(
            (int)CommonHeadType.Attack,
            (int)CommonHeadType.TerrainCreation + 1);
        CommonHeadItem.Create(type, selected.position, GetSprite(type));
        Debug.Log($"Spawned {type} common head at {selected.name}. Active: {CommonHeadItem.ActiveCount}/{maxActiveItems}");
        return true;
    }

    private bool IsValidSpawnPoint(Vector2 position)
    {
        if (terrain == null)
        {
            terrain = FindAny<TerrainManager>();
        }

        bool hasGround = false;
        if (terrain != null)
        {
            for (float distance = 0.1f; distance <= 2f; distance += 0.1f)
            {
                if (terrain.IsSolidWorld(position + Vector2.down * distance))
                {
                    hasGround = true;
                    break;
                }
            }
        }

        if (!hasGround)
        {
            return false;
        }

        if (IsNearAny<TurnCharacterController>(position, minCharacterDistance) ||
            IsNearAny<CommonHeadItem>(position, minItemDistance) ||
            IsNearAny<GroundHazardZone>(position, minHazardDistance))
        {
            return false;
        }

        return true;
    }

    private static bool IsNearAny<T>(Vector2 position, float distance) where T : Component
    {
        if (distance <= 0f)
        {
            return false;
        }

#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        T[] objects = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        T[] objects = Object.FindObjectsOfType<T>();
#endif
        float distanceSquared = distance * distance;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null && ((Vector2)objects[i].transform.position - position).sqrMagnitude < distanceSquared)
            {
                return true;
            }
        }

        return false;
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
        if (attackSprite == null) attackSprite = Resources.Load<Sprite>("Sprites/Heads/head_bomb_red");
        if (mobilitySprite == null) mobilitySprite = Resources.Load<Sprite>("Sprites/Heads/head_bulb_on");
        if (terrainCreationSprite == null) terrainCreationSprite = Resources.Load<Sprite>("Sprites/Heads/head_seed");
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
