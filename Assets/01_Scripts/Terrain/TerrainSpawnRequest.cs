using System.Collections.Generic;
using UnityEngine;

public struct TerrainSpawnExclusion
{
    public Vector2 position;
    public float minimumDistance;

    public TerrainSpawnExclusion(Vector2 worldPosition, float distance)
    {
        position = worldPosition;
        minimumDistance = Mathf.Max(0f, distance);
    }
}

public struct TerrainCharacterSpawnRequest
{
    public float minWorldX;
    public float maxWorldX;
    public Vector2 colliderExtents;
    public float spawnLiftWorld;
    public float waterPaddingWorld;
    public float maximumSurfaceHeightDifference;
    public float clearanceSampleStepWorld;
    public int randomAttempts;
    public List<TerrainSpawnExclusion> exclusions;
}

public struct TerrainItemSpawnRequest
{
    public float minWorldX;
    public float maxWorldX;
    public Vector2 halfExtents;
    public float minimumHeightAboveSurface;
    public float maximumHeightAboveSurface;
    public float waterPaddingWorld;
    public float maximumSurfaceHeightDifference;
    public float clearanceSampleStepWorld;
    public int randomAttempts;
    public List<TerrainSpawnExclusion> exclusions;
}
