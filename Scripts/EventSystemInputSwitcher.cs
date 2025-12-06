using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures any EventSystem in the scene uses the Input System UI module.
/// Removes legacy StandaloneInputModule instances that rely on the old Input class,
/// preventing runtime exceptions when the project is set to "Input System" handling only.
/// </summary>
public class EventSystemInputSwitcher : MonoBehaviour
{
    [SerializeField]
    [Tooltip("When true, this component will replace legacy input modules on Awake.")]
    private bool replaceOnAwake = true;

    private void Awake()
    {
        if (replaceOnAwake)
        {
            EnsureInputSystemModule(GetComponent<EventSystem>());
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnforceExistingEventSystems()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var eventSystem in root.GetComponentsInChildren<EventSystem>(true))
            {
                EnsureInputSystemModule(eventSystem);
            }
        }
    }

    private static void EnsureInputSystemModule(EventSystem eventSystem)
    {
        if (eventSystem == null)
            return;

        var standalone = eventSystem.GetComponent<StandaloneInputModule>();
        if (standalone != null)
        {
            Debug.LogWarning($"Replaced legacy StandaloneInputModule on {eventSystem.name} with InputSystemUIInputModule to match project input settings.");
            Object.Destroy(standalone);
        }

        var inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputSystemModule == null)
        {
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }
}
