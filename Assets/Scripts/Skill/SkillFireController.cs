using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class SkillFireController : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float projectileRadius = 0.18f;
    [FormerlySerializedAs("spawnDistanceFromTarget")]
    [SerializeField, Min(0f)] private float spawnDistanceFromCharacter = 0.7f;
    [SerializeField, Min(0.1f)] private float minLaunchSpeed = 5f;
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

    private TurnCharacterController turnCharacter;
    private CharacterCombat ownerCombat;
    private AimController aimController;
    private PowerChargeController powerChargeController;
    private CharacterVisual characterVisual;
    private DemoSkillSelector skillSelector;
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

        if (turnCharacter == null ||
            powerChargeController == null ||
            hasFiredThisTurn ||
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

    private TurnManager FindTurnManager()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TurnManager>();
#else
        return Object.FindObjectOfType<TurnManager>();
#endif
    }
}
