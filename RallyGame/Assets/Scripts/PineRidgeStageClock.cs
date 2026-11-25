using UnityEngine;

public sealed class PineRidgeStageClock : MonoBehaviour
{
    [SerializeField] private Transform car;
    [SerializeField] private Transform startLine;
    [SerializeField] private Transform finishLine;
    [SerializeField] private StopwatchTimer timer;
    [SerializeField, Min(1f)] private float gateHalfWidth = 5.2f;

    private Vector3 previousPosition;
    private bool running;
    private bool finished;

    private void Start()
    {
        if (car == null || startLine == null || finishLine == null || timer == null)
        {
            enabled = false;
            return;
        }
        previousPosition = car.position;
    }

    private void FixedUpdate()
    {
        if (finished) return;

        Vector3 position = car.position;
        if (!running && Crossed(startLine, position))
        {
            running = true;
            timer.StartTimer();
        }
        else if (running && Crossed(finishLine, position))
        {
            finished = true;
            timer.StopTimer();
        }

        previousPosition = position;
    }

    private bool Crossed(Transform gate, Vector3 position)
    {
        float before = Vector3.Dot(previousPosition - gate.position, gate.forward);
        float after = Vector3.Dot(position - gate.position, gate.forward);
        if (before >= 0f || after < 0f || after <= before) return false;

        Vector3 crossing = Vector3.Lerp(previousPosition, position, -before / (after - before));
        Vector3 relative = crossing - gate.position;
        return Mathf.Abs(Vector3.Dot(relative, gate.right)) <= gateHalfWidth &&
            Mathf.Abs(relative.y) <= 3f;
    }
}
