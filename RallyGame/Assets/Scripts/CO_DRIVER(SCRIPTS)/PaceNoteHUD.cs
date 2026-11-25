using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Keeps a short row of pace notes on screen. Put this on an empty object INSIDE your Canvas
// (right-click the Canvas > Create Empty). The tiles are built by this script, so there is no prefab.
//
// Slot 0 = the note you are driving into right now (biggest).
// Slots 1, 2... = what is coming next (smaller), lined up to the right.
//
// Your game only needs two calls:
//   PushNote(sprite, badge)  -> a new note was called out (badge is optional)
//   CompleteCurrentNote()    -> the car passed the current note
public class PaceNoteHUD : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Where the tiles live. Leave empty to use this object.")]
    public RectTransform container;

    [Header("Tile look")]
    public float tileSize = 100f;
    public float badgeSize = 40f;
    [Tooltip("Badge position relative to the tile's top-left corner")]
    public Vector2 badgeOffset = new Vector2(6f, -6f);

    [Header("Layout")]
    [Tooltip("Direction the upcoming notes line up in. (1,0) = to the right, (0,-1) = downwards")]
    public Vector2 direction = Vector2.right;
    [Tooltip("Size of each slot relative to the tile. One entry per slot, so 3 entries = 3 notes visible.")]
    public float[] slotScales = { 1f, 0.78f, 0.62f };
    [Tooltip("Empty space between neighbouring tiles")]
    public float gap = 16f;
    [Tooltip("How far the passed note slides back while it fades out")]
    public float exitDistance = 150f;

    [Header("Debug")]
    public bool debugLog = true;

    struct Note
    {
        public Sprite icon;
        public Sprite badge;
    }

    static readonly Color CurrentFrame = new Color(0.83f, 0.94f, 1f);
    static readonly Color PreviewFrame = new Color(0.07f, 0.11f, 0.15f);
    const float FrameInset = 3f;

    public int VisibleCapacity => slotScales != null && slotScales.Length > 0 ? slotScales.Length : 3;

    readonly List<PaceNoteItem> visible = new List<PaceNoteItem>();
    readonly Queue<Note> waiting = new Queue<Note>();

    void Awake()
    {
        if (container == null) container = transform as RectTransform;
        if (container == null)
        {
            Debug.LogError("PaceNoteHUD: this object has a plain Transform, not a Rect Transform. " +
                           "Delete it and create it again by right-clicking your Canvas > Create Empty.", this);
            return;
        }

        // Guard against empty / zero values in the Inspector, which would stack or hide every tile
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        direction = direction.normalized;
        if (slotScales == null || slotScales.Length == 0) slotScales = new float[] { 1f, 0.78f, 0.62f };

        // Draw on top of the other UI under the same Canvas
        transform.SetAsLastSibling();

        if (GetComponentInParent<Canvas>() == null)
            Debug.LogError("PaceNoteHUD: this object is not inside a Canvas, so nothing will be drawn.", this);

        foreach (CanvasGroup g in GetComponentsInParent<CanvasGroup>())
        {
            if (g.alpha < 0.99f)
                Debug.LogWarning("PaceNoteHUD: the Canvas Group on '" + g.name + "' has Alpha " + g.alpha +
                                 ", so the notes will be invisible.", g);
        }

        if (debugLog) Debug.Log("PaceNoteHUD is ready.", this);
    }

    // A new note was called out
    public void PushNote(Sprite icon, Sprite badge = null)
    {
        if (container == null) return;
        waiting.Enqueue(new Note { icon = icon, badge = badge });
        FillSlots();
    }

    // The car passed the current note
    public void CompleteCurrentNote()
    {
        if (container == null || visible.Count == 0) return;

        PaceNoteItem done = visible[0];
        visible.RemoveAt(0);
        done.Dismiss(SlotPosition(0) - direction * exitDistance);

        FillSlots(); // pulls the next waiting note in and shifts everyone forward
    }

    // Remove everything (used when the car respawns / restarts)
    public void Clear()
    {
        if (container == null) return;
        waiting.Clear();
        for (int i = 0; i < visible.Count; i++)
            visible[i].Dismiss(SlotPosition(i) - direction * exitDistance);
        visible.Clear();
    }

    void FillSlots()
    {
        while (visible.Count < slotScales.Length && waiting.Count > 0)
        {
            Note note = waiting.Dequeue();
            int slot = visible.Count;

            PaceNoteItem item = CreateItem();
            // Starts just past its slot and slides in while fading up
            item.Spawn(note.icon, note.badge, SlotPosition(slot) + direction * (gap * 0.5f), slotScales[slot]);
            visible.Add(item);

            if (debugLog)
                Debug.Log("PaceNoteHUD: showing note '" + (note.icon != null ? note.icon.name : "no sprite") + "'", this);
        }
        ApplyLayout();
    }

    // Builds one tile (and its badge) from code, so no prefab is needed
    PaceNoteItem CreateItem()
    {
        GameObject go = new GameObject("PaceNote", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(PaceNoteItem));
        go.layer = container.gameObject.layer;

        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(container, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(tileSize, tileSize);

        Image frame = go.GetComponent<Image>();
        frame.color = PreviewFrame;
        frame.raycastTarget = false;

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.layer = go.layer;
        RectTransform iconRect = (RectTransform)iconGo.transform;
        iconRect.SetParent(rt, false);
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.one * FrameInset;
        iconRect.offsetMax = -Vector2.one * FrameInset;

        Image icon = iconGo.GetComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;

        GameObject badgeGo = new GameObject("Badge", typeof(RectTransform), typeof(Image));
        badgeGo.layer = go.layer;

        RectTransform brt = (RectTransform)badgeGo.transform;
        brt.SetParent(rt, false);
        brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f); // top-left corner of the tile
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(badgeSize, badgeSize);
        brt.anchoredPosition = badgeOffset;

        Image badge = badgeGo.GetComponent<Image>();
        badge.raycastTarget = false;
        badge.preserveAspect = true;

        PaceNoteItem item = go.GetComponent<PaceNoteItem>();
        item.icon = icon;
        item.badge = badge;
        return item;
    }

    // Give every tile its new target; the tiles animate themselves there
    void ApplyLayout()
    {
        for (int i = 0; i < visible.Count; i++)
        {
            PaceNoteItem item = visible[i];
            item.GetComponent<Image>().color = i == 0 ? CurrentFrame : PreviewFrame;
            float opacity = i == 0 ? 1f : i == 1 ? 0.87f : 0.74f;
            item.SetTarget(SlotPosition(i), slotScales[i], opacity);
        }
    }

    // Tiles are packed edge to edge with a constant gap, so smaller tiles sit closer
    Vector2 SlotPosition(int slot)
    {
        float offset = 0f;
        for (int k = 1; k <= slot; k++)
            offset += SlotSize(k - 1) * 0.5f + gap + SlotSize(k) * 0.5f;
        return direction * offset;
    }

    float SlotSize(int slot)
    {
        return tileSize * slotScales[slot];
    }
}