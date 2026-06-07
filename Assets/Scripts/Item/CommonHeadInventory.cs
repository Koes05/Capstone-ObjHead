using System;
using UnityEngine;

[DisallowMultipleComponent]
public class CommonHeadInventory : MonoBehaviour
{
    private const int SlotCount = 3;
    [SerializeField, Min(1)] private int playerIndex = 1;
    [SerializeField] private CommonHeadType[] slots = new CommonHeadType[SlotCount];
    [SerializeField] private Sprite[] slotSprites = new Sprite[SlotCount];

    public event Action InventoryChanged;
    public int PlayerIndex => playerIndex;

    public int Count
    {
        get
        {
            int count = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetSlot(i) != CommonHeadType.None)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public CommonHeadType GetSlot(int slotIndex)
    {
        EnsureSlots();
        return slotIndex >= 0 && slotIndex < SlotCount ? slots[slotIndex] : CommonHeadType.None;
    }

    public Sprite GetSlotSprite(int slotIndex)
    {
        EnsureSlots();
        return slotIndex >= 0 && slotIndex < SlotCount ? slotSprites[slotIndex] : null;
    }

    public void ConfigurePlayer(int ownerPlayerIndex)
    {
        playerIndex = Mathf.Max(1, ownerPlayerIndex);
        gameObject.name = $"P{playerIndex}_CommonHeadInventory";
        EnsureSlots();
    }

    public bool TryAdd(CommonHeadType type, out int slotIndex)
    {
        return TryAdd(type, CommonHeadItem.GetDefaultSprite(type), out slotIndex);
    }

    public bool TryAdd(CommonHeadType type, Sprite sprite, out int slotIndex)
    {
        EnsureSlots();
        slotIndex = -1;
        if (type == CommonHeadType.None)
        {
            return false;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (slots[i] != CommonHeadType.None)
            {
                continue;
            }

            slots[i] = type;
            slotSprites[i] = sprite != null ? sprite : CommonHeadItem.GetDefaultSprite(type);
            slotIndex = i;
            InventoryChanged?.Invoke();
            return true;
        }

        return false;
    }

    public bool TryConsume(int slotIndex, out CommonHeadType type)
    {
        type = GetSlot(slotIndex);
        if (type == CommonHeadType.None)
        {
            return false;
        }

        slots[slotIndex] = CommonHeadType.None;
        slotSprites[slotIndex] = null;
        InventoryChanged?.Invoke();
        return true;
    }

    private void EnsureSlots()
    {
        if (slots == null || slots.Length != SlotCount)
        {
            CommonHeadType[] resized = new CommonHeadType[SlotCount];
            if (slots != null)
            {
                Array.Copy(slots, resized, Mathf.Min(slots.Length, resized.Length));
            }

            slots = resized;
        }

        if (slotSprites == null || slotSprites.Length != SlotCount)
        {
            Sprite[] resizedSprites = new Sprite[SlotCount];
            if (slotSprites != null)
            {
                Array.Copy(slotSprites, resizedSprites, Mathf.Min(slotSprites.Length, resizedSprites.Length));
            }

            slotSprites = resizedSprites;
        }
    }
}
