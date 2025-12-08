using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using System.Collections.Generic;

public class CameraGridFocusController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform mainGridCenter;
    [SerializeField] private Transform subGridCenter;

    [Header("Zoom")]
    [SerializeField] private float minOrthographicSize = 3f;
    [SerializeField] private float maxOrthographicSize = 12f;
    [SerializeField] private float scrollZoomStep = 0.1f;

    [Header("Input")]
    [SerializeField] private Key toggleFocusKey = Key.Tab;
    [SerializeField] private Button toggleFocusButton;

    [Header("UI State")]
    [SerializeField] private TextMeshProUGUI focusLabel;
    [SerializeField] private string mainGridLabel = "Main Grid";
    [SerializeField] private string subGridLabel = "Subgrid";

    [Header("Defaults")]
    [SerializeField] private bool startWithSubGridActive;

    [Header("UI Panels")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject inspectorPanel;
    [SerializeField] private GameObject wirePipeUIRoot;

    [Header("Chat Bubbles")]
    [SerializeField] private GameObject chatBubbleContainer;
    [SerializeField] private bool keepChatBubblesVisibleInSubGrid = true;

    public bool IsSubGridActive { get; private set; }

    private readonly Dictionary<GameObject, bool> defaultPanelVisibility = new();

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        ClampOrthographicSize();
    }

    private void OnEnable()
    {
        CacheDefaultPanelStates();

        if (toggleFocusButton != null)
        {
            toggleFocusButton.onClick.AddListener(ToggleFocus);
        }

        if (startWithSubGridActive)
        {
            FocusSubGrid();
        }
        else
        {
            FocusMainGrid();
        }
    }

    private void OnDisable()
    {
        if (toggleFocusButton != null)
        {
            toggleFocusButton.onClick.RemoveListener(ToggleFocus);
        }
    }

    private void Update()
    {
        HandleHotkey();
        HandleScrollZoom();
    }

    public void FocusMainGrid()
    {
        if (mainGridCenter != null)
        {
            CenterCamera(mainGridCenter.position);
        }

        IsSubGridActive = false;
        UpdateFocusIndicator();
        ApplyFrontViewVisibility();
    }

    public void FocusSubGrid()
    {
        if (subGridCenter != null)
        {
            CenterCamera(subGridCenter.position);
        }

        IsSubGridActive = true;
        UpdateFocusIndicator();
        ApplyFrontViewVisibility();
    }

    public void ToggleFocus()
    {
        if (IsSubGridActive)
        {
            FocusMainGrid();
        }
        else
        {
            FocusSubGrid();
        }
    }

    private void CenterCamera(Vector3 targetPosition)
    {
        if (targetCamera == null)
        {
            return;
        }

        Vector3 cameraPosition = targetCamera.transform.position;
        cameraPosition.x = targetPosition.x;
        cameraPosition.y = targetPosition.y;
        targetCamera.transform.position = cameraPosition;
    }

    private void HandleHotkey()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        KeyControl keyControl = Keyboard.current[toggleFocusKey];
        if (keyControl != null && keyControl.wasPressedThisFrame)
        {
            ToggleFocus();
        }
    }

    private void HandleScrollZoom()
    {
        if (targetCamera == null || !targetCamera.orthographic || Mouse.current == null)
        {
            return;
        }

        float scrollDelta = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Approximately(scrollDelta, 0f))
        {
            return;
        }

        float newSize = targetCamera.orthographicSize - (scrollDelta * scrollZoomStep);
        targetCamera.orthographicSize = Mathf.Clamp(newSize, minOrthographicSize, maxOrthographicSize);
    }

    private void ClampOrthographicSize()
    {
        if (targetCamera == null || !targetCamera.orthographic)
        {
            return;
        }

        targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize, minOrthographicSize, maxOrthographicSize);
    }

    private void UpdateFocusIndicator()
    {
        if (focusLabel == null)
        {
            return;
        }

        focusLabel.text = IsSubGridActive ? subGridLabel : mainGridLabel;
    }

    private void CacheDefaultPanelStates()
    {
        CacheDefaultState(inventoryPanel);
        CacheDefaultState(inspectorPanel);
        CacheDefaultState(wirePipeUIRoot);
        CacheDefaultState(chatBubbleContainer);
    }

    private void CacheDefaultState(GameObject panel)
    {
        if (panel == null || defaultPanelVisibility.ContainsKey(panel))
        {
            return;
        }

        defaultPanelVisibility[panel] = panel.activeSelf;
    }

    private void ApplyFrontViewVisibility()
    {
        bool shouldShowPanels = !IsSubGridActive;

        ApplyVisibility(inventoryPanel, shouldShowPanels);
        ApplyVisibility(inspectorPanel, shouldShowPanels);
        ApplyVisibility(wirePipeUIRoot, shouldShowPanels);

        bool shouldShowChatBubbles = keepChatBubblesVisibleInSubGrid || shouldShowPanels;
        ApplyVisibility(chatBubbleContainer, shouldShowChatBubbles);
    }

    private void ApplyVisibility(GameObject panel, bool shouldShow)
    {
        if (panel == null)
        {
            return;
        }

        bool desiredState = shouldShow
            ? defaultPanelVisibility.GetValueOrDefault(panel, true)
            : false;

        if (panel.activeSelf != desiredState)
        {
            panel.SetActive(desiredState);
        }
    }
}
