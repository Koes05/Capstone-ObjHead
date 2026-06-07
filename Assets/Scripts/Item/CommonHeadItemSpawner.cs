using UnityEngine;

[DisallowMultipleComponent]
public class CommonHeadItemSpawner : MonoBehaviour
{
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private Transform spawnRoot;
    [SerializeField, Min(1)] private int spawnEveryTurns = 3;
    [SerializeField, Min(1)] private int maxActiveItems = 3;

    private int turnsObserved;

    public void Configure(TurnManager manager, Transform itemSpawnRoot)
    {
        Unsubscribe();
        turnManager = manager;
        spawnRoot = itemSpawnRoot;
        Subscribe();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (turnManager == null)
        {
            return;
        }

        turnManager.TurnStarted -= HandleTurnStarted;
        turnManager.TurnStarted += HandleTurnStarted;
    }

    private void Unsubscribe()
    {
        if (turnManager != null)
        {
            turnManager.TurnStarted -= HandleTurnStarted;
        }
    }

    private void HandleTurnStarted(TurnCharacterController currentCharacter)
    {
        turnsObserved++;
        if (turnsObserved % Mathf.Max(1, spawnEveryTurns) == 0)
        {
            TrySpawn();
        }
    }

    public bool TrySpawn()
    {
        if (CommonHeadItem.ActiveCount >= maxActiveItems || spawnRoot == null || spawnRoot.childCount == 0)
        {
            return false;
        }

        Transform point = spawnRoot.GetChild(Random.Range(0, spawnRoot.childCount));
        CommonHeadType type = (CommonHeadType)Random.Range(
            (int)CommonHeadType.Attack,
            (int)CommonHeadType.TerrainCreation + 1);

        CommonHeadItem.Create(type, point.position, LoadTemporarySprite(type));
        Debug.Log($"Spawned {type} common head at {point.name}. Active: {CommonHeadItem.ActiveCount}/{maxActiveItems}");
        return true;
    }

    private Sprite LoadTemporarySprite(CommonHeadType type)
    {
        string path;
        switch (type)
        {
            case CommonHeadType.Attack:
                path = "Sprites/Heads/head_bomb_red";
                break;
            case CommonHeadType.Mobility:
                path = "Sprites/Heads/head_bulb_on";
                break;
            default:
                path = "Sprites/Heads/head_seed";
                break;
        }

        return Resources.Load<Sprite>(path);
    }
}
