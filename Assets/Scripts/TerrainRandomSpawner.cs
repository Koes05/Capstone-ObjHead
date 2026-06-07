using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TerrainRandomSpawner : MonoBehaviour
{
    private struct PlacedCharacter
    {
        public int playerIndex;
        public Vector2 position;

        public PlacedCharacter(int player, Vector2 worldPosition)
        {
            playerIndex = player;
            position = worldPosition;
        }
    }

    [SerializeField] private TerrainManager terrain;
    [SerializeField] private TurnCharacterController[] characters = new TurnCharacterController[0];
    [SerializeField] private int deterministicSeed = 6974;
    [SerializeField, Min(0f)] private float mapEdgePaddingWorld = 2.5f;
    [SerializeField, Min(0f)] private float waterPaddingWorld = 1.5f;
    [SerializeField, Min(0f)] private float sameTeamMinimumDistance = 2f;
    [SerializeField, Min(0f)] private float sameTeamAnchorSearchMinimum = 3f;
    [SerializeField, Min(0f)] private float sameTeamAnchorSearchMaximum = 5f;
    [SerializeField, Min(0f)] private float sameTeamMaximumDistance = 6f;
    [SerializeField, Min(0f)] private float enemyMinimumDistance = 6f;
    [SerializeField, Min(0.03f)] private float spawnLiftWorld = 0.06f;
    [SerializeField, Min(0.05f)] private float clearanceSampleStepWorld = 0.18f;
    [SerializeField, Min(0.05f)] private float maximumSurfaceHeightDifference = 0.45f;
    [SerializeField, Min(1)] private int maxAttemptsPerCharacter = 140;

    private readonly List<PlacedCharacter> placedCharacters = new List<PlacedCharacter>();
    private readonly Dictionary<int, Vector2> teamAnchors = new Dictionary<int, Vector2>();
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
        placedCharacters.Clear();
        teamAnchors.Clear();

        SortedDictionary<int, List<TurnCharacterController>> teams = BuildTeams();
        int playerCount = Mathf.Max(1, teams.Count);
        int playerOrder = 0;

        foreach (KeyValuePair<int, List<TurnCharacterController>> pair in teams)
        {
            int playerIndex = pair.Key;
            List<TurnCharacterController> team = pair.Value;
            team.Sort(CompareTeamSlot);
            GetPlayerRegion(playerOrder, playerCount, out float regionMinX, out float regionMaxX);

            for (int slot = 0; slot < team.Count; slot++)
            {
                TurnCharacterController character = team[slot];
                if (character == null)
                {
                    continue;
                }

                Vector2 spawn;
                bool found = slot == 0
                    ? TryFindTeamAnchor(character, playerIndex, regionMinX, regionMaxX, out spawn)
                    : TryFindNearTeamAnchor(character, playerIndex, regionMinX, regionMaxX, out spawn);

                if (!found)
                {
                    found = TryFindEmergencyTeamSpawn(
                        character,
                        playerIndex,
                        regionMinX,
                        regionMaxX,
                        out spawn);
                }

                if (!found)
                {
                    Debug.LogError($"No safe terrain spawn found for {character.name}.");
                    continue;
                }

                PlaceCharacter(character, spawn);
                placedCharacters.Add(new PlacedCharacter(playerIndex, spawn));
                if (slot == 0)
                {
                    teamAnchors[playerIndex] = spawn;
                }
            }

            playerOrder++;
        }

        spawned = true;
    }

    private bool TryFindTeamAnchor(
        TurnCharacterController character,
        int playerIndex,
        float regionMinX,
        float regionMaxX,
        out Vector2 spawn)
    {
        TerrainCharacterSpawnRequest request = BuildRequest(
            character,
            playerIndex,
            regionMinX,
            regionMaxX);
        return terrain.FindValidCharacterSpawn(request, random, out spawn);
    }

    private bool TryFindNearTeamAnchor(
        TurnCharacterController character,
        int playerIndex,
        float regionMinX,
        float regionMaxX,
        out Vector2 spawn)
    {
        spawn = character.transform.position;
        if (!teamAnchors.TryGetValue(playerIndex, out Vector2 anchor))
        {
            return TryFindTeamAnchor(character, playerIndex, regionMinX, regionMaxX, out spawn);
        }

        bool preferLeft = random.NextDouble() < 0.5;
        for (int pass = 0; pass < 4; pass++)
        {
            bool searchLeft = pass % 2 == 0 ? preferLeft : !preferLeft;
            float distanceMin = pass < 2 ? sameTeamAnchorSearchMinimum : sameTeamMinimumDistance;
            float distanceMax = pass < 2 ? sameTeamAnchorSearchMaximum : sameTeamMaximumDistance;
            float minX = searchLeft ? anchor.x - distanceMax : anchor.x + distanceMin;
            float maxX = searchLeft ? anchor.x - distanceMin : anchor.x + distanceMax;
            minX = Mathf.Max(regionMinX, minX);
            maxX = Mathf.Min(regionMaxX, maxX);
            if (maxX <= minX)
            {
                continue;
            }

            TerrainCharacterSpawnRequest request = BuildRequest(character, playerIndex, minX, maxX);
            if (!terrain.FindValidCharacterSpawn(request, random, out Vector2 candidate))
            {
                continue;
            }

            float anchorDistance = Vector2.Distance(candidate, anchor);
            if (anchorDistance >= sameTeamMinimumDistance &&
                anchorDistance <= sameTeamMaximumDistance)
            {
                spawn = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryFindEmergencyTeamSpawn(
        TurnCharacterController character,
        int playerIndex,
        float regionMinX,
        float regionMaxX,
        out Vector2 spawn)
    {
        spawn = character.transform.position;
        if (!teamAnchors.TryGetValue(playerIndex, out Vector2 anchor))
        {
            TerrainCharacterSpawnRequest regionRequest = BuildRequest(
                character,
                playerIndex,
                regionMinX,
                regionMaxX);
            regionRequest.maximumSurfaceHeightDifference =
                Mathf.Max(maximumSurfaceHeightDifference, 0.65f);
            regionRequest.randomAttempts = maxAttemptsPerCharacter * 2;
            return terrain.FindValidCharacterSpawn(regionRequest, random, out spawn);
        }

        float minX = Mathf.Max(regionMinX, anchor.x - sameTeamMaximumDistance);
        float maxX = Mathf.Min(regionMaxX, anchor.x + sameTeamMaximumDistance);
        TerrainCharacterSpawnRequest request = BuildRequest(character, playerIndex, minX, maxX);
        request.maximumSurfaceHeightDifference = Mathf.Max(maximumSurfaceHeightDifference, 0.65f);
        request.randomAttempts = Mathf.Max(24, maxAttemptsPerCharacter / 4);
        for (int pass = 0; pass < 8; pass++)
        {
            if (!terrain.FindValidCharacterSpawn(request, random, out Vector2 candidate))
            {
                continue;
            }

            float anchorDistance = Vector2.Distance(candidate, anchor);
            if (anchorDistance >= sameTeamMinimumDistance &&
                anchorDistance <= sameTeamMaximumDistance)
            {
                spawn = candidate;
                return true;
            }
        }

        return false;
    }

    private TerrainCharacterSpawnRequest BuildRequest(
        TurnCharacterController character,
        int playerIndex,
        float minX,
        float maxX)
    {
        Collider2D collider = character.GetComponent<Collider2D>();
        Vector2 extents = collider != null
            ? (Vector2)collider.bounds.extents
            : new Vector2(0.36f, 0.58f);
        List<TerrainSpawnExclusion> exclusions = new List<TerrainSpawnExclusion>();

        for (int i = 0; i < placedCharacters.Count; i++)
        {
            float minimumDistance = placedCharacters[i].playerIndex == playerIndex
                ? sameTeamMinimumDistance
                : enemyMinimumDistance;
            exclusions.Add(new TerrainSpawnExclusion(placedCharacters[i].position, minimumDistance));
        }

        return new TerrainCharacterSpawnRequest
        {
            minWorldX = minX,
            maxWorldX = maxX,
            colliderExtents = extents,
            spawnLiftWorld = spawnLiftWorld,
            waterPaddingWorld = waterPaddingWorld,
            maximumSurfaceHeightDifference = maximumSurfaceHeightDifference,
            clearanceSampleStepWorld = clearanceSampleStepWorld,
            randomAttempts = maxAttemptsPerCharacter,
            exclusions = exclusions
        };
    }

    private void GetPlayerRegion(int zeroBasedPlayerOrder, int playerCount, out float minX, out float maxX)
    {
        Bounds bounds = terrain.GetTerrainBounds();
        float left = bounds.min.x + mapEdgePaddingWorld;
        float right = bounds.max.x - mapEdgePaddingWorld;
        float usableWidth = Mathf.Max(1f, right - left);

        if (playerCount == 2)
        {
            float contestWidth = usableWidth * 0.2f;
            float sideWidth = (usableWidth - contestWidth) * 0.5f;
            if (zeroBasedPlayerOrder == 0)
            {
                minX = left;
                maxX = left + sideWidth;
            }
            else
            {
                minX = right - sideWidth;
                maxX = right;
            }
            return;
        }

        float gap = Mathf.Min(0.75f, usableWidth * 0.025f);
        float regionWidth = (usableWidth - gap * (playerCount - 1)) / playerCount;
        minX = left + zeroBasedPlayerOrder * (regionWidth + gap);
        maxX = minX + regionWidth;
    }

    private SortedDictionary<int, List<TurnCharacterController>> BuildTeams()
    {
        SortedDictionary<int, List<TurnCharacterController>> teams =
            new SortedDictionary<int, List<TurnCharacterController>>();
        for (int i = 0; i < characters.Length; i++)
        {
            TurnCharacterController character = characters[i];
            if (character == null)
            {
                continue;
            }

            ObjectHeadTeamMember member = character.GetComponent<ObjectHeadTeamMember>();
            int playerIndex = member != null ? member.PlayerIndex : i + 1;
            if (!teams.TryGetValue(playerIndex, out List<TurnCharacterController> team))
            {
                team = new List<TurnCharacterController>();
                teams[playerIndex] = team;
            }
            team.Add(character);
        }
        return teams;
    }

    private static int CompareTeamSlot(TurnCharacterController left, TurnCharacterController right)
    {
        if (left == right) return 0;
        if (left == null) return 1;
        if (right == null) return -1;
        ObjectHeadTeamMember leftMember = left.GetComponent<ObjectHeadTeamMember>();
        ObjectHeadTeamMember rightMember = right.GetComponent<ObjectHeadTeamMember>();
        int leftSlot = leftMember != null ? leftMember.TeamSlotIndex : 999;
        int rightSlot = rightMember != null ? rightMember.TeamSlotIndex : 999;
        return leftSlot.CompareTo(rightSlot);
    }

    private static void PlaceCharacter(TurnCharacterController character, Vector2 spawnPosition)
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

    private static TerrainManager FindTerrain()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TerrainManager>();
#else
        return Object.FindObjectOfType<TerrainManager>();
#endif
    }
}
