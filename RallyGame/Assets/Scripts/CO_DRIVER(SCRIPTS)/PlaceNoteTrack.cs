using System.Collections.Generic;
using UnityEngine;

// Calls your hand-placed waypoints one after another.
// Put this on an empty object, then make PaceNoteWaypoint objects as its children, in driving order.
//
// A note appears when the car is within "Call Distance" of its waypoint,
// and is completed once the car has reached the waypoint and is driving away from it.
public class PaceNoteTrack : MonoBehaviour
{
    [Header("Hook up")]
    public PaceNoteHUD hud;
    public Transform car;

    [Header("Timing (metres)")]
    [Tooltip("Show a note when the car is this close to its waypoint")]
    public float callDistance = 150f;
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

    // Call this again after a respawn / restart
    public void Restart()
    {
        GetComponentsInChildren(points); // hierarchy order = driving order
        shown.Clear();
        headClosest = float.MaxValue;
        hud.Clear();

        if (points.Count == 0)
        {
            Debug.LogWarning("PaceNoteTrack: no PaceNoteWaypoint objects found under this object.", this);
            return;
        }

        for (int i = 0; i < points.Count; i++)
            if (points[i].note == null)
                Debug.LogWarning("PaceNoteTrack: waypoint '" + points[i].name + "' has no Note sprite.", points[i]);

        // Start from the waypoint just ahead of the car (so a car that starts mid-road doesn't get old notes)
        int nearest = 0;
        float best = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            float d = Dist(points[i]);
            if (d < best) { best = d; nearest = i; }
        }

        next = nearest;
        Vector3 wp = points[nearest].transform.position;
        Vector3 along = Vector3.zero;
        if (nearest + 1 < points.Count) along = points[nearest + 1].transform.position - wp;
        else if (loop && points.Count > 1) along = points[0].transform.position - wp;
        else if (nearest > 0) along = wp - points[nearest - 1].transform.position;

        if (Vector3.Dot(car.position - wp, along) > 0f) next = nearest + 1; // already past it

        if (debugLog) Debug.Log("PaceNoteTrack: " + points.Count + " waypoints, starting at #" + (Mathf.Min(next, points.Count - 1) + 1) + ".", this);
    }

    void Update()
    {
        if (points.Count == 0) return;

        // 1) Show upcoming notes, strictly in order
        int guard = points.Count;
        while (guard-- > 0)
        {
            if (next >= points.Count)
            {
                if (loop) next = 0;
                else break;
            }

            PaceNoteWaypoint w = points[next];
            if (shown.Contains(w)) break;              // the whole lap is already on screen
            if (Dist(w) > callDistance) break;         // not close enough yet

            hud.PushNote(w.note, w.badge);
            shown.Add(w);
            next++;

            if (debugLog) Debug.Log("PaceNoteTrack: called '" + (w.note != null ? w.note.name : "no sprite") + "'", w);
        }

        // 2) Complete the current (first) note once the car has gone past it
        if (shown.Count > 0)
        {
            PaceNoteWaypoint head = shown[0];
            float d = Dist(head);
            if (d < headClosest) headClosest = d;

            float radius = head.passRadius > 0f ? head.passRadius : passRadius;
            bool reachedAndLeaving = headClosest <= radius && d > headClosest + 5f;
            bool overtaken = shown.Count > 1 && headClosest <= radius * 2f && Dist(shown[1]) < d;
            bool leftFarBehind = d > callDistance + 60f;

            if (reachedAndLeaving || overtaken || leftFarBehind)
            {
                shown.RemoveAt(0);
                hud.CompleteCurrentNote();
                headClosest = float.MaxValue;
            }
        }
    }

    // Flat distance (ignores height) from the car to a waypoint
    float Dist(PaceNoteWaypoint w)
    {
        Vector3 a = car.position;
        Vector3 b = w.transform.position;
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

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