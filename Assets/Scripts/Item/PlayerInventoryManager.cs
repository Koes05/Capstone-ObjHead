using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInventoryManager : MonoBehaviour
{
    private readonly Dictionary<int, CommonHeadInventory> inventories = new Dictionary<int, CommonHeadInventory>();

    public void ConfigurePlayers(int playerCount)
    {
        inventories.Clear();
        for (int player = 1; player <= Mathf.Max(1, playerCount); player++)
        {
            GetOrCreateInventory(player);
        }
    }

    public CommonHeadInventory GetInventory(int playerIndex)
    {
        if (playerIndex <= 0)
        {
            return null;
        }

        if (inventories.TryGetValue(playerIndex, out CommonHeadInventory inventory) && inventory != null)
        {
            return inventory;
        }

        RefreshInventories();
        return inventories.TryGetValue(playerIndex, out inventory) ? inventory : GetOrCreateInventory(playerIndex);
    }

    public CommonHeadInventory GetInventoryFor(GameObject character)
    {
        ObjectHeadTeamMember member = character != null ? character.GetComponent<ObjectHeadTeamMember>() : null;
        return member != null ? GetInventory(member.PlayerIndex) : null;
    }

    private CommonHeadInventory GetOrCreateInventory(int playerIndex)
    {
        if (inventories.TryGetValue(playerIndex, out CommonHeadInventory existing) && existing != null)
        {
            return existing;
        }

        GameObject inventoryObject = new GameObject($"P{playerIndex}_CommonHeadInventory");
        inventoryObject.transform.SetParent(transform, false);
        CommonHeadInventory inventory = inventoryObject.AddComponent<CommonHeadInventory>();
        inventory.ConfigurePlayer(playerIndex);
        inventories[playerIndex] = inventory;
        return inventory;
    }

    private void RefreshInventories()
    {
        inventories.Clear();
        CommonHeadInventory[] found = GetComponentsInChildren<CommonHeadInventory>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
            {
                inventories[found[i].PlayerIndex] = found[i];
            }
        }
    }
}
