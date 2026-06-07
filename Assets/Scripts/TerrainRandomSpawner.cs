using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TerrainRandomSpawner : MonoBehaviour
{
    [SerializeField] private TerrainManager terrain;
    [SerializeField] private TurnCharacterController[] characters = new TurnCharacterController[0];
    [SerializeField, Min(0f)] private float mapEdgePaddingWorld = 3f;
    [SerializeField, Min(0f)] private float minHorizontalSpacingWorld = 8f;
    [SerializeField, Min(0f)] private float spawnLiftWorld = 0.12f;
    [SerializeField, Min(1)] private int maxAttemptsPerCharacter = 80;

    private readonly List<Vector2> usedSpawnPositions = new List<Vector2>();

    private void Start()
    {
        if (terrain == null)
        {
            terrain = FindTerrain();
        }

        if (characters == null || characters.Length == 0)
        {
            RefreshCharactersFromScene();
        }

        SpawnCharacters();
    }

    private void SpawnCharacters()
    {
        if (terrain == null || characters == null || characters.Length == 0)
        {
            return;
        }

        usedSpawnPositions.Clear();

        for (int i = 0; i < characters.Length; i++)
        {
            TurnCharacterController character = characters[i];
            if (character == null)
            {
                continue;
            }

            if (TryFindSpawnPosition(character, out Vector2 spawnPosition))
            {
                PlaceCharacter(character, spawnPosition);
                usedSpawnPositions.Add(spawnPosition);
            }
        }
    }

    private bool TryFindSpawnPosition(TurnCharacterController character, out Vector2 spawnPosition)
    {
        int minPixelX = Mathf.Clamp(Mathf.RoundToInt(mapEdgePaddingWorld * terrain.PixelsPerUnit), 0, terrain.WidthPx - 1);
        int maxPixelX = Mathf.Clamp(terrain.WidthPx - 1 - minPixelX, 0, terrain.WidthPx - 1);

        if (maxPixelX <= minPixelX)
        {
            minPixelX = 0;
            maxPixelX = Mathf.Max(0, terrain.WidthPx - 1);
        }

        for (int attempt = 0; attempt < maxAttemptsPerCharacter; attempt++)
        {
            int pixelX = Random.Range(minPixelX, maxPixelX + 1);
            if (!TryFindSurfaceAtPixelX(pixelX, out Vector2 surfaceWorld))
            {
                continue;
            }

            Vector2 candidate = surfaceWorld + Vector2.up * (GetColliderHalfHeight(character) + spawnLiftWorld);
            if (IsFarEnoughFromOtherSpawns(candidate))
            {
                spawnPosition = candidate;
                return true;
            }
        }

        for (int pixelX = minPixelX; pixelX <= maxPixelX; pixelX += Mathf.Max(1, terrain.PixelsPerUnit))
        {
            if (!TryFindSurfaceAtPixelX(pixelX, out Vector2 surfaceWorld))
            {
                continue;
            }

            Vector2 candidate = surfaceWorld + Vector2.up * (GetColliderHalfHeight(character) + spawnLiftWorld);
            if (IsFarEnoughFromOtherSpawns(candidate))
            {
                spawnPosition = candidate;
                return true;
            }
        }

        spawnPosition = character.transform.position;
        return false;
    }

    private bool TryFindSurfaceAtPixelX(int pixelX, out Vector2 surfaceWorld)
    {
        for (int y = terrain.HeightPx - 1; y >= 0; y--)
        {
            Vector2 worldPoint = terrain.PixelToWorld(new Vector2Int(pixelX, y));
            if (!terrain.IsSolidWorld(worldPoint))
            {
                continue;
            }

            surfaceWorld = terrain.PixelToWorld(new Vector2Int(pixelX, y + 1));
            return true;
        }

        surfaceWorld = Vector2.zero;
        return false;
    }

    private float GetColliderHalfHeight(TurnCharacterController character)
    {
        Collider2D collider = character.GetComponent<Collider2D>();
        if (collider == null)
        {
            return 0.5f;
        }

        return Mathf.Max(0.1f, collider.bounds.extents.y);
    }

    private bool IsFarEnoughFromOtherSpawns(Vector2 candidate)
    {
        for (int i = 0; i < usedSpawnPositions.Count; i++)
        {
            if (Mathf.Abs(candidate.x - usedSpawnPositions[i].x) < minHorizontalSpacingWorld)
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

    private void RefreshCharactersFromScene()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        characters = Object.FindObjectsByType<TurnCharacterController>(FindObjectsSortMode.None);
#else
        characters = Object.FindObjectsOfType<TurnCharacterController>();
#endif
    }

    private TerrainManager FindTerrain()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TerrainManager>();
#else
        return Object.FindObjectOfType<TerrainManager>();
#endif
    }
}
