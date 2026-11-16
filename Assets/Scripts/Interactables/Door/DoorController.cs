using UnityEngine;

// Goes directly on the Door object now — no separate parenting Hinge object required.
public class DoorController : MonoBehaviour
{
    [SerializeField] private Transform hingePoint;      // empty placed at the pivot corner (just needs its position, no parenting relationship)
    [SerializeField] private Vector3 hingeAxis = Vector3.up; // kept as Z (Vector3.forward) to match your original script; switch to Vector3.up if this is a 3D door, not a 2D top-down one
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float speed = 2f;           // same meaning as before: higher = snappier ease-out

    private bool isOpen = false;
    private float currentAngle = 0f;
    private float targetAngle = 0f;

    public void Toggle()
    {
        isOpen = !isOpen;
        targetAngle = isOpen ? openAngle : 0f;
    }

    void Update()
    {
        if (Mathf.Approximately(currentAngle, targetAngle)) return;

        float newAngle = Mathf.Lerp(currentAngle, targetAngle, Time.deltaTime * speed);
        if (Mathf.Abs(targetAngle - newAngle) < 0.01f) newAngle = targetAngle; // snap so it doesn't crawl forever

        float delta = newAngle - currentAngle;
        transform.RotateAround(hingePoint.position, hingeAxis, delta);
        currentAngle = newAngle;
    }
}
