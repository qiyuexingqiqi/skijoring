using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class SkiFinishTrigger : MonoBehaviour
{
    public SkiRaceManager race;
    private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
    private void OnTriggerEnter(Collider other)
    {
        if (race != null) race.TryFinish(other);
    }

    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.48f, 0.84f, 1f, 0.13f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(0.59f, 0.92f, 0.79f, 0.95f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
