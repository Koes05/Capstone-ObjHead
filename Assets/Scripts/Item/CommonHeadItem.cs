using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class CommonHeadItem : MonoBehaviour
{
    private const float ItemVisualScale = 0.75f;

    private static readonly Dictionary<CommonHeadType, int> activeCounts =
        new Dictionary<CommonHeadType, int>();
    private static PhysicsMaterial2D itemPhysicsMaterial;

    private CommonHeadType itemType;
    private Collider2D groundCollider;
    private Collider2D pickupTrigger;
    private bool registered;

    public static int ActiveCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<CommonHeadType, int> pair in activeCounts)
            {
                total += pair.Value;
            }
            return total;
        }
    }

    public CommonHeadType ItemType => itemType;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveCounts()
    {
        activeCounts.Clear();
        itemPhysicsMaterial = null;
    }

    public static int GetActiveCount(CommonHeadType type)
    {
        return activeCounts.TryGetValue(type, out int count) ? count : 0;
    }

    public static CommonHeadItem Create(CommonHeadType type, Vector2 position, Sprite sprite)
    {
        GameObject itemObject = new GameObject($"CommonHeadItem_{type}");
        itemObject.transform.position = position;
        itemObject.transform.localScale = Vector3.one * ItemVisualScale;

        SpriteRenderer renderer = itemObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite != null ? sprite : GetDefaultSprite(type);
        renderer.color = ColorForType(type);
        renderer.sortingOrder = 24;

        Rigidbody2D body = itemObject.AddComponent<Rigidbody2D>();
        body.gravityScale = 1.5f;
        body.mass = 0.25f;
        body.freezeRotation = false;
        body.angularDamping = 0.8f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        CircleCollider2D groundCollider = itemObject.AddComponent<CircleCollider2D>();
        groundCollider.isTrigger = false;
        groundCollider.radius = 0.42f;
        groundCollider.sharedMaterial = GetItemPhysicsMaterial();

        CircleCollider2D pickupTrigger = itemObject.AddComponent<CircleCollider2D>();
        pickupTrigger.isTrigger = true;
        pickupTrigger.radius = 0.62f;

        CommonHeadItem item = itemObject.AddComponent<CommonHeadItem>();
        item.itemType = type;
        item.groundCollider = groundCollider;
        item.pickupTrigger = pickupTrigger;
        item.RefreshIgnoredCharacterCollisions();
        item.Register();
        return item;
    }

    private void Start()
    {
        RefreshIgnoredCharacterCollisions();
    }

    public void RefreshIgnoredCharacterCollisions()
    {
        if (groundCollider == null)
        {
            groundCollider = FindGroundCollider();
        }

        if (groundCollider == null)
        {
            return;
        }

#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        TurnCharacterController[] characters = Object.FindObjectsByType<TurnCharacterController>(FindObjectsInactive.Exclude);
#else
        TurnCharacterController[] characters = Object.FindObjectsOfType<TurnCharacterController>();
#endif
        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] == null)
            {
                continue;
            }

            Collider2D[] characterColliders = characters[i].GetComponentsInChildren<Collider2D>();
            for (int j = 0; j < characterColliders.Length; j++)
            {
                if (characterColliders[j] != null && characterColliders[j] != pickupTrigger)
                {
                    Physics2D.IgnoreCollision(groundCollider, characterColliders[j], true);
                }
            }
        }
    }

    private Collider2D FindGroundCollider()
    {
        Collider2D[] colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && !colliders[i].isTrigger)
            {
                return colliders[i];
            }
        }

        return null;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<WaterZone>() != null || other.GetComponent<DeathZone>() != null)
        {
            Destroy(gameObject);
            return;
        }

        ObjectHeadTeamMember member = other.GetComponentInParent<ObjectHeadTeamMember>();
        PlayerInventoryManager manager = FindAny<PlayerInventoryManager>();
        CommonHeadInventory inventory = member != null && manager != null
            ? manager.GetInventory(member.PlayerIndex)
            : null;
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        Sprite collectedSprite = renderer != null ? renderer.sprite : GetDefaultSprite(itemType);
        if (inventory == null || !inventory.TryAdd(itemType, collectedSprite, out int slotIndex))
        {
            return;
        }

        Debug.Log($"{other.name} picked up {itemType} common head in slot {slotIndex + 6}.");
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (!registered)
        {
            return;
        }

        int current = GetActiveCount(itemType);
        activeCounts[itemType] = Mathf.Max(0, current - 1);
        registered = false;
    }

    private void Register()
    {
        activeCounts[itemType] = GetActiveCount(itemType) + 1;
        registered = true;
    }

    private static Color ColorForType(CommonHeadType type)
    {
        return Color.white;
    }

    public static Sprite GetDefaultSprite(CommonHeadType type)
    {
        switch (type)
        {
            case CommonHeadType.Attack:
                return Resources.Load<Sprite>("Sprites/Heads/common_head_attack_bomb");
            case CommonHeadType.Mobility:
                return Resources.Load<Sprite>("Sprites/Heads/common_head_mobility_wing");
            case CommonHeadType.TerrainCreation:
                return Resources.Load<Sprite>("Sprites/Heads/common_head_terrain_cloud");
            default:
                return null;
        }
    }

    private static PhysicsMaterial2D GetItemPhysicsMaterial()
    {
        if (itemPhysicsMaterial != null)
        {
            return itemPhysicsMaterial;
        }

        itemPhysicsMaterial = new PhysicsMaterial2D("CommonHeadItemPhysics")
        {
            friction = 0.45f,
            bounciness = 0.03f
        };
        itemPhysicsMaterial.hideFlags = HideFlags.HideAndDontSave;
        return itemPhysicsMaterial;
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
