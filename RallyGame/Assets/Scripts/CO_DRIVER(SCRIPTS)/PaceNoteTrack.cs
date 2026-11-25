using System.Collections.Generic;
using UnityEngine;

// Calls your hand-placed waypoints one after another.
// Put this on an empty object, then make PaceNoteWaypoint objects as its children, in driving order.
//
// Look ahead along the authored route and retire notes as the car passes each waypoint.
public class PaceNoteTrack : MonoBehaviour
{
    [Header("Hook up")]
    public PaceNoteHUD hud;
    public Transform car;

    [Header("Timing (metres)")]
    [Tooltip("Maximum warning distance for upcoming notes, in metres.")]
    [Min(1f)] public float callDistance = 150f;
    [Min(0f)] public float minimumLeadDistance = 45f;
    [Min(0f)] public float leadTime = 3.5f;
    [Tooltip("The car must get this close to a waypoint to pass it (a waypoint can override this)")]
    public float passRadius = 25f;
    [Tooltip("Tick for a circuit: after the last waypoint it starts again at the first")]
    public bool loop = false;

    [Header("Debug")]
    [Tooltip("Scene view: orange = left, cyan = right (from the sprite name), with the sprite name above each point")]
    public bool drawGizmos = true;
    public bool debugLog = true;

    readonly List<PaceNoteWaypoint> points = new List<PaceNoteWaypoint>();
    readonly List<PaceNoteWaypoint> shown = new List<PaceNoteWaypoint>(); // on the HUD right now, same order as the HUD
    int next;
    float headClosest = float.MaxValue;
    Vector2 previousPosition;

    void Start()
    {
        if (hud == null) hud = GetComponent<PaceNoteHUD>();
        if (hud == null)
        {
            Debug.LogError("PaceNoteTrack: drag the PaceNoteHUD object into the Hud slot.", this);
            enabled = false;
            return;
        }
        if (car == null)
        {
            Debug.LogError("PaceNoteTrack: drag your car into the Car slot.", this);
            enabled = false;
            return;
        }
        Restart();
    }

    // Call this after repositioning the car at a new point on the stage.
    public void Restart()
    {
        if (hud == null || car == null)
            return;

        GetComponentsInChildren(points); // hierarchy order = driving order
        points.RemoveAll(point => point.note == null);
        shown.Clear();
        next = 0;
        headClosest = float.MaxValue;
        previousPosition = Flat(car.position);
        hud.Clear();

        if (points.Count == 0)
        {
            Debug.LogWarning("PaceNoteTrack: no waypoints with a Note sprite were found.", this);
            return;
        }

        float best = float.PositiveInfinity;
        for (int i = 0; i < points.Count; i++)
        {
            float distance = (previousPosition - Position(i)).sqrMagnitude;
            if (distance >= best)
                continue;

            best = distance;
            next = i;
        }

        if (Vector2.Dot(previousPosition - Position(next), TravelDirection(next)) > 2f)
            next++;

        if (debugLog)
            Debug.Log($"PaceNoteTrack: {points.Count} waypoints, starting at #{Mathf.Min(next + 1, points.Count)}.", this);
    }

    void Update()
    {
        AdvanceNotes(Flat(car.position), Time.deltaTime);
    }

    void AdvanceNotes(Vector2 position, float dt)
    {
        if (points.Count == 0 || dt <= 0f)
            return;

        float speed = Vector2.Distance(previousPosition, position) / dt;
        // Rejoin the route on a teleport rather than sweeping through unrelated notes.
        if (speed > 120f && Vector2.Distance(previousPosition, position) > 30f)
        {
            Restart();
            return;
        }

        int remaining = points.Count;
        while (shown.Count > 0 && remaining-- > 0 && HasPassed(shown[0], position))
        {
            shown.RemoveAt(0);
            hud.CompleteCurrentNote();
            headClosest = float.MaxValue;
        }

        float lead = Mathf.Min(callDistance, minimumLeadDistance + speed * leadTime);
        PaceNoteWaypoint tail = null;
        float distanceAhead = 0f;
        foreach (PaceNoteWaypoint waypoint in shown)
        {
            Vector2 point = Flat(waypoint.transform.position);
            distanceAhead += Vector2.Distance(tail == null ? position : Flat(tail.transform.position), point);
            tail = waypoint;
        }

        while (shown.Count < hud.VisibleCapacity)
        {
            if (next >= points.Count)
            {
                if (!loop) break;
                next = 0;
            }

            PaceNoteWaypoint waypoint = points[next];
            if (shown.Contains(waypoint))
                break;

            distanceAhead += Vector2.Distance(tail == null ? position : Flat(tail.transform.position), Position(next));
            if (distanceAhead > lead)
                break;

            hud.PushNote(waypoint.note, waypoint.badge);
            shown.Add(waypoint);
            tail = waypoint;
            next++;
            if (debugLog)
                Debug.Log($"PaceNoteTrack: called '{waypoint.note.name}'.", waypoint);
        }

        previousPosition = position;
    }

