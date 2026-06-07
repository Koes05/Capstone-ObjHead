using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider2D))]
public class DeathZone : MonoBehaviour
{
    [SerializeField] private UnityEvent<GameObject> entered = new UnityEvent<GameObject>();

    public UnityEvent<GameObject> Entered
    {
        get { return entered; }
    }

    private void Reset()
    {
        Collider2D collider = GetComponent<Collider2D>();
        collider.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        entered.Invoke(other.gameObject);
        CharacterCombat combat = other.GetComponentInParent<CharacterCombat>();
        if (combat != null)
        {
            combat.Die();
        }

        Debug.Log(other.name + " entered DeathZone.");
    }
}
