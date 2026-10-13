using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class AutoSelectButton : MonoBehaviour
{
    public Selectable firstSelected;   // the button that starts selected

    GameObject lastSelected;

    void OnEnable()
    {
        StartCoroutine(SelectFirst());
    }

    IEnumerator SelectFirst()
    {
        yield return null;   // wait a frame so the buttons are active
        if (EventSystem.current == null || firstSelected == null) yield break;
        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
    }

    void Update()
    {
        var es = EventSystem.current;
        if (es == null) return;

        // remember whatever is selected right now
        if (es.currentSelectedGameObject != null)
        {
            lastSelected = es.currentSelectedGameObject;
            return;
        }

        // nothing selected: an arrow key brings the last button back
        if (lastSelected != null && lastSelected.activeInHierarchy && ArrowPressed())
        {
            var sel = lastSelected.GetComponent<Selectable>();
            if (sel != null && sel.IsInteractable())
                es.SetSelectedGameObject(lastSelected);
        }
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