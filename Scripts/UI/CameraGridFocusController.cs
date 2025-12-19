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

    [Header("Starting Positions")]
    [SerializeField] private Vector2 subGridCameraStartPosition;

    [Header("Zoom")]
    [SerializeField] private float minOrthographicSize = 3f;
    [SerializeField] private float maxOrthographicSize = 25f;
    [SerializeField] private float scrollZoomStep = 0.1f;
    [SerializeField] private float mainGridInitialOrthographicSize = 19f;

    [Header("Pan")]
    [SerializeField] private PanBounds mainGridPanBounds = new(-5f, 5f, -3f, 3f);
    [SerializeField] private PanBounds subGridPanBounds = new(-3f, 3f, -2f, 2f);
    [SerializeField] private float panReturnSpeed = 10f;

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
    public Transform SubGridCenter => subGridCenter;

    private readonly Dictionary<GameObject, bool> defaultPanelVisibility = new();
    private bool isDragging;
    private Vector3 dragStartWorldPosition;
    private Vector3 cameraStartPosition;
    private float mainGridZoomSize;
    private float subGridZoomSize;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        ApplyInitialMainGridZoom();
        ClampOrthographicSize();
        CacheInitialZoomSizes();
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
        HandleMiddleMouseDrag();
        ApplyPanBoundsWhenReleased();
    }

    public void FocusMainGrid()
    {
        if (mainGridCenter != null)
        {
            CenterCamera(mainGridCenter.position);
        }

        CacheActiveZoom();
        ApplyCachedZoom(mainGridZoomSize);

        IsSubGridActive = false;
        UpdateFocusIndicator();
        ApplyFrontViewVisibility();
    }

    public void FocusSubGrid()
    {
        CacheActiveZoom();
        CenterCamera(new Vector3(subGridCameraStartPosition.x, subGridCameraStartPosition.y));

        ApplyCachedZoom(subGridZoomSize);

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
        CacheActiveZoom();
    }

    private void ClampOrthographicSize()
    {
        if (targetCamera == null || !targetCamera.orthographic)
        {
            return;
        }

        targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize, minOrthographicSize, maxOrthographicSize);
        CacheActiveZoom();
    }

    private void CacheInitialZoomSizes()
    {
        if (targetCamera == null || !targetCamera.orthographic)
        {
            return;
        }

        if (Mathf.Approximately(mainGridZoomSize, 0f))
        {
            mainGridZoomSize = targetCamera.orthographicSize;
        }
        subGridZoomSize = targetCamera.orthographicSize;
    }

    private void ApplyInitialMainGridZoom()
    {
        if (targetCamera == null || !targetCamera.orthographic)
        {
            return;
        }

        mainGridZoomSize = Mathf.Clamp(mainGridInitialOrthographicSize, minOrthographicSize, maxOrthographicSize);
        targetCamera.orthographicSize = mainGridZoomSize;
    }

    private void CacheActiveZoom()
    {
        if (targetCamera == null || !targetCamera.orthographic)
        {
            return;
        }

        if (IsSubGridActive)
        {
            subGridZoomSize = targetCamera.orthographicSize;
        }
        else
        {
            mainGridZoomSize = targetCamera.orthographicSize;
        }
    }

    private void ApplyCachedZoom(float zoomSize)
    {
        if (targetCamera == null || !targetCamera.orthographic)
        {
            return;
        }

        targetCamera.orthographicSize = Mathf.Clamp(zoomSize, minOrthographicSize, maxOrthographicSize);
    }

    private void HandleMiddleMouseDrag()
    {
        if (targetCamera == null || Mouse.current == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;

        if (mouse.middleButton.wasPressedThisFrame)
        {
            BeginDrag();
        }
        else if (mouse.middleButton.isPressed && isDragging)
        {
            UpdateDrag();
        }
        else if (mouse.middleButton.wasReleasedThisFrame)
        {
            EndDrag();
        }
    }

    private void BeginDrag()
    {
        if (targetCamera == null || Mouse.current == null)
        {
            return;
        }

        isDragging = true;
        dragStartWorldPosition = ScreenToWorld(Mouse.current.position.ReadValue());
        cameraStartPosition = targetCamera.transform.position;
    }

    private void UpdateDrag()
    {
        if (targetCamera == null || Mouse.current == null)
        {
            return;
        }

        Vector3 currentWorld = ScreenToWorld(Mouse.current.position.ReadValue());
        Vector3 delta = dragStartWorldPosition - currentWorld;

        Vector3 desiredPosition = cameraStartPosition + delta;
        targetCamera.transform.position = new(desiredPosition.x, desiredPosition.y, targetCamera.transform.position.z);
    }

    private void EndDrag()
    {
        isDragging = false;
    }

    private void ApplyPanBoundsWhenReleased()
    {
        if (isDragging || targetCamera == null)
        {
            return;
        }

        if (!TryGetActiveCenter(out Transform activeCenter))
        {
            return;
        }

        PanBounds bounds = IsSubGridActive ? subGridPanBounds : mainGridPanBounds;
        Vector3 cameraPosition = targetCamera.transform.position;
        Vector3 clampedPosition = bounds.ClampPositionAroundCenter(cameraPosition, activeCenter.position);

        if (cameraPosition != clampedPosition)
        {
            Vector3 newPosition = Vector3.MoveTowards(cameraPosition, clampedPosition, panReturnSpeed * Time.deltaTime);
            targetCamera.transform.position = newPosition;
        }
    }

    private bool TryGetActiveCenter(out Transform activeCenter)
    {
        activeCenter = IsSubGridActive ? subGridCenter : mainGridCenter;
        return activeCenter != null;
    }

    private Vector3 ScreenToWorld(Vector2 screenPosition)
    {
        if (targetCamera == null)
        {
            return Vector3.zero;
        }

        Vector3 position = new(screenPosition.x, screenPosition.y, Mathf.Abs(targetCamera.transform.position.z));
        return targetCamera.ScreenToWorldPoint(position);
    }

    [System.Serializable]
    private struct PanBounds
    {
        public float MinX;
        public float MaxX;
        public float MinY;
        public float MaxY;

        public PanBounds(float minX, float maxX, float minY, float maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public Vector3 ClampPositionAroundCenter(Vector3 position, Vector3 center)
        {
            float clampedX = Mathf.Clamp(position.x, center.x + MinX, center.x + MaxX);
            float clampedY = Mathf.Clamp(position.y, center.y + MinY, center.y + MaxY);
            return new(clampedX, clampedY, position.z);
        }
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
