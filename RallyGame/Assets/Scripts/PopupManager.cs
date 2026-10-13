using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// One script that handles everything:
//  - auto-selects a toolbar button
//  - arrow keys move the selection (Unity's EventSystem does the moving)
//  - toolbar buttons open popups, Esc / Backspace / the close button closes them
//  - optional: Tab shows/hides the toolbar and unlocks/locks the mouse
//
// Put this on an empty GameObject (e.g. "UIManager"), NOT on SideToolBar.
public class ToolbarController : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public Button toolbarButton;   // e.g. BTN_setting
        public GameObject popup;       // e.g. Popup_Settings
        public Button closeButton;     // e.g. BTN_Close (inside the popup)
    }

    [Header("Toolbar")]
    public GameObject toolbar;          // SideToolBar
    public CanvasGroup toolbarGroup;    // Canvas Group on SideToolBar (optional)
    public Selectable firstSelected;    // button that starts selected

    [Header("Popups")]
    public Entry[] entries;
    public bool hideToolbarWhilePopupOpen = true;  // true = toolbar disappears while a popup is open

    [Header("Menu toggle (optional)")]
    public bool toggleWithTab = false;  // true = toolbar hidden until Tab is pressed

    Entry currentPopup;
    GameObject lastSelected;
    bool toolbarOpen;

    void Start()
    {
        foreach (var e in entries)
        {
            var entry = e;
            if (entry.popup != null) entry.popup.SetActive(false);
            if (entry.toolbarButton != null) entry.toolbarButton.onClick.AddListener(() => OpenPopup(entry));
            if (entry.closeButton != null) entry.closeButton.onClick.AddListener(ClosePopup);
        }

        SetToolbarOpen(!toggleWithTab);
    }

    void Update()
    {
        if (toggleWithTab && TabPressed())
        {
            SetToolbarOpen(!toolbarOpen);
            return;
        }

        if (!toolbarOpen) return;

        if (currentPopup != null && BackPressed())
        {
            ClosePopup();
            return;
        }

        KeepSelection();
    }

    // ---------- toolbar ----------

    void SetToolbarOpen(bool open)
    {
        toolbarOpen = open;

        HidePopupInstantly();
        if (toolbar != null) toolbar.SetActive(open);
        if (toolbarGroup != null) toolbarGroup.interactable = true;

        // mouse: unlocked while the menu is open (or always, if Tab toggle is off)
        bool showMouse = open || !toggleWithTab;
        Cursor.lockState = showMouse ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = showMouse;

        if (open && firstSelected != null) StartCoroutine(Select(firstSelected.gameObject));
    }

    // ---------- popups ----------

    void OpenPopup(Entry e)
    {
        if (e.popup == null) return;

        currentPopup = e;
        e.popup.SetActive(true);
        if (hideToolbarWhilePopupOpen && toolbar != null) toolbar.SetActive(false);
        else if (toolbarGroup != null) toolbarGroup.interactable = false; // arrows can't leave the popup

        if (e.closeButton != null) StartCoroutine(Select(e.closeButton.gameObject));
    }

    public void ClosePopup()
    {
        if (currentPopup == null) return;

        var back = currentPopup.toolbarButton;
        HidePopupInstantly();
        if (toolbar != null) toolbar.SetActive(true);
        if (toolbarGroup != null) toolbarGroup.interactable = true;

        if (back != null) StartCoroutine(Select(back.gameObject)); // go back to the button you pressed
    }

    void HidePopupInstantly()
    {
        if (currentPopup != null && currentPopup.popup != null) currentPopup.popup.SetActive(false);
        currentPopup = null;
    }

    // ---------- selection ----------

    IEnumerator Select(GameObject go)
    {
        yield return null; // wait a frame so the object is active
        if (EventSystem.current == null || go == null) yield break;
        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(go);
    }

    // If a click on empty space clears the selection, an arrow key brings it back.
    void KeepSelection()
    {
        var es = EventSystem.current;
        if (es == null) return;

        if (es.currentSelectedGameObject != null)
        {
            lastSelected = es.currentSelectedGameObject;
            return;
        }

        if (lastSelected != null && lastSelected.activeInHierarchy && ArrowPressed())
        {
            var sel = lastSelected.GetComponent<Selectable>();
            if (sel != null && sel.IsInteractable()) es.SetSelectedGameObject(lastSelected);
        }
    }

    // ---------- input ----------

    bool TabPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }

    bool BackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace);
#endif
    }

    bool ArrowPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && (k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame
                          || k.leftArrowKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)
            || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow);
#endif
    }
}