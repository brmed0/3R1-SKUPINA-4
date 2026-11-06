using System.Collections;
using UnityEngine;

// Temporary test script: feeds random notes into the HUD so you can
// see the animation without driving. Delete it once the real co-driver calls the notes.
public class PaceNoteDemo : MonoBehaviour
{
    public PaceNoteHUD hud;
    [Tooltip("Any note sprites (the corner tiles)")]
    public Sprite[] icons;
    [Tooltip("Optional: Caution and Dont cut")]
    public Sprite[] badges;
    [Tooltip("Seconds between notes")]
    public float interval = 2f;

    IEnumerator Start()
    {
        if (hud == null)
        {
            Debug.LogError("PaceNoteDemo: drag the PaceNoteHUD object into the Hud slot.", this);
            yield break;
        }
        if (icons == null || icons.Length == 0)
        {
            Debug.LogError("PaceNoteDemo: the Icons list is empty.", this);
            yield break;
        }

        // Fill the row with a small stagger
        for (int i = 0; i < 3; i++)
        {
            hud.PushNote(RandomIcon(), RandomBadge());
            yield return new WaitForSecondsRealtime(0.15f);
        }

        // Then "drive past" a note every few seconds and call a new one
        while (true)
        {
            yield return new WaitForSecondsRealtime(interval);
            hud.CompleteCurrentNote();
            hud.PushNote(RandomIcon(), RandomBadge());
        }
    }

    Sprite RandomIcon()
    {
        return icons[Random.Range(0, icons.Length)];
    }

    // About 1 in 4 notes gets a badge
    Sprite RandomBadge()
    {
        if (badges == null || badges.Length == 0 || Random.value > 0.25f) return null;
        return badges[Random.Range(0, badges.Length)];
    }
}