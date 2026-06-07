using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class GroundHazardSegment : MonoBehaviour
{
    private GroundHazardZone owner;

    public void Initialize(GroundHazardZone zone)
    {
        owner = zone;
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        owner?.NotifySegmentEnter(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        owner?.NotifySegmentExit(other);
    }
}
