using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class CommonHeadItem : MonoBehaviour
{
    private static readonly Dictionary<CommonHeadType, int> activeCounts =
        new Dictionary<CommonHeadType, int>();

    private CommonHeadType itemType;
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
    }

    public static int GetActiveCount(CommonHeadType type)
    {
        return activeCounts.TryGetValue(type, out int count) ? count : 0;
    }

    public static CommonHeadItem Create(CommonHeadType type, Vector2 position, Sprite sprite)
    {
        GameObject itemObject = new GameObject($"CommonHeadItem_{type}");
        itemObject.transform.position = position;
        itemObject.transform.localScale = Vector3.one * 0.75f;

        SpriteRenderer renderer = itemObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite != null ? sprite : GetDefaultSprite(type);
        renderer.color = ColorForType(type);
        renderer.sortingOrder = 24;

        Rigidbody2D body = itemObject.AddComponent<Rigidbody2D>();
        body.gravityScale = 1.5f;
        body.mass = 0.25f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        CircleCollider2D groundCollider = itemObject.AddComponent<CircleCollider2D>();
        groundCollider.isTrigger = false;
        groundCollider.radius = 0.42f;

        CircleCollider2D pickupTrigger = itemObject.AddComponent<CircleCollider2D>();
        pickupTrigger.isTrigger = true;
        pickupTrigger.radius = 0.62f;

        CommonHeadItem item = itemObject.AddComponent<CommonHeadItem>();
        item.itemType = type;
        item.Register();
        return item;
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
        switch (type)
        {
            case CommonHeadType.Attack:
                return new Color(1f, 0.35f, 0.2f, 1f);
            case CommonHeadType.Mobility:
                return new Color(0.25f, 0.9f, 1f, 1f);
            case CommonHeadType.TerrainCreation:
                return new Color(0.4f, 1f, 0.4f, 1f);
            default:
                return Color.white;
        }
    }

    public static Sprite GetDefaultSprite(CommonHeadType type)
    {
        switch (type)
        {
            case CommonHeadType.Attack:
                return Resources.Load<Sprite>("Sprites/Heads/head_bomb_red");
            case CommonHeadType.Mobility:
                return Resources.Load<Sprite>("Sprites/Heads/head_bulb_on");
            case CommonHeadType.TerrainCreation:
                return Resources.Load<Sprite>("Sprites/Heads/head_seed");
            default:
                return null;
        }
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
