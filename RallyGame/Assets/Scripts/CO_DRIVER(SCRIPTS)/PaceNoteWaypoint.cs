using UnityEngine;

// Put this on an empty object, placed on the road where a note should be called.
// Drag the tile you want into "Note" (for example 04_5_left). That sprite is exactly what shows,
// so left/right is whatever you pick. Order is set by the hierarchy order under the PaceNoteTrack.
public class PaceNoteWaypoint : MonoBehaviour
{
    [Tooltip("The tile to show, e.g. 04_5_left")]
    public Sprite note;

    [Tooltip("Optional small badge in the corner, e.g. Caution or Dont cut")]
    public Sprite badge;

    [Tooltip("How close the car must get for the note to count as passed. 0 = use the Track's value.")]
    public float passRadius = 0f;
}