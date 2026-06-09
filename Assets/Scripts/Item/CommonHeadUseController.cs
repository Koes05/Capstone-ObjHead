using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class CommonHeadUseController : MonoBehaviour
{
    [Header("Throw")]
    [SerializeField, Min(0.05f)] private float projectileRadius = 0.18f;
    [SerializeField, Min(0.1f)] private float minLaunchSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float maxLaunchSpeed = 13f;
    [SerializeField, Min(0f)] private float projectileGravityScale = 1f;
    [SerializeField, Min(0.1f)] private float projectileLifetime = 8f;
    [SerializeField, Min(0f)] private float spawnDistanceFromCharacter = 0.75f;

    [Header("Attack Head")]
    [SerializeField, Min(1)] private int attackClusterCount = 8;
    [SerializeField, Min(0)] private int attackClusterDamagePerExplosion = 16;
    [SerializeField, Min(0)] private int attackClusterMaxTotalDamage = 72;
    [SerializeField, Min(1)] private int attackClusterTerrainRadiusPx = 40;
    [SerializeField, Min(0.1f)] private float attackClusterExplosionRadiusWorld = 1.25f;
    [SerializeField, Min(0.1f)] private float attackClusterSpreadRadiusWorld = 2.6f;
    [SerializeField, Min(0.01f)] private float attackClusterDelaySeconds = 0.09f;
    [SerializeField, Min(0f)] private float attackClusterKnockbackForce = 9.5f;

    [Header("Terrain Head")]
    [SerializeField, Min(1)] private int createdTerrainRadiusPx = 20;
    [SerializeField, Min(1)] private int terrainBurstCount = 22;
    [SerializeField, Min(1)] private int terrainBurstStampRadiusPx = 12;
    [SerializeField, Min(0.01f)] private float terrainBurstInterval = 0.045f;
    [SerializeField, Min(0.1f)] private float terrainBurstSpreadWorld = 2.2f;
    [SerializeField, Min(0.1f)] private float terrainBurstRadiusXWorld = 2.2f;
    [SerializeField, Min(0.1f)] private float terrainBurstRadiusYWorld = 1.65f;
    [SerializeField, Min(0.5f)] private float maxBuildHeightAboveSurfaceWorld = 7f;

    [Header("Jet Jump")]
    [SerializeField, Min(0.1f)] private float minJetJumpSpeed = 7f;
    [SerializeField, Min(0.1f)] private float maxJetJumpSpeed = 17f;
    [SerializeField, Min(0.25f)] private float jetJumpResolveTimeoutSeconds = 5f;
    [SerializeField, Min(0.02f)] private float jetJumpLandingStableSeconds = 0.12f;

    private CommonHeadInventory inventory;
    private PlayerInventoryManager inventoryManager;
    private TurnCharacterController turnCharacter;
    private CharacterCombat combat;
    private AimController aimController;
    private PowerChargeController powerChargeController;
    private CharacterVisual characterVisual;
    private TurnManager turnManager;
    private int selectedSlotIndex = -1;
    private CommonHeadType selectedType = CommonHeadType.None;
    private Sprite selectedSprite;
    private bool commonActionInProgress;

    public bool HasSelectedCommonHead => selectedSlotIndex >= 0 && selectedType != CommonHeadType.None;
    public bool IsUsingCommonHead => commonActionInProgress;
    public int SelectedSlotIndex => selectedSlotIndex;
    public CommonHeadType SelectedType => selectedType;
    public Sprite SelectedSprite => selectedSprite;
    public float MinJetJumpSpeed => minJetJumpSpeed;
    public float MaxJetJumpSpeed => maxJetJumpSpeed;

    private void Awake()
    {
        inventoryManager = FindAny<PlayerInventoryManager>();
        turnCharacter = GetComponent<TurnCharacterController>();
        combat = GetComponent<CharacterCombat>();
        aimController = GetComponent<AimController>();
        powerChargeController = GetComponent<PowerChargeController>();
        characterVisual = GetComponent<CharacterVisual>();
        turnManager = FindAny<TurnManager>();
    }

    private void OnDisable()
    {
        commonActionInProgress = false;
        CancelSelectionAndRestoreUniqueHead();
    }

    private void Update()
    {
        RefreshReferences();
        if (inventory == null || turnManager == null || turnCharacter == null)
        {
            return;
        }

        ValidateSelectedSlot();

        if (WasCancelPressed())
        {
            CancelSelectionAndRestoreUniqueHead();
            return;
        }

        if (turnManager.CanCharacterFire(turnCharacter))
        {
            int slotIndex = ReadSlotInput();
            if (slotIndex >= 0)
            {
                SelectSlot(slotIndex);
            }
        }

        if (!HasSelectedCommonHead ||
            powerChargeController == null ||
            !turnManager.CanCharacterFire(turnCharacter))
        {
            return;
        }

        if (powerChargeController.ConsumeReleasedPower(out float power))
        {
            UseSelectedHead(power);
        }
    }

    public void CancelSelectionAndRestoreUniqueHead()
    {
        selectedSlotIndex = -1;
        selectedType = CommonHeadType.None;
        selectedSprite = null;
        powerChargeController?.CancelCharge();
        if (!commonActionInProgress)
        {
            characterVisual?.RestoreUniqueHead();
        }
    }

    private void RefreshReferences()
    {
        if (turnManager == null)
        {
            turnManager = FindAny<TurnManager>();
        }

        if (inventoryManager == null)
        {
            inventoryManager = FindAny<PlayerInventoryManager>();
        }

        ObjectHeadTeamMember member = GetComponent<ObjectHeadTeamMember>();
        inventory = member != null && inventoryManager != null
            ? inventoryManager.GetInventory(member.PlayerIndex)
            : null;
    }

    private void ValidateSelectedSlot()
    {
        if (!HasSelectedCommonHead)
        {
            return;
        }

        if (inventory.GetSlot(selectedSlotIndex) != selectedType)
        {
            CancelSelectionAndRestoreUniqueHead();
        }
    }

    private void SelectSlot(int slotIndex)
    {
        CommonHeadType type = inventory.GetSlot(slotIndex);
        if (type == CommonHeadType.None)
        {
            return;
        }

        if (selectedSlotIndex == slotIndex)
        {
            CancelSelectionAndRestoreUniqueHead();
            return;
        }

        selectedSlotIndex = slotIndex;
        selectedType = type;
        selectedSprite = inventory.GetSlotSprite(slotIndex);
        if (selectedSprite == null)
        {
            selectedSprite = CommonHeadItem.GetDefaultSprite(type);
        }

        powerChargeController?.CancelCharge();
        characterVisual?.SetTemporaryCommonHead(selectedSprite);
        Debug.Log($"{name} selected {type} common head in slot {slotIndex + 6}.");
    }

    private void UseSelectedHead(float normalizedPower)
    {
        int slotIndex = selectedSlotIndex;
        CommonHeadType type = selectedType;
        Sprite sprite = selectedSprite;
        if (slotIndex < 0 ||
            type == CommonHeadType.None ||
            inventory.GetSlot(slotIndex) != type)
        {
            return;
        }

        if (!inventory.TryConsume(slotIndex, out CommonHeadType consumedType))
        {
            return;
        }

        if (!turnManager.TryBeginAction(turnCharacter))
        {
            inventory.TryAdd(consumedType, sprite, out _);
            return;
        }

        selectedSlotIndex = -1;
        selectedType = CommonHeadType.None;
        selectedSprite = null;
        commonActionInProgress = true;
        powerChargeController?.CancelCharge();
        aimController?.ConfirmFacingFromAim();
        characterVisual?.PlayThrowPose(0.25f);
        characterVisual?.HideHeadForThrow();
        Debug.Log($"{name} used {consumedType} common head from slot {slotIndex + 6} at power {normalizedPower:0.00}.");

        switch (consumedType)
        {
            case CommonHeadType.Attack:
                FireProjectile(BuildAttackSettings(sprite), normalizedPower, "CommonHeadAttackProjectile");
                break;
            case CommonHeadType.Mobility:
                StartCoroutine(JetJumpRoutine(normalizedPower));
                break;
            case CommonHeadType.TerrainCreation:
                FireProjectile(BuildTerrainSettings(sprite), normalizedPower, "CommonHeadTerrainProjectile");
                break;
            default:
                CompleteCommonAction();
                turnManager.NotifyActionResolved();
                break;
        }
    }

    private void FireProjectile(
        ObjectHeadSkillSettings settings,
        float normalizedPower,
        string projectileName)
    {
        Vector2 direction = aimController != null ? aimController.AimDirection : Vector2.right;
        Vector2 origin = aimController != null ? aimController.AimOrigin : (Vector2)transform.position;
        float throwPowerMultiplier = combat != null ? combat.ThrowPower : 1f;
        float launchSpeed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, Mathf.Clamp01(normalizedPower)) * throwPowerMultiplier;

        GameObject projectileObject = new GameObject(projectileName);
        projectileObject.transform.position = origin + direction * spawnDistanceFromCharacter;
        SkillProjectile projectile = projectileObject.AddComponent<SkillProjectile>();
        projectile.Resolved += CompleteCommonAction;
        projectile.Initialize(
            direction * launchSpeed,
            projectileRadius,
            projectileGravityScale,
            projectileLifetime,
            settings.projectileColor,
            combat,
            turnManager,
            settings.maxDamage,
            settings.explosionRadiusWorld,
            0.3f,
            settings.impactColor,
            settings.knockbackForce,
            settings);

        IgnoreOwnerCollision(projectile.GetComponent<Collider2D>());
    }

    private IEnumerator JetJumpRoutine(float normalizedPower)
    {
        turnManager.NotifyResolving();
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        Vector2 direction = aimController != null ? aimController.EffectiveAimDirection : Vector2.up;
        float launchSpeed = Mathf.Lerp(minJetJumpSpeed, maxJetJumpSpeed, Mathf.Clamp01(normalizedPower));
        bool leftGround = turnCharacter == null || !turnCharacter.IsGrounded;
        float groundedStableSeconds = 0f;
        float elapsed = 0f;

        turnCharacter?.BeginJetJumpFallDamageImmunity();
        turnCharacter?.PreserveExternalMotion(jetJumpResolveTimeoutSeconds);
        if (body != null)
        {
            body.AddForce(direction * launchSpeed, ForceMode2D.Impulse);
        }

        while (elapsed < jetJumpResolveTimeoutSeconds)
        {
            if (combat != null && combat.IsDead)
            {
                break;
            }

            if (turnCharacter == null ||
                !turnCharacter.isActiveAndEnabled ||
                !turnCharacter.gameObject.activeInHierarchy)
            {
                break;
            }

            if (!turnCharacter.IsGrounded)
            {
                leftGround = true;
                groundedStableSeconds = 0f;
            }
            else if (leftGround)
            {
                groundedStableSeconds += Time.deltaTime;
                if (groundedStableSeconds >= jetJumpLandingStableSeconds)
                {
                    break;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        CompleteCommonAction();
        turnManager.NotifyActionResolved();
    }

    private ObjectHeadSkillSettings BuildAttackSettings(Sprite sprite)
    {
        ObjectHeadSkillSettings settings = ObjectHeadSkillSettings.CreateDefault(
            sprite != null ? sprite : CommonHeadItem.GetDefaultSprite(CommonHeadType.Attack),
            new Color(1f, 0.35f, 0.12f, 1f),
            new Color(1f, 0.12f, 0f, 0.58f),
            attackClusterDamagePerExplosion,
            attackClusterExplosionRadiusWorld,
            attackClusterKnockbackForce);
        settings.effectType = SkillEffectType.ChainExplosion;
        settings.terrainRadiusPx = attackClusterTerrainRadiusPx;
        settings.chainCount = attackClusterCount;
        settings.chainSpacingWorld = 0.5f;
        settings.chainDelaySeconds = attackClusterDelaySeconds;
        settings.chainMaxTotalDamage = attackClusterMaxTotalDamage;
        settings.chainSpreadRadiusWorld = attackClusterSpreadRadiusWorld;
        settings.useWideClusterPattern = true;
        settings.skillId = 101;
        settings.commonHeadTypeId = (int)CommonHeadType.Attack;
        settings.projectileVisualDiameter = 0.7f;
        return settings;
    }

    private ObjectHeadSkillSettings BuildTerrainSettings(Sprite sprite)
    {
        ObjectHeadSkillSettings settings = ObjectHeadSkillSettings.CreateDefault(
            sprite != null ? sprite : CommonHeadItem.GetDefaultSprite(CommonHeadType.TerrainCreation),
            new Color(0.35f, 0.9f, 0.35f, 1f),
            new Color(0.3f, 0.95f, 0.35f, 0.5f),
            0,
            0.45f,
            0f);
        settings.effectType = SkillEffectType.CreateTerrainCircle;
        settings.terrainRadiusPx = createdTerrainRadiusPx;
        settings.terrainBurstCount = terrainBurstCount;
        settings.terrainBurstStampRadiusPx = terrainBurstStampRadiusPx;
        settings.terrainBurstMaxPlacementAttemptsPerStamp = 4;
        settings.terrainBurstIntervalSeconds = terrainBurstInterval;
        settings.terrainBurstSpreadWorld = terrainBurstSpreadWorld;
        settings.terrainBurstVerticalBiasWorld = 0f;
        settings.finalTerrainRadiusXWorld = terrainBurstRadiusXWorld;
        settings.finalTerrainRadiusYWorld = Mathf.Max(0.5f, terrainBurstRadiusYWorld);
        settings.maxBuildHeightAboveSurfaceWorld = maxBuildHeightAboveSurfaceWorld;
        settings.skillId = 102;
        settings.commonHeadTypeId = (int)CommonHeadType.TerrainCreation;
        settings.projectileVisualDiameter = 0.58f;
        return settings;
    }

    private void IgnoreOwnerCollision(Collider2D projectileCollider)
    {
        if (projectileCollider == null)
        {
            return;
        }

        Collider2D[] ownerColliders = GetComponents<Collider2D>();
        for (int i = 0; i < ownerColliders.Length; i++)
        {
            if (ownerColliders[i] != null)
            {
                Physics2D.IgnoreCollision(ownerColliders[i], projectileCollider, true);
            }
        }
    }

    private void CompleteCommonAction()
    {
        commonActionInProgress = false;
        characterVisual?.RestoreUniqueHead();
        characterVisual?.ShowHeadAfterAction();
    }

    private int ReadSlotInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return -1;
        }

        if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) return 0;
        if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) return 1;
        if (keyboard.digit8Key.wasPressedThisFrame || keyboard.numpad8Key.wasPressedThisFrame) return 2;
#else
        if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) return 0;
        if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) return 1;
        if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)) return 2;
#endif
        return -1;
    }

    private static bool WasCancelPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
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
