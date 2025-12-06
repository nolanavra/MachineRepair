using System.Collections.Generic;
using MachineRepair.Grid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Very simple UI for SimpleInventory.
/// Place this on a GameObject with a GridLayoutGroup, and assign:
/// - inventory: reference to SimpleInventory
/// - slotPrefab: a prefab containing an Image for icon and Text for count
/// </summary>
public class SimpleInventoryUI : MonoBehaviour
{
    [Header("Inventory Source")]
    public Inventory inventory;
    public GameObject inventoryPanel;
    [SerializeField] private InputRouter inputRouter;

    [Header("Input")]
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private string gameplayMapName = "Gameplay";
    [SerializeField] private string toggleActionName = "ToggleInventory";

    [Header("UI Setup")]
    public GameObject slotPrefab;           // prefab with Icon + Count
    public GridLayoutGroup gridLayout;      // parent container

    [Header("Options")]
    public bool refreshOnStart = true;


    private readonly List<GameObject> slotInstances = new List<GameObject>();
    private int draggingSlotIndex = -1;
    private InputAction toggleAction;

    private void Reset()
    {
        gridLayout = GetComponent<GridLayoutGroup>();
    }

    private void Start()
    {
        if (inputRouter == null) inputRouter = FindFirstObjectByType<InputRouter>();
        if (playerInput == null) playerInput = FindFirstObjectByType<PlayerInput>();
        CacheInputActions();
        EnableInput();

        if (refreshOnStart)
            RefreshUI();
    }

    public void ShowHideInventory()
    {
        if (inventoryPanel == null)
        {
            Debug.LogWarning("SimpleInventoryUI: No inventory panel assigned to toggle.");
            return;
        }

        if (inventoryPanel.activeSelf) inventoryPanel.SetActive(false);
        else inventoryPanel.SetActive(true);
    }

    /// <summary>
    /// Rebuilds the entire grid based on the inventory slots.
    /// Call this after you change inventory contents.
    /// </summary>
    public void RefreshUI()
    {
        if (inventory == null)
        {
            Debug.LogWarning("SimpleInventoryUI: No inventory assigned.");
            return;
        }

        if (gridLayout == null)
        {
            Debug.LogWarning("SimpleInventoryUI: No GridLayoutGroup assigned.");
            return;
        }

        if (slotPrefab == null)
        {
            Debug.LogWarning("SimpleInventoryUI: No slotPrefab assigned.");
            return;
        }

        ClearOldSlots();

        var slots = inventory.GetSlots();
        for (int i = 0; i < slots.Count; i++)
        {
            var slotData = slots[i];

            GameObject slotGO = Instantiate(slotPrefab, gridLayout.transform);
            slotInstances.Add(slotGO);

            // Find icon & text components in prefab
            Image icon = null;
            Text countText = null;

            // You can also do named lookups if you prefer:
            // icon = slotGO.transform.Find("Icon").GetComponent<Image>();
            // countText = slotGO.transform.Find("Count").GetComponent<Text>();

            foreach (var img in slotGO.GetComponentsInChildren<Image>())
            {
                if (icon == null && img.gameObject.name.ToLower().Contains("icon"))
                    icon = img;
            }

            foreach (var txt in slotGO.GetComponentsInChildren<Text>())
            {
                if (countText == null && txt.gameObject.name.ToLower().Contains("count"))
                    countText = txt;
            }

            var slotComponent = slotGO.GetComponent<InventorySlotUI>();
            if (slotComponent == null) slotComponent = slotGO.AddComponent<InventorySlotUI>();

            var def = slotData.IsEmpty ? null : inventory.GetDef(slotData.id);
            slotComponent.Initialize(this, i, icon, countText, def?.icon, slotData.quantity);
        }
    }

    private void ClearOldSlots()
    {
        for (int i = 0; i < slotInstances.Count; i++)
        {
            if (slotInstances[i] != null)
                DestroyImmediate(slotInstances[i]);
        }
        slotInstances.Clear();
    }

    internal void HandleSlotClicked(int slotIndex)
    {
        if (inventory == null) return;
        var slots = inventory.GetSlots();
        if (slotIndex < 0 || slotIndex >= slots.Count) return;
        var slotData = slots[slotIndex];
        if (slotData.IsEmpty) return;

        if (!inventory.ConsumeFromSlot(slotIndex, out var itemId)) return;

        bool placementStarted = inputRouter != null && inputRouter.BeginComponentPlacement(itemId, removeFromInventory: false);
        if (!placementStarted)
        {
            inventory.AddItem(itemId, 1);
        }

        RefreshUI();
    }

    internal void BeginSlotDrag(int slotIndex)
    {
        draggingSlotIndex = slotIndex;
    }

    internal void HandleSlotDrop(int targetIndex)
    {
        if (inventory == null) return;
        if (draggingSlotIndex < 0 || draggingSlotIndex == targetIndex) return;

        if (inventory.SwapSlots(draggingSlotIndex, targetIndex))
            RefreshUI();

        draggingSlotIndex = -1;
    }

    internal void EndSlotDrag()
    {
        draggingSlotIndex = -1;
    }

    private void CacheInputActions()
    {
        if (playerInput == null || playerInput.actions == null) return;

        var map = playerInput.actions.FindActionMap(gameplayMapName, throwIfNotFound: false);
        if (map == null) return;

        toggleAction = map.FindAction(toggleActionName, throwIfNotFound: false);
    }

    private void EnableInput()
    {
        if (toggleAction != null)
        {
            toggleAction.performed += OnToggleInventoryPerformed;
            toggleAction.Enable();
        }
    }

    private void OnDisable()
    {
        DisableInput();
    }

    private void DisableInput()
    {
        if (toggleAction != null)
        {
            toggleAction.performed -= OnToggleInventoryPerformed;
            toggleAction.Disable();
        }
    }

    private void OnToggleInventoryPerformed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        ShowHideInventory();
    }
}

internal class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    private SimpleInventoryUI owner;
    private int slotIndex;
    private Image icon;
    private Text countText;
    private RectTransform rectTransform;
    private Vector3 startPosition;
    private CanvasGroup canvasGroup;

    public void Initialize(SimpleInventoryUI owner, int slotIndex, Image icon, Text countText, Sprite iconSprite, int quantity)
    {
        this.owner = owner;
        this.slotIndex = slotIndex;
        this.icon = icon;
        this.countText = countText;

        rectTransform = transform as RectTransform;
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        ApplyVisual(iconSprite, quantity);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        owner?.HandleSlotClicked(slotIndex);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        startPosition = rectTransform.position;
        canvasGroup.blocksRaycasts = false;
        owner?.BeginSlotDrag(slotIndex);
    }

    public void OnDrag(PointerEventData eventData)
    {
        rectTransform.position += (Vector3)eventData.delta;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        rectTransform.position = startPosition;
        canvasGroup.blocksRaycasts = true;
        owner?.EndSlotDrag();
    }

    public void OnDrop(PointerEventData eventData)
    {
        owner?.HandleSlotDrop(slotIndex);
    }

    private void ApplyVisual(Sprite iconSprite, int quantity)
    {
        if (icon != null)
        {
            icon.sprite = iconSprite;
            icon.color = (iconSprite != null) ? Color.white : new Color(1, 1, 1, 0);
        }

        if (countText != null)
        {
            countText.text = (quantity > 1) ? quantity.ToString() : string.Empty;
        }
    }
}
