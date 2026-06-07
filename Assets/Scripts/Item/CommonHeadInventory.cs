using System;
using UnityEngine;

[DisallowMultipleComponent]
public class CommonHeadInventory : MonoBehaviour
{
    private const int SlotCount = 3;
    [SerializeField] private CommonHeadType[] slots = new CommonHeadType[SlotCount];

    public event Action InventoryChanged;

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

    public bool TryAdd(CommonHeadType type, out int slotIndex)
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
    }
}
