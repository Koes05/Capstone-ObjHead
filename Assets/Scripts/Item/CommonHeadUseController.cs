using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class CommonHeadUseController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float mobilityImpulse = 9f;
    [SerializeField, Min(0.1f)] private float mobilityResolveSeconds = 0.45f;
    [SerializeField, Min(1)] private int createdTerrainRadiusPx = 24;

    private CommonHeadInventory inventory;
    private PlayerInventoryManager inventoryManager;
    private TurnCharacterController turnCharacter;
    private CharacterCombat combat;
    private AimController aimController;
    private TurnManager turnManager;

    private void Awake()
    {
        inventoryManager = FindAny<PlayerInventoryManager>();
        turnCharacter = GetComponent<TurnCharacterController>();
        combat = GetComponent<CharacterCombat>();
        aimController = GetComponent<AimController>();
        turnManager = FindAny<TurnManager>();
    }

    private void Update()
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

        if (inventory == null ||
            turnManager == null ||
            !turnManager.CanCharacterFire(turnCharacter))
        {
            return;
        }

        int slotIndex = ReadSlotInput();
        if (slotIndex >= 0)
        {
            TryUseSlot(slotIndex);
        }
    }

    private void TryUseSlot(int slotIndex)
    {
        CommonHeadType type = inventory.GetSlot(slotIndex);
        if (type == CommonHeadType.None || !turnManager.TryBeginAction(turnCharacter))
        {
            return;
        }

        inventory.TryConsume(slotIndex, out _);
        Debug.Log($"{name} used {type} common head from slot {slotIndex + 6}.");

        switch (type)
        {
            case CommonHeadType.Attack:
                FireAttackHead();
                break;
            case CommonHeadType.Mobility:
                StartCoroutine(MobilityRoutine());
                break;
            case CommonHeadType.TerrainCreation:
                StartCoroutine(CreateTerrainRoutine());
                break;
        }
    }

    private void FireAttackHead()
    {
        aimController?.ConfirmFacingFromAim();
        Vector2 direction = aimController != null ? aimController.AimDirection : Vector2.right;
        Vector2 origin = aimController != null ? aimController.AimOrigin : (Vector2)transform.position;
        ObjectHeadSkillSettings settings = ObjectHeadSkillSettings.CreateDefault(
            Resources.Load<Sprite>("Sprites/Heads/head_bomb_red"),
            new Color(1f, 0.35f, 0.12f, 1f),
            new Color(1f, 0.12f, 0f, 0.58f),
            24,
            1.35f,
            8f);
        settings.effectType = SkillEffectType.DamageExplosion;
        settings.terrainRadiusPx = 34;
        settings.projectileVisualDiameter = 0.65f;

        GameObject projectileObject = new GameObject("CommonHeadAttackProjectile");
        projectileObject.transform.position = origin + direction * 0.75f;
        SkillProjectile projectile = projectileObject.AddComponent<SkillProjectile>();
        projectile.Initialize(
            direction * 10f,
            0.18f,
            1f,
            8f,
            settings.projectileColor,
            combat,
            turnManager,
            settings.maxDamage,
            settings.explosionRadiusWorld,
            0.3f,
            settings.impactColor,
            settings.knockbackForce,
            settings);

        Collider2D projectileCollider = projectile.GetComponent<Collider2D>();
        Collider2D[] ownerColliders = GetComponents<Collider2D>();
        for (int i = 0; i < ownerColliders.Length; i++)
        {
            if (ownerColliders[i] != null && projectileCollider != null)
            {
                Physics2D.IgnoreCollision(ownerColliders[i], projectileCollider, true);
            }
        }
    }

    private IEnumerator MobilityRoutine()
    {
        turnManager.NotifyResolving();
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        Vector2 aim = aimController != null ? aimController.AimDirection : Vector2.right;
        Vector2 direction = new Vector2(aim.x, Mathf.Max(0.45f, aim.y)).normalized;

        if (body != null)
        {
            turnCharacter?.PreserveExternalMotion(mobilityResolveSeconds + 0.2f);
            body.AddForce(direction * mobilityImpulse, ForceMode2D.Impulse);
        }

        yield return new WaitForSeconds(mobilityResolveSeconds);
        turnManager.NotifyActionResolved();
    }

    private IEnumerator CreateTerrainRoutine()
    {
        turnManager.NotifyResolving();
        TerrainManager terrain = FindAny<TerrainManager>();
        Vector2 direction = aimController != null ? aimController.AimDirection : Vector2.right;
        Vector2 target = (Vector2)transform.position + direction * 2.6f;
        terrain?.CreateCircle(target, createdTerrainRadiusPx, TerrainType.Created);
        yield return null;
        turnManager.NotifyActionResolved();
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

    private static T FindAny<T>() where T : Object
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
