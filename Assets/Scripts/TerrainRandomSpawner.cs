using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TerrainRandomSpawner : MonoBehaviour
{
    [SerializeField] private TerrainManager terrain;
    [SerializeField] private TurnCharacterController[] characters = new TurnCharacterController[0];
    [SerializeField] private int deterministicSeed = 6974;
    [SerializeField, Min(0f)] private float mapEdgePaddingWorld = 2.5f;
    [SerializeField, Min(0f)] private float waterPaddingWorld = 1.5f;
    [SerializeField, Min(0f)] private float minCharacterSpacingWorld = 2.5f;
    [SerializeField, Min(0f)] private float spawnLiftWorld = 0.12f;
    [SerializeField, Min(0.05f)] private float clearanceSampleStepWorld = 0.2f;
    [SerializeField, Min(0.05f)] private float maximumSurfaceHeightDifference = 0.45f;
    [SerializeField, Min(1)] private int maxAttemptsPerCharacter = 120;

    private readonly List<Vector2> usedSpawnPositions = new List<Vector2>();
    private System.Random random;
    private bool spawned;

    private void Start()
    {
        if (!spawned)
        {
            if (terrain == null) terrain = FindTerrain();
            if (characters == null || characters.Length == 0) RefreshCharactersFromScene();
            SpawnCharacters();
        }
    }

    public void Configure(TerrainManager terrainManager, TurnCharacterController[] turnCharacters, int seed)
    {
        terrain = terrainManager;
        characters = turnCharacters ?? new TurnCharacterController[0];
        deterministicSeed = seed;
    }

    public void SpawnCharacters()
    {
        if (terrain == null || characters == null || characters.Length == 0)
        {
            return;
        }

        random = new System.Random(deterministicSeed);
        usedSpawnPositions.Clear();
        int playerCount = GetPlayerCount();

        for (int i = 0; i < characters.Length; i++)
        {
            TurnCharacterController character = characters[i];
            if (character == null)
            {
                continue;
            }

            ObjectHeadTeamMember member = character.GetComponent<ObjectHeadTeamMember>();
            int playerIndex = member != null ? member.PlayerIndex : i + 1;
            int slotIndex = member != null ? member.TeamSlotIndex : 1;
            int zoneIndex = Mathf.Clamp((slotIndex - 1) * playerCount + (playerIndex - 1), 0, characters.Length - 1);
            if (TryFindSpawnPosition(character, zoneIndex, characters.Length, out Vector2 spawnPosition))
            {
                PlaceCharacter(character, spawnPosition);
                usedSpawnPositions.Add(spawnPosition);
            }
            else
            {
                Debug.LogWarning($"No fully safe deterministic spawn found for {character.name}; keeping its fallback position.");
            }
        }

        spawned = true;
    }

    private bool TryFindSpawnPosition(
        TurnCharacterController character,
        int zoneIndex,
        int zoneCount,
        out Vector2 spawnPosition)
    {
        float terrainWidth = terrain.WidthPx / (float)terrain.PixelsPerUnit;
        float normalizedPadding = terrainWidth > 0f ? mapEdgePaddingWorld / terrainWidth : 0f;
        float zoneMin = Mathf.Lerp(normalizedPadding, 1f - normalizedPadding, zoneIndex / (float)Mathf.Max(1, zoneCount));
        float zoneMax = Mathf.Lerp(normalizedPadding, 1f - normalizedPadding, (zoneIndex + 1f) / Mathf.Max(1, zoneCount));
        zoneMin = Mathf.Clamp01(zoneMin);
        zoneMax = Mathf.Clamp01(zoneMax);

        for (int attempt = 0; attempt < maxAttemptsPerCharacter; attempt++)
        {
            float normalizedX = Mathf.Lerp(zoneMin, zoneMax, (float)random.NextDouble());
            if (TryBuildSafeCandidate(character, normalizedX, out Vector2 candidate))
            {
                spawnPosition = candidate;
                return true;
            }
        }

        int scanSteps = 40;
        for (int step = 0; step <= scanSteps; step++)
        {
            float normalizedX = Mathf.Lerp(zoneMin, zoneMax, step / (float)scanSteps);
            if (TryBuildSafeCandidate(character, normalizedX, out Vector2 candidate))
            {
                spawnPosition = candidate;
                return true;
            }
        }

        spawnPosition = character.transform.position;
        return false;
    }

    private bool TryBuildSafeCandidate(
        TurnCharacterController character,
        float normalizedX,
        out Vector2 candidate)
    {
        float worldX = terrain.TerrainOriginWorld.x +
                       terrain.WidthPx / (float)terrain.PixelsPerUnit * Mathf.Clamp01(normalizedX);
        if (!TryFindSurfaceAtWorldX(worldX, out float surfaceY))
        {
            candidate = Vector2.zero;
            return false;
        }

        Collider2D collider = character.GetComponent<Collider2D>();
        Vector2 extents = collider != null ? collider.bounds.extents : new Vector2(0.36f, 0.58f);
        candidate = new Vector2(worldX, surfaceY + extents.y + spawnLiftWorld);
        if (candidate.y < terrain.TerrainOriginWorld.y + waterPaddingWorld ||
            !HasStableSupport(candidate, extents) ||
            !HasClearance(candidate, extents) ||
            !IsFarEnoughFromOtherSpawns(candidate))
        {
            return false;
        }

        return true;
    }

    private bool HasStableSupport(Vector2 candidate, Vector2 extents)
    {
        float supportHalfWidth = extents.x + 0.12f;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        const int samples = 5;

        for (int i = 0; i < samples; i++)
        {
            float x = candidate.x + Mathf.Lerp(-supportHalfWidth, supportHalfWidth, i / (float)(samples - 1));
            if (!TryFindSurfaceAtWorldX(x, out float surfaceY))
            {
                return false;
            }

            minY = Mathf.Min(minY, surfaceY);
            maxY = Mathf.Max(maxY, surfaceY);
        }

        return maxY - minY <= maximumSurfaceHeightDifference;
    }

    private bool HasClearance(Vector2 candidate, Vector2 extents)
    {
        float left = candidate.x - extents.x * 0.9f;
        float right = candidate.x + extents.x * 0.9f;
        float bottom = candidate.y - extents.y + 0.08f;
        float top = candidate.y + extents.y + 0.15f;

        for (float y = bottom; y <= top; y += clearanceSampleStepWorld)
        {
            for (float x = left; x <= right; x += clearanceSampleStepWorld)
            {
                if (terrain.IsSolidWorld(new Vector2(x, y)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryFindSurfaceAtWorldX(float worldX, out float surfaceY)
    {
        Vector2Int pixel = terrain.WorldToPixel(new Vector2(worldX, terrain.TerrainOriginWorld.y));
        if (pixel.x < 0 || pixel.x >= terrain.WidthPx)
        {
            surfaceY = 0f;
            return false;
        }

        for (int y = terrain.HeightPx - 2; y >= 0; y--)
        {
            Vector2 solid = terrain.PixelToWorld(new Vector2Int(pixel.x, y));
            Vector2 above = terrain.PixelToWorld(new Vector2Int(pixel.x, y + 1));
            if (terrain.IsSolidWorld(solid) && !terrain.IsSolidWorld(above))
            {
                surfaceY = above.y;
                return true;
            }
        }

        surfaceY = 0f;
        return false;
    }

    private bool IsFarEnoughFromOtherSpawns(Vector2 candidate)
    {
        float minimumSquared = minCharacterSpacingWorld * minCharacterSpacingWorld;
        for (int i = 0; i < usedSpawnPositions.Count; i++)
        {
            if ((candidate - usedSpawnPositions[i]).sqrMagnitude < minimumSquared)
            {
                return false;
            }
        }

        return true;
    }

    private void PlaceCharacter(TurnCharacterController character, Vector2 spawnPosition)
    {
        character.transform.position = new Vector3(spawnPosition.x, spawnPosition.y, character.transform.position.z);
        Rigidbody2D body = character.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    private int GetPlayerCount()
    {
        HashSet<int> players = new HashSet<int>();
        for (int i = 0; i < characters.Length; i++)
        {
            ObjectHeadTeamMember member = characters[i] != null
                ? characters[i].GetComponent<ObjectHeadTeamMember>()
                : null;
            if (member != null)
            {
                players.Add(member.PlayerIndex);
            }
        }

        return Mathf.Max(1, players.Count);
    }

    private void RefreshCharactersFromScene()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        characters = Object.FindObjectsByType<TurnCharacterController>(FindObjectsSortMode.None);
#else
        characters = Object.FindObjectsOfType<TurnCharacterController>();
#endif
    }

    private static TerrainManager FindTerrain()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TerrainManager>();
#else
        return Object.FindObjectOfType<TerrainManager>();
#endif
    }
}
