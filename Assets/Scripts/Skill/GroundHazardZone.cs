using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GroundHazardZone : MonoBehaviour
{
    private const float DefaultSampleSpacing = 0.25f;
    private const float SurfaceOffset = 0.05f;
    private const float MaxRunHeightDifference = 0.35f;
    private static Sprite whiteSprite;

    private readonly Dictionary<CharacterCombat, int> overlapCounts = new Dictionary<CharacterCombat, int>();
    private readonly Dictionary<CharacterCombat, int> lastDamagedTurn = new Dictionary<CharacterCombat, int>();
    private readonly List<GameObject> segments = new List<GameObject>();

    private TerrainManager terrain;
    private TurnManager turnManager;
    private CharacterCombat owner;
    private Vector2 center;
    private float lengthWorld;
    private float thicknessWorld;
    private float sampleSpacing = DefaultSampleSpacing;
    private int remainingRounds;
    private int damagePerTurn;
    private float slowMultiplier;
    private Color color;
    private int createdRound;
    private int lastProcessedRound;

    public int RemainingRounds => remainingRounds;
    public int SegmentCount => segments.Count;

    public static GroundHazardZone Create(
        Vector2 position,
        float length,
        float thickness,
        int durationRounds,
        int turnDamage,
        float movementSlowMultiplier,
        Color zoneColor,
        CharacterCombat zoneOwner,
        TurnManager manager)
    {
        GameObject zoneObject = new GameObject("GroundHazardZone");
        GroundHazardZone zone = zoneObject.AddComponent<GroundHazardZone>();
        zone.Initialize(
            position,
            length,
            thickness,
            durationRounds,
            turnDamage,
            movementSlowMultiplier,
            zoneColor,
            zoneOwner,
            manager);
        return zone;
    }

    public void Initialize(
        Vector2 position,
        float length,
        float thickness,
        int durationRounds,
        int turnDamage,
        float movementSlowMultiplier,
        Color zoneColor,
        CharacterCombat zoneOwner,
        TurnManager manager)
    {
        center = position;
        transform.position = position;
        lengthWorld = Mathf.Max(0.5f, length);
        thicknessWorld = Mathf.Max(0.05f, thickness);
        remainingRounds = Mathf.Max(1, durationRounds);
        damagePerTurn = Mathf.Max(0, turnDamage);
        slowMultiplier = Mathf.Clamp(movementSlowMultiplier, 0.1f, 1f);
        color = zoneColor;
        owner = zoneOwner;
        turnManager = manager != null ? manager : FindAny<TurnManager>();
        terrain = FindAny<TerrainManager>();
        createdRound = turnManager != null ? turnManager.RoundSerial : 0;
        lastProcessedRound = createdRound;
        Subscribe();
        RebuildSegments();
        Debug.Log($"{name} created for {remainingRounds} rounds with {segments.Count} ground segments.");
    }

    private void OnDestroy()
    {
        Unsubscribe();
        ClearTrackedSlows();
    }

    public void NotifySegmentEnter(Collider2D other)
    {
        CharacterCombat combat = other != null ? other.GetComponentInParent<CharacterCombat>() : null;
        if (combat == null || combat.IsDead)
        {
            return;
        }

        overlapCounts.TryGetValue(combat, out int count);
        overlapCounts[combat] = count + 1;
        if (count == 0)
        {
            ApplyToCurrentCharacter(combat);
        }
    }

    public void NotifySegmentExit(Collider2D other)
    {
        CharacterCombat combat = other != null ? other.GetComponentInParent<CharacterCombat>() : null;
        if (combat == null || !overlapCounts.TryGetValue(combat, out int count))
        {
            return;
        }

        count--;
        if (count > 0)
        {
            overlapCounts[combat] = count;
            return;
        }

        overlapCounts.Remove(combat);
        combat.GetComponent<TurnCharacterController>()?.ClearHazardSlow();
    }

    private void HandleTurnStarted(TurnCharacterController current)
    {
        if (current == null)
        {
            return;
        }

        CharacterCombat combat = current.GetComponent<CharacterCombat>();
        if (combat != null && overlapCounts.ContainsKey(combat))
        {
            ApplyToCurrentCharacter(combat);
        }
    }

    private void HandleTurnEnded(TurnCharacterController ending)
    {
        ending?.ClearHazardSlow();
    }

    private void HandleRoundStarted(int round)
    {
        if (round <= createdRound || round <= lastProcessedRound)
        {
            return;
        }

        lastProcessedRound = round;
        remainingRounds = Mathf.Max(0, remainingRounds - 1);
        Debug.Log($"{name} remaining rounds: {remainingRounds}");
        if (remainingRounds <= 0)
        {
            Destroy(gameObject);
            return;
        }

        RebuildSegments();
    }

    private void ApplyToCurrentCharacter(CharacterCombat combat)
    {
        if (turnManager == null ||
            turnManager.CurrentCharacter == null ||
            turnManager.CurrentCharacter.gameObject != combat.gameObject)
        {
            return;
        }

        TurnCharacterController controller = combat.GetComponent<TurnCharacterController>();
        if (slowMultiplier < 0.999f)
        {
            controller?.SetHazardMoveSpeedMultiplier(slowMultiplier);
        }

        if (damagePerTurn <= 0)
        {
            return;
        }

        int turn = turnManager.TurnSerial;
        if (lastDamagedTurn.TryGetValue(combat, out int damagedTurn) && damagedTurn == turn)
        {
            return;
        }

        lastDamagedTurn[combat] = turn;
        combat.TakeDamage(damagePerTurn);
    }

    private void RebuildSegments()
    {
        ClearSegments();
        if (terrain == null)
        {
            terrain = FindAny<TerrainManager>();
        }

        if (terrain == null || !terrain.IsInitialized)
        {
            Destroy(gameObject);
            return;
        }

        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(lengthWorld / sampleSpacing) + 1);
        float startX = center.x - lengthWorld * 0.5f;
        bool runActive = false;
        float runStartX = 0f;
        float runEndX = 0f;
        float runYSum = 0f;
        int runSamples = 0;
        float previousY = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
            float x = Mathf.Lerp(startX, startX + lengthWorld, t);
            bool valid = TryFindSurface(x, out float surfaceY);
            bool continuesRun = valid && (!runActive || Mathf.Abs(surfaceY - previousY) <= MaxRunHeightDifference);

            if (!continuesRun)
            {
                FinishRun(runActive, runStartX, runEndX, runYSum, runSamples);
                runActive = false;
                runYSum = 0f;
                runSamples = 0;
            }

            if (!valid)
            {
                continue;
            }

            if (!runActive)
            {
                runActive = true;
                runStartX = x;
            }

            runEndX = x;
            runYSum += surfaceY;
            runSamples++;
            previousY = surfaceY;
        }

        FinishRun(runActive, runStartX, runEndX, runYSum, runSamples);
        if (segments.Count == 0)
        {
            Debug.Log($"{name} removed because no supported ground segments remain.");
            Destroy(gameObject);
        }
    }

    private bool TryFindSurface(float x, out float surfaceY)
    {
        Vector2Int centerPixel = terrain.WorldToPixel(new Vector2(x, center.y + 1.5f));
        int pixelX = centerPixel.x;
        if (pixelX < 0 || pixelX >= terrain.WidthPx)
        {
            surfaceY = 0f;
            return false;
        }

        int startY = Mathf.Clamp(centerPixel.y, 1, terrain.HeightPx - 1);
        int minimumY = Mathf.Max(0, terrain.WorldToPixel(new Vector2(x, center.y - 4f)).y);
        for (int y = startY; y >= minimumY; y--)
        {
            Vector2 solidPoint = terrain.PixelToWorld(new Vector2Int(pixelX, y));
            if (!terrain.IsSolidWorld(solidPoint))
            {
                continue;
            }

            Vector2 abovePoint = terrain.PixelToWorld(new Vector2Int(pixelX, y + 1));
            if (terrain.IsSolidWorld(abovePoint))
            {
                continue;
            }

            surfaceY = abovePoint.y + SurfaceOffset;
            return true;
        }

        surfaceY = 0f;
        return false;
    }

    private void FinishRun(bool active, float startX, float endX, float ySum, int samples)
    {
        if (!active || samples <= 0)
        {
            return;
        }

        float width = Mathf.Max(sampleSpacing * 0.8f, endX - startX + sampleSpacing * 0.8f);
        float y = ySum / samples;
        GameObject segmentObject = new GameObject($"GroundHazardSegment_{segments.Count + 1}");
        segmentObject.transform.SetParent(transform, false);
        segmentObject.transform.position = new Vector3((startX + endX) * 0.5f, y, 0f);

        SpriteRenderer renderer = segmentObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetWhiteSprite();
        renderer.color = color;
        renderer.sortingOrder = 20;
        segmentObject.transform.localScale = new Vector3(width, thicknessWorld, 1f);

        BoxCollider2D box = segmentObject.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = Vector2.one;
        GroundHazardSegment segment = segmentObject.AddComponent<GroundHazardSegment>();
        segment.Initialize(this);
        segments.Add(segmentObject);
    }

    private void ClearSegments()
    {
        ClearTrackedSlows();
        overlapCounts.Clear();
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (segments[i] == null)
            {
                continue;
            }

            Collider2D collider = segments[i].GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            Destroy(segments[i]);
        }

        segments.Clear();
    }

    private void ClearTrackedSlows()
    {
        foreach (CharacterCombat combat in overlapCounts.Keys)
        {
            if (combat != null)
            {
                combat.GetComponent<TurnCharacterController>()?.ClearHazardSlow();
            }
        }
    }

    private void Subscribe()
    {
        if (turnManager != null)
        {
            turnManager.TurnStarted -= HandleTurnStarted;
            turnManager.TurnStarted += HandleTurnStarted;
            turnManager.TurnEnded -= HandleTurnEnded;
            turnManager.TurnEnded += HandleTurnEnded;
            turnManager.RoundStarted -= HandleRoundStarted;
            turnManager.RoundStarted += HandleRoundStarted;
        }

        if (terrain != null)
        {
            terrain.TerrainChanged -= RebuildSegments;
            terrain.TerrainChanged += RebuildSegments;
        }
    }

    private void Unsubscribe()
    {
        if (turnManager != null)
        {
            turnManager.TurnStarted -= HandleTurnStarted;
            turnManager.TurnEnded -= HandleTurnEnded;
            turnManager.RoundStarted -= HandleRoundStarted;
        }

        if (terrain != null)
        {
            terrain.TerrainChanged -= RebuildSegments;
        }
    }

    private static Sprite GetWhiteSprite()
    {
        if (whiteSprite != null)
        {
            return whiteSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        whiteSprite.hideFlags = HideFlags.HideAndDontSave;
        return whiteSprite;
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
