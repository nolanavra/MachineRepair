using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // New Input System
using MachineRepair;

public enum GameMode
{
    Selection = 0,
    ComponentPlacement = 1,
    WirePlacement = 2,
    PipePlacement = 3,
    Simulation = 4
}

public interface IGameModeListener
{
    // Called AFTER a mode becomes active
    void OnEnterMode(GameMode newMode);

    // Called BEFORE the old mode is left
    void OnExitMode(GameMode oldMode);
}

[DefaultExecutionOrder(-100)]
public class GameModeManager : MonoBehaviour
{
    public static GameModeManager Instance { get; private set; }

    [Header("Startup")]
    [SerializeField] private GameMode initialMode = GameMode.Selection;

    [Header("Hotkeys (New Input System)")]
    [Tooltip("PlayerInput action map name containing mode selection bindings.")]
    [SerializeField] private string gameplayMapName = "Gameplay";
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private string componentPlacementActionName = "ModeComponentPlacement";
    [SerializeField] private string wirePlacementActionName = "ModeWirePlacement";
    [SerializeField] private string pipePlacementActionName = "ModePipePlacement";
    [SerializeField] private string selectionActionName = "ModeSelection";
    [SerializeField] private string simulationActionName = "ModeSimulation";
    [SerializeField] private SimulationManager simulationManager;

    public GameMode CurrentMode { get; private set; }

    public event Action<GameMode, GameMode> OnModeChanged; // (old,new)

    private readonly List<IGameModeListener> _listeners = new();
    private InputAction componentPlacementAction;
    private InputAction wirePlacementAction;
    private InputAction pipePlacementAction;
    private InputAction selectionAction;
    private InputAction simulationAction;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (simulationManager == null)
        {
            simulationManager = FindFirstObjectByType<SimulationManager>();
        }
    }

    private void OnEnable()
    {
        if (playerInput == null) playerInput = FindFirstObjectByType<PlayerInput>();
        CacheModeActions();
        EnableModeActions();
    }

    private void OnDisable()
    {
        DisableModeActions();
    }

    private void Start()
    {
        SetMode(initialMode, fireEvents: false); // set silently at boot
        // Now announce so UI & systems can initialize cleanly
        ForceAnnounceMode();
    }

    public void RegisterListener(IGameModeListener listener)
    {
        if (listener == null) return;
        if (!_listeners.Contains(listener)) _listeners.Add(listener);

        // Immediately inform the new listener of current mode
        listener.OnEnterMode(CurrentMode);  // <<< ensure this line exists
        Debug.Log($"[GameModeManager] Listener registered: {listener.GetType().Name}, current={CurrentMode}");
    }


    public void UnregisterListener(IGameModeListener listener)
    {
        if (listener == null) return;
        _listeners.Remove(listener);
    }

    public void SetMode(GameMode newMode, bool fireEvents = true)
    {
        if (newMode == CurrentMode && fireEvents) return;

        GameMode old = CurrentMode;

        if (fireEvents)
        {
            // Exit old
            for (int i = 0; i < _listeners.Count; i++)
                _listeners[i].OnExitMode(old);
        }

        CurrentMode = newMode;

        if (fireEvents)
        {
            // Enter new
            for (int i = 0; i < _listeners.Count; i++)
                _listeners[i].OnEnterMode(CurrentMode);

            OnModeChanged?.Invoke(old, CurrentMode);
        }
    }

    public void ToggleSimulation()
    {
        if (simulationManager == null)
        {
            simulationManager = FindFirstObjectByType<SimulationManager>();
        }

        if (simulationManager == null)
        {
            Debug.LogWarning("[GameModeManager] No SimulationManager assigned; cannot toggle simulation.");
            return;
        }

        simulationManager.ToggleSimulationRunning();
    }

    public static bool Is(GameMode mode) =>
        Instance != null && Instance.CurrentMode == mode;

    public static string ModeToDisplay(GameMode m) => m switch
    {
        GameMode.Selection => "Selection",
        GameMode.ComponentPlacement => "Component Placement",
        GameMode.WirePlacement => "Wire Placement",
        GameMode.PipePlacement => "Pipe Placement",
        GameMode.Simulation => "Simulation",
        _ => m.ToString()
    };

    private void ForceAnnounceMode()
    {
        // Inform listeners on Start even if we didnt change
        for (int i = 0; i < _listeners.Count; i++)
            _listeners[i].OnEnterMode(CurrentMode);

        OnModeChanged?.Invoke(CurrentMode, CurrentMode);
        Debug.Log("AnnounceMode");
    }

    private void CacheModeActions()
    {
        if (playerInput == null || playerInput.actions == null) return;

        var map = playerInput.actions.FindActionMap(gameplayMapName, throwIfNotFound: false);
        if (map == null) return;

        componentPlacementAction = map.FindAction(componentPlacementActionName, throwIfNotFound: false);
        wirePlacementAction = map.FindAction(wirePlacementActionName, throwIfNotFound: false);
        pipePlacementAction = map.FindAction(pipePlacementActionName, throwIfNotFound: false);
        selectionAction = map.FindAction(selectionActionName, throwIfNotFound: false);
        simulationAction = map.FindAction(simulationActionName, throwIfNotFound: false);
    }

    private void EnableModeActions()
    {
        if (componentPlacementAction != null)
        {
            componentPlacementAction.performed += OnComponentPlacementPerformed;
            componentPlacementAction.Enable();
        }

        if (wirePlacementAction != null)
        {
            wirePlacementAction.performed += OnWirePlacementPerformed;
            wirePlacementAction.Enable();
        }

        if (pipePlacementAction != null)
        {
            pipePlacementAction.performed += OnPipePlacementPerformed;
            pipePlacementAction.Enable();
        }

        if (selectionAction != null)
        {
            selectionAction.performed += OnSelectionPerformed;
            selectionAction.Enable();
        }

        if (simulationAction != null)
        {
            simulationAction.performed += OnSimulationPerformed;
            simulationAction.Enable();
        }
    }

    private void DisableModeActions()
    {
        if (componentPlacementAction != null)
        {
            componentPlacementAction.performed -= OnComponentPlacementPerformed;
            componentPlacementAction.Disable();
        }

        if (wirePlacementAction != null)
        {
            wirePlacementAction.performed -= OnWirePlacementPerformed;
            wirePlacementAction.Disable();
        }

        if (pipePlacementAction != null)
        {
            pipePlacementAction.performed -= OnPipePlacementPerformed;
            pipePlacementAction.Disable();
        }

        if (selectionAction != null)
        {
            selectionAction.performed -= OnSelectionPerformed;
            selectionAction.Disable();
        }

        if (simulationAction != null)
        {
            simulationAction.performed -= OnSimulationPerformed;
            simulationAction.Disable();
        }
    }

    private void OnComponentPlacementPerformed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        SetMode(GameMode.ComponentPlacement);
    }

    private void OnWirePlacementPerformed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        SetMode(GameMode.WirePlacement);
    }

    private void OnPipePlacementPerformed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        SetMode(GameMode.PipePlacement);
    }

    private void OnSelectionPerformed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        SetMode(GameMode.Selection);
    }

    private void OnSimulationPerformed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        ToggleSimulation();
    }
}
