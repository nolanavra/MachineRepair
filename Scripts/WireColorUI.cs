using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WireColorUI : MonoBehaviour, IGameModeListener
{
    [Header("References")]
    [SerializeField] private WirePlacementTool wireTool;
    [SerializeField] private GameModeManager gameModeManager;

    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Button redButton;
    [SerializeField] private Button blackButton;
    [SerializeField] private Button greenButton;
    [SerializeField] private Button blueButton;
    [SerializeField] private Button purpleButton;
    [SerializeField] private Button orangeButton;

    private readonly Dictionary<Button, Color> buttonColors = new();

    private void Awake()
    {
        if (wireTool == null) wireTool = FindFirstObjectByType<WirePlacementTool>();
        if (gameModeManager == null) gameModeManager = GameModeManager.Instance;

        buttonColors[redButton] = Color.red;
        buttonColors[blackButton] = Color.black;
        buttonColors[greenButton] = Color.green;
        buttonColors[blueButton] = Color.blue;
        buttonColors[purpleButton] = new Color(0.5f, 0f, 0.5f);
        buttonColors[orangeButton] = new Color(1f, 0.5f, 0f);
    }

    private void OnEnable()
    {
        if (gameModeManager != null)
        {
            gameModeManager.RegisterListener(this);
            SetPanelVisible(gameModeManager.CurrentMode == GameMode.WirePlacement);
        }

        HookupButtons();
    }

    private void OnDisable()
    {
        if (gameModeManager != null)
        {
            gameModeManager.UnregisterListener(this);
        }

        UnhookButtons();
    }

    public void OnEnterMode(GameMode newMode)
    {
        SetPanelVisible(newMode == GameMode.WirePlacement);
    }

    public void OnExitMode(GameMode oldMode)
    {
        if (oldMode == GameMode.WirePlacement)
        {
            SetPanelVisible(false);
        }
    }

    private void HookupButtons()
    {
        foreach (var kvp in buttonColors)
        {
            var button = kvp.Key;
            if (button == null) continue;

            button.onClick.RemoveAllListeners();
            var color = kvp.Value;
            button.onClick.AddListener(() => SelectColor(color));
        }
    }

    private void UnhookButtons()
    {
        foreach (var kvp in buttonColors)
        {
            var button = kvp.Key;
            if (button == null) continue;

            button.onClick.RemoveAllListeners();
        }
    }

    private void SelectColor(Color color)
    {
        if (wireTool == null) return;
        wireTool.SetWireColor(color);
    }

    private void SetPanelVisible(bool visible)
    {
        if (panel != null)
        {
            panel.SetActive(visible);
        }
    }
}
