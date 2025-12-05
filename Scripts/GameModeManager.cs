using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // New Input System

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
    [Tooltip("Enable number-key hotkeys: 1=Component, 2=Wire, 3=Pipe, 4=Selection, 5=Simulation")]
    [SerializeField] private bool enableHotkeys = true;

    public GameMode CurrentMode { get; private set; }

    public event Action<GameMode, GameMode> OnModeChanged; // (old,new)

    private readonly List<IGameModeListener> _listeners = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        SetMode(initialMode, fireEvents: false); // set silently at boot
        // Now announce so UI & systems can initialize cleanly
        ForceAnnounceMode();
    }

    private void Update()
    {
        if (!enableHotkeys) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        // 1..5 map to modes
        if (kb.digit2Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) SetMode(GameMode.ComponentPlacement);
        if (kb.digit3Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) SetMode(GameMode.WirePlacement);
        if (kb.digit4Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) SetMode(GameMode.PipePlacement);
        if (kb.digit5Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) SetMode(GameMode.Selection);
        if (kb.digit1Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) SetMode(GameMode.Simulation);

        // Optional: quick toggle Simulation with Space (comment out if not desired)
        // if (kb.spaceKey.wasPressedThisFrame) ToggleSimulation();
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
        if (CurrentMode == GameMode.Simulation)
            SetMode(GameMode.Selection);
        else
            SetMode(GameMode.Simulation);
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
        // Inform listeners on Start even if we didn’t “change”
        for (int i = 0; i < _listeners.Count; i++)
            _listeners[i].OnEnterMode(CurrentMode);

        OnModeChanged?.Invoke(CurrentMode, CurrentMode);
        Debug.Log("AnnounceMode");
    }


}