    bool HasPassed(PaceNoteWaypoint waypoint, Vector2 position)
    {
        int index = points.IndexOf(waypoint);
        Vector2 marker = Position(index);
        Vector2 movement = position - previousPosition;
        float step = movement.sqrMagnitude;
        float t = step > 0f ? Mathf.Clamp01(Vector2.Dot(marker - previousPosition, movement) / step) : 0f;
        float sweptDistance = Vector2.Distance(marker, previousPosition + movement * t);
        float distance = Vector2.Distance(marker, position);
        headClosest = Mathf.Min(headClosest, sweptDistance);

        float radius = waypoint.passRadius > 0f ? waypoint.passRadius : passRadius;
        Vector2 direction = TravelDirection(index);
        if (direction == Vector2.zero)
            return headClosest <= radius && distance > headClosest + 3f;

        float before = Vector2.Dot(previousPosition - marker, direction);
        float after = Vector2.Dot(position - marker, direction);
        bool crossed = before <= 0f && after > 0f && sweptDistance <= radius;
        bool leavingForward = after > 0f && after > before && headClosest <= radius && distance > headClosest + 3f;
        if (crossed || leavingForward)
            return true;

        // Recover a missed gate only once the car is approaching the immediate next note.
        if (index + 1 >= points.Count && !loop)
            return false;
        Vector2 following = Position((index + 1) % points.Count);
        float followingDistance = Vector2.Distance(position, following);
        return headClosest <= radius * 2f && following != marker && followingDistance <= radius &&
            followingDistance < distance && Vector2.Dot(movement, following - previousPosition) > 0f;
    }

    Vector2 TravelDirection(int index)
    {
        Vector2 here = Position(index);
        Vector2 incoming = index > 0 ? (here - Position(index - 1)).normalized :
            loop && points.Count > 1 ? (here - Position(points.Count - 1)).normalized : Vector2.zero;
        Vector2 outgoing = index + 1 < points.Count ? (Position(index + 1) - here).normalized :
            loop && points.Count > 1 ? (Position(0) - here).normalized : Vector2.zero;
        // At a hairpin the incoming and outgoing directions nearly cancel.
        return Vector2.Dot(incoming, outgoing) < -0.25f ? outgoing : (incoming + outgoing).normalized;
    }

    Vector2 Position(int index) => Flat(points[index].transform.position);
    static Vector2 Flat(Vector3 position) => new Vector2(position.x, position.z);

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        PaceNoteWaypoint[] all = GetComponentsInChildren<PaceNoteWaypoint>();
        for (int i = 0; i < all.Length; i++)
        {
            Vector3 p = all[i].transform.position;
            string n = all[i].note != null ? all[i].note.name.ToLowerInvariant() : "";

            Gizmos.color = n.Contains("left") ? new Color(1f, 0.6f, 0.1f)
                         : n.Contains("right") ? new Color(0.2f, 0.8f, 1f)
                         : Color.yellow;
            Gizmos.DrawSphere(p, 1.5f);

            float r = all[i].passRadius > 0f ? all[i].passRadius : passRadius;
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireSphere(p, r);

            Gizmos.color = Color.white;
            if (i + 1 < all.Length) Gizmos.DrawLine(p, all[i + 1].transform.position);
            else if (loop && all.Length > 1) Gizmos.DrawLine(p, all[0].transform.position);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(p + Vector3.up * 3f, (i + 1) + ": " + (all[i].note != null ? all[i].note.name : "NO SPRITE"));
#endif
        }
    }
}