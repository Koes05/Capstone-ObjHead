using UnityEngine;

[DisallowMultipleComponent]
public class ObjectHeadTeamMember : MonoBehaviour
{
    [SerializeField, Min(1)] private int playerIndex = 1;
    [SerializeField, Min(1)] private int teamSlotIndex = 1;
    [SerializeField] private string characterLabel = "Character";

    public int PlayerIndex => playerIndex;
    public int TeamSlotIndex => teamSlotIndex;
    public string CharacterLabel => characterLabel;
    public string DisplayName => $"P{playerIndex}-{teamSlotIndex} {characterLabel}";

    public void Configure(int ownerPlayerIndex, int slotIndex, ObjectHeadCharacterKind kind)
    {
        playerIndex = Mathf.Max(1, ownerPlayerIndex);
        teamSlotIndex = Mathf.Max(1, slotIndex);
        characterLabel = kind.ToString();
        gameObject.name = $"P{playerIndex}_Slot{teamSlotIndex}_{characterLabel}";
    }
}
