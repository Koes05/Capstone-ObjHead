using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class CommonHeadItem : MonoBehaviour
{
    private static int activeCount;
    private CommonHeadType itemType;
    private bool registered;

    public static int ActiveCount => activeCount;
    public CommonHeadType ItemType => itemType;

    public static CommonHeadItem Create(CommonHeadType type, Vector2 position, Sprite sprite)
    {
        GameObject itemObject = new GameObject($"CommonHeadItem_{type}");
        itemObject.transform.position = position;
        itemObject.transform.localScale = Vector3.one * 0.75f;

        SpriteRenderer renderer = itemObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = ColorForType(type);
        renderer.sortingOrder = 24;

        CircleCollider2D trigger = itemObject.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 0.55f;

        CommonHeadItem item = itemObject.AddComponent<CommonHeadItem>();
        item.itemType = type;
        item.registered = true;
        activeCount++;
        return item;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        CommonHeadInventory inventory = other.GetComponentInParent<CommonHeadInventory>();
        if (inventory == null || !inventory.TryAdd(itemType, out int slotIndex))
        {
            return;
        }

        Debug.Log($"{other.name} picked up {itemType} common head in slot {slotIndex + 6}.");
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (registered)
        {
            activeCount = Mathf.Max(0, activeCount - 1);
            registered = false;
        }
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
}
