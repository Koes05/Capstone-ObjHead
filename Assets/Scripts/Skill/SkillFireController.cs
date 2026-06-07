using UnityEngine;
using System.Collections;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class SkillFireController : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float projectileRadius = 0.18f;
    [FormerlySerializedAs("spawnDistanceFromTarget")]
    [SerializeField, Min(0f)] private float spawnDistanceFromCharacter = 0.7f;
    [SerializeField, Min(0.1f)] private float minLaunchSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float maxLaunchSpeed = 13f;
    [SerializeField, Min(0f)] private float projectileGravityScale = 1f;
    [SerializeField, Min(0.1f)] private float projectileLifetime = 8f;
    [SerializeField] private Color projectileColor = new Color(1f, 0.25f, 0.05f, 1f);

    [Header("Impact")]
    [SerializeField, Min(0)] private int maxDamage = 35;
    [SerializeField, Min(1f)] private float explosionRadiusMultiplier = 10f;
    [SerializeField, Min(0.01f)] private float explosionFadeSeconds = 0.3f;
    [SerializeField] private Color explosionColor = new Color(1f, 0f, 0f, 0.55f);
    [SerializeField, Min(0f)] private float knockbackForce = 8f;

    [Header("Seed Vine")]
    [SerializeField, Min(0.1f)] private float minVineLengthWorld = 2.5f;
    [SerializeField, Min(0.1f)] private float maxVineLengthWorld = 6.5f;
    [SerializeField, Min(0.05f)] private float vineSegmentSpacingWorld = 0.25f;
    [SerializeField, Min(1)] private int vineSegmentRadiusPx = 9;
    [SerializeField, Min(0.01f)] private float vineGrowthIntervalSeconds = 0.045f;
    [SerializeField, Min(1f)] private float terrainPassLengthMultiplier = 1.75f;
    [SerializeField, Min(0f)] private float vineStartForwardOffsetWorld = 0.25f;
    [SerializeField, Min(0f)] private float vineStartSurfaceClearanceWorld = 0.05f;
    [SerializeField, Min(1)] private int colliderRebuildEveryNStamps = 2;
    [SerializeField, Min(0.01f)] private float colliderRebuildMaxInterval = 0.1f;

    private TurnCharacterController turnCharacter;
    private CharacterCombat ownerCombat;
    private AimController aimController;
    private PowerChargeController powerChargeController;
    private CharacterVisual characterVisual;
    private DemoSkillSelector skillSelector;
    private CommonHeadUseController commonHeadUseController;
    private TurnManager turnManager;
    private bool hasFiredThisTurn;
    private int observedTurnSerial = -1;

    private void Awake()
    {
        turnCharacter = GetComponent<TurnCharacterController>();
        ownerCombat = GetComponent<CharacterCombat>();
        aimController = GetComponent<AimController>();
        powerChargeController = GetComponent<PowerChargeController>();
        characterVisual = GetComponent<CharacterVisual>();
        skillSelector = GetComponent<DemoSkillSelector>();
        commonHeadUseController = GetComponent<CommonHeadUseController>();
        turnManager = FindTurnManager();
    }

    private void Update()
    {
        if (turnManager == null)
        {
            turnManager = FindTurnManager();
        }

        if (turnManager != null && observedTurnSerial != turnManager.TurnSerial)
        {
            observedTurnSerial = turnManager.TurnSerial;
            hasFiredThisTurn = false;
        }

        if (commonHeadUseController == null)
        {
            commonHeadUseController = GetComponent<CommonHeadUseController>();
        }

        if (turnCharacter == null ||
            powerChargeController == null ||
            hasFiredThisTurn ||
            (commonHeadUseController != null && commonHeadUseController.HasSelectedCommonHead) ||
            turnManager == null ||
            !turnManager.CanCharacterFire(turnCharacter))
        {
            return;
        }

        if (powerChargeController.ConsumeReleasedPower(out float power))
        {
            Fire(power);
        }
    }

    public void Fire(float normalizedPower)
    {
        if (aimController == null)
        {
            return;
        }

        aimController.ConfirmFacingFromAim();

        if (skillSelector != null && !skillSelector.CanUseSelectedSkill())
        {
            Debug.Log($"{name} cannot fire skill {skillSelector.SelectedSkillIndex + 1}: cooldown {skillSelector.GetRemainingCooldown(skillSelector.SelectedSkillIndex)}.");
            return;
        }

        if (turnManager == null || !turnManager.TryBeginAction(turnCharacter))
        {
            return;
        }

        hasFiredThisTurn = true;

        Vector2 direction = aimController.AimDirection;
        Vector2 spawnPosition = aimController.AimOrigin + direction * spawnDistanceFromCharacter;
        float throwPowerMultiplier = ownerCombat != null ? ownerCombat.ThrowPower : 1f;
        float launchSpeed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, Mathf.Clamp01(normalizedPower)) * throwPowerMultiplier;
        ObjectHeadSkillSettings skillSettings = skillSelector != null
            ? skillSelector.GetCurrentSkillSettings()
            : ObjectHeadSkillSettings.CreateDefault(null, projectileColor, explosionColor, maxDamage, projectileRadius * explosionRadiusMultiplier, knockbackForce);

        characterVisual?.PlayThrowPose(0.25f);

        if (skillSettings.effectType == SkillEffectType.CreateTerrainBridge)
        {
            skillSelector?.NotifySkillFired();
            StartCoroutine(GrowSeedVineRoutine(direction, normalizedPower, skillSettings));
            return;
        }

        GameObject projectileObject = new GameObject("SkillProjectile");
        projectileObject.transform.position = spawnPosition;

        SkillProjectile projectile = projectileObject.AddComponent<SkillProjectile>();
        projectile.Initialize(
            direction * launchSpeed,
            projectileRadius,
            projectileGravityScale,
            projectileLifetime,
            projectileColor,
            ownerCombat,
            turnManager,
            maxDamage,
            projectileRadius * explosionRadiusMultiplier,
            explosionFadeSeconds,
            explosionColor,
            knockbackForce,
            skillSettings);

        Collider2D projectileCollider = projectile.GetComponent<Collider2D>();
        Collider2D[] ownerColliders = GetComponents<Collider2D>();

        for (int i = 0; i < ownerColliders.Length; i++)
        {
            if (ownerColliders[i] != null && projectileCollider != null)
            {
                Physics2D.IgnoreCollision(ownerColliders[i], projectileCollider, true);
            }
        }

        skillSelector?.NotifySkillFired();
    }

    private IEnumerator GrowSeedVineRoutine(
        Vector2 direction,
        float charge01,
        ObjectHeadSkillSettings settings)
    {
        turnManager?.NotifyResolving();
        TerrainManager terrain = FindTerrainManager();
        if (terrain == null)
        {
            characterVisual?.RestoreAfterChargeCancel();
            turnManager?.NotifyActionResolved();
            yield break;
        }

        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        Collider2D ownerCollider = GetComponent<Collider2D>();
        Vector2 center = ownerCollider != null
            ? (Vector2)ownerCollider.bounds.center
            : (Vector2)transform.position;
        Vector2 extents = ownerCollider != null
            ? (Vector2)ownerCollider.bounds.extents
            : new Vector2(0.36f, 0.58f);
        float xDistance = Mathf.Abs(direction.x) > 0.0001f
            ? extents.x / Mathf.Abs(direction.x)
            : float.PositiveInfinity;
        float yDistance = Mathf.Abs(direction.y) > 0.0001f
            ? extents.y / Mathf.Abs(direction.y)
            : float.PositiveInfinity;
        float boundaryDistance = Mathf.Min(xDistance, yDistance);
        Vector2 startPosition = center +
            direction * (boundaryDistance + vineStartForwardOffsetWorld + vineStartSurfaceClearanceWorld);

        float remainingLength = Mathf.Lerp(
            minVineLengthWorld,
            maxVineLengthWorld,
            Mathf.Clamp01(charge01));
        int seed = TerrainGrowthSeedUtility.Build(
            ownerCombat,
            turnManager,
            settings.skillId,
            settings.commonHeadTypeId,
            startPosition);
        System.Random random = new System.Random(seed);
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        Bounds terrainBounds = terrain.GetTerrainBounds();
        float travelledDistance = 0f;
        int stampsSinceRebuild = 0;
        float lastRebuildTime = Time.time;

        Debug.Log(
            $"Seed vine seed={seed}, start={startPosition}, direction={direction}, " +
            $"length={remainingLength:0.##}.");

        while (remainingLength > 0f)
        {
            Vector2 samplePosition =
                startPosition +
                direction * travelledDistance +
                perpendicular * ((float)random.NextDouble() - 0.5f) * 0.025f;
            if (!terrainBounds.Contains(samplePosition))
            {
                Debug.Log($"Seed vine stopped at terrain texture bounds: {samplePosition}.");
                break;
            }

            bool insideExistingTerrain = terrain.IsSolidWorld(samplePosition);
            if (!insideExistingTerrain)
            {
                Collider2D[] blockedCharacters = FindLivingCharacterColliders();
                if (terrain.CreateCircleDeferred(
                    samplePosition,
                    vineSegmentRadiusPx,
                    TerrainType.Created,
                    blockedCharacters))
                {
                    stampsSinceRebuild++;
                    StartCoroutine(ShowVineGrowthMarker(samplePosition));
                }
            }

            if (stampsSinceRebuild >= Mathf.Max(1, colliderRebuildEveryNStamps) ||
                Time.time - lastRebuildTime >= colliderRebuildMaxInterval)
            {
                terrain.FlushDeferredTerrainChanges();
                stampsSinceRebuild = 0;
                lastRebuildTime = Time.time;
            }

            float stepCost = vineSegmentSpacingWorld *
                (insideExistingTerrain ? Mathf.Max(1f, terrainPassLengthMultiplier) : 1f);
            remainingLength -= stepCost;
            travelledDistance += vineSegmentSpacingWorld;
            yield return new WaitForSeconds(vineGrowthIntervalSeconds);
        }

        terrain.FlushDeferredTerrainChanges();
        characterVisual?.RestoreAfterChargeCancel();
        turnManager?.NotifyActionResolved();
    }

    private IEnumerator ShowVineGrowthMarker(Vector2 position)
    {
        GameObject marker = new GameObject("VineGrowthMarker");
        marker.transform.position = position;
        SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = CommonHeadItem.GetDefaultSprite(CommonHeadType.TerrainCreation);
        renderer.color = new Color(0.3f, 1f, 0.25f, 0.7f);
        renderer.sortingOrder = 39;
        marker.transform.localScale = Vector3.one * 0.25f;

        const float duration = 0.15f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            if (renderer != null)
            {
                Color color = renderer.color;
                color.a = 0.7f * (1f - elapsed / duration);
                renderer.color = color;
                marker.transform.localScale = Vector3.one * Mathf.Lerp(0.12f, 0.28f, elapsed / duration);
            }
            yield return null;
        }

        Destroy(marker);
    }

    private static Collider2D[] FindLivingCharacterColliders()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        CharacterCombat[] combats = Object.FindObjectsByType<CharacterCombat>(FindObjectsSortMode.None);
#else
        CharacterCombat[] combats = Object.FindObjectsOfType<CharacterCombat>();
#endif
        System.Collections.Generic.List<Collider2D> colliders =
            new System.Collections.Generic.List<Collider2D>();
        for (int i = 0; i < combats.Length; i++)
        {
            if (combats[i] == null || combats[i].IsDead)
            {
                continue;
            }

            Collider2D collider = combats[i].GetComponent<Collider2D>();
            if (collider != null)
            {
                colliders.Add(collider);
            }
        }

        return colliders.ToArray();
    }

    private TurnManager FindTurnManager()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TurnManager>();
#else
        return Object.FindObjectOfType<TurnManager>();
#endif
    }

    private static TerrainManager FindTerrainManager()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TerrainManager>();
#else
        return Object.FindObjectOfType<TerrainManager>();
#endif
    }
}
