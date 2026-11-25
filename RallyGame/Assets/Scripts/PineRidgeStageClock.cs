using UnityEngine;

public sealed class PineRidgeStageClock : MonoBehaviour
{
    [SerializeField] private Transform car;
    [SerializeField] private Transform startLine;
    [SerializeField] private Transform finishLine;
    [SerializeField] private StopwatchTimer timer;
    [SerializeField, Min(1f)] private float gateHalfWidth = 5.2f;

    [SerializeField] private Transform[] checkpoints = System.Array.Empty<Transform>();

    private Vector3 previousPosition;
    private int nextCheckpoint;
    public int CheckpointsCleared => nextCheckpoint;
    public int CheckpointCount => checkpoints?.Length ?? 0;
    public bool Running => running && !finished;
    public bool Finished => finished;
    public bool MissedCheckpoint { get; private set; }
    public float ElapsedSeconds => timer != null ? timer.ElapsedSeconds : 0f;
    public float? BestSeconds { get; private set; }
    private bool running;
    private bool finished;

    private void Start()
    {
        if (car == null || startLine == null || finishLine == null || timer == null)
        {
            enabled = false;
            return;
        }
        ResetRun();
    }

    public void ResetRun()
    {
        if (car == null || timer == null) return;
        previousPosition = car.position;
        running = finished = MissedCheckpoint = false;
        nextCheckpoint = 0;
        timer.StopTimer();
        timer.ResetTimer();
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
        else if (running)
        {
            if (nextCheckpoint < CheckpointCount && Crossed(checkpoints[nextCheckpoint], position))
                nextCheckpoint++;

            if (Crossed(finishLine, position))
            {
                finished = true;
                MissedCheckpoint = nextCheckpoint < CheckpointCount;
                timer.StopTimer();
                if (!MissedCheckpoint && (!BestSeconds.HasValue || ElapsedSeconds < BestSeconds.Value))
                    BestSeconds = ElapsedSeconds;
            }
        }

        previousPosition = position;
    }

    private bool Crossed(Transform gate, Vector3 position)
    {
        if (gate == null) return false;

        float before = Vector3.Dot(previousPosition - gate.position, gate.forward);
        float after = Vector3.Dot(position - gate.position, gate.forward);
        if (before >= 0f || after < 0f || after <= before) return false;

        Vector3 crossing = Vector3.Lerp(previousPosition, position, -before / (after - before));
        Vector3 relative = crossing - gate.position;
        return Mathf.Abs(Vector3.Dot(relative, gate.right)) <= gateHalfWidth &&
            Mathf.Abs(relative.y) <= 3f;
    }
}
