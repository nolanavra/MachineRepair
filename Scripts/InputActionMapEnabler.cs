using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MachineRepair.Input
{
    /// <summary>
    /// Ensures a PlayerInput keeps specified action maps enabled (e.g., UI + Gameplay).
    /// </summary>
    [DisallowMultipleComponent]
    public class InputActionMapEnabler : MonoBehaviour
    {
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private List<string> mapsToEnable = new() { "Gameplay", "UI" };

        private void Awake()
        {
            if (playerInput == null)
                playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            EnableMaps();
        }

        private void EnableMaps()
        {
            if (playerInput == null)
            {
                Debug.LogWarning("InputActionMapEnabler requires a PlayerInput reference.");
                return;
            }

            var asset = playerInput.actions;
            if (asset == null)
            {
                Debug.LogWarning("InputActionMapEnabler: PlayerInput has no actions asset.");
                return;
            }

            for (int i = 0; i < mapsToEnable.Count; i++)
            {
                var mapName = mapsToEnable[i];
                if (string.IsNullOrEmpty(mapName)) continue;

                var map = asset.FindActionMap(mapName, throwIfNotFound: false);
                if (map == null) continue;

                if (!map.enabled)
                    map.Enable();
            }
        }
    }
}
