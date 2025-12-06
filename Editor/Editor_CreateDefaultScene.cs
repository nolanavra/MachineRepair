using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem;
using MachineRepair;
using MachineRepair.Grid;
using MachineRepair.Input;

public static class Editor_CreateDefaultScene
{
    private const string ScenePath = "Assets/Scenes/MachineRepair.unity";

    [MenuItem("Tools/MachineRepair/Create Default Scene")]
    public static void CreateDefaultScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/MachineRepairControls.inputactions");
        if (actions == null)
        {
            Debug.LogError("MachineRepairControls.inputactions not found at Assets/Input. Aborting scene creation.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);

        var playerInput = CreatePlayerInputRoot(scene, actions);

        var camera = CreateCamera(scene);
        var gridManager = CreateGridRoot(scene);
        var gameModeManager = CreateGameModeManager(scene, playerInput);
        var inventory = CreateInventory(scene);
        var simulationManager = CreateSimulationRoot(scene, gridManager);
        var toolsRoot = CreateToolsRoot(scene, gridManager, inventory, camera, playerInput);

        var canvas = CreateCanvas(scene);
        CreateEventSystem(scene, actions);
        CreateInventoryUI(canvas, inventory, toolsRoot.inputRouter, playerInput);
        CreateInspectorUI(canvas, toolsRoot.inputRouter, gridManager, inventory, toolsRoot.wireTool);
        CreateSimulationUI(canvas, simulationManager, gridManager, gameModeManager);
        CreateDebugUI(canvas, camera, gridManager, toolsRoot.inputRouter, gameModeManager);

        FocusHierarchyAndScene();
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static PlayerInput CreatePlayerInputRoot(Scene scene, InputActionAsset actions)
    {
        var inputGO = new GameObject("PlayerInput");
        SceneManager.MoveGameObjectToScene(inputGO, scene);
        var playerInput = inputGO.AddComponent<PlayerInput>();
        playerInput.actions = actions;
        playerInput.defaultActionMap = "Gameplay";
        playerInput.notificationBehavior = PlayerNotifications.InvokeUnityEvents;

        var enabler = inputGO.AddComponent<InputActionMapEnabler>();
        var enablerSO = new SerializedObject(enabler);
        enablerSO.FindProperty("playerInput").objectReferenceValue = playerInput;
        enablerSO.ApplyModifiedPropertiesWithoutUndo();

        return playerInput;
    }

    private static Camera CreateCamera(Scene scene)
    {
        var cameraGO = new GameObject("Main Camera");
        SceneManager.MoveGameObjectToScene(cameraGO, scene);
        var camera = cameraGO.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 25f;
        camera.transform.position = new Vector3(32f, 24f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        camera.tag = "MainCamera";
        cameraGO.AddComponent<AudioListener>();
        return camera;
    }

    private static GridManager CreateGridRoot(Scene scene)
    {
        var gridRoot = new GameObject("Grid Root");
        SceneManager.MoveGameObjectToScene(gridRoot, scene);
        gridRoot.AddComponent<UnityEngine.Grid>();

        var tilemapGO = new GameObject("Tilemap");
        tilemapGO.transform.SetParent(gridRoot.transform, false);
        var tilemap = tilemapGO.AddComponent<Tilemap>();
        tilemapGO.AddComponent<TilemapRenderer>();

        var gridManager = gridRoot.AddComponent<GridManager>();
        var gridSO = new SerializedObject(gridManager);
        gridSO.FindProperty("tilemap").objectReferenceValue = tilemap;
        gridSO.ApplyModifiedPropertiesWithoutUndo();

        AddOptionalGridUI(gridRoot, gridManager, tilemap);
        return gridManager;
    }

    private static void AddOptionalGridUI(GameObject gridRoot, GridManager gridManager, Tilemap tilemap)
    {
        var gridUiType = Type.GetType("GridUI, Assembly-CSharp");
        if (gridUiType == null)
            return;

        var gridUi = gridRoot.AddComponent(gridUiType);
        var serialized = new SerializedObject(gridUi);
        var gridManagerProperty = serialized.FindProperty("gridManager");
        if (gridManagerProperty != null)
            gridManagerProperty.objectReferenceValue = gridManager;

        var tilemapProperty = serialized.FindProperty("tilemap");
        if (tilemapProperty != null)
            tilemapProperty.objectReferenceValue = tilemap;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameModeManager CreateGameModeManager(Scene scene, PlayerInput playerInput)
    {
        var go = new GameObject("GameModeManager");
        SceneManager.MoveGameObjectToScene(go, scene);
        var manager = go.AddComponent<GameModeManager>();
        var so = new SerializedObject(manager);
        so.FindProperty("playerInput").objectReferenceValue = playerInput;
        so.ApplyModifiedPropertiesWithoutUndo();
        return manager;
    }

    private static Inventory CreateInventory(Scene scene)
    {
        var go = new GameObject("Inventory");
        SceneManager.MoveGameObjectToScene(go, scene);
        return go.AddComponent<Inventory>();
    }

    private static (WirePlacementTool wireTool, InputRouter inputRouter) CreateToolsRoot(Scene scene, GridManager gridManager, Inventory inventory, Camera camera, PlayerInput playerInput)
    {
        var go = new GameObject("Tools");
        SceneManager.MoveGameObjectToScene(go, scene);

        var wireTool = go.AddComponent<WirePlacementTool>();
        var wireToolSO = new SerializedObject(wireTool);
        wireToolSO.FindProperty("grid").objectReferenceValue = gridManager;
        wireToolSO.FindProperty("cameraOverride").objectReferenceValue = camera;
        wireToolSO.FindProperty("playerInput").objectReferenceValue = playerInput;
        wireToolSO.ApplyModifiedPropertiesWithoutUndo();

        var inputRouter = go.AddComponent<InputRouter>();
        var inputRouterSO = new SerializedObject(inputRouter);
        inputRouterSO.FindProperty("grid").objectReferenceValue = gridManager;
        inputRouterSO.FindProperty("inventory").objectReferenceValue = inventory;
        inputRouterSO.FindProperty("wireTool").objectReferenceValue = wireTool;
        inputRouterSO.FindProperty("playerInput").objectReferenceValue = playerInput;
        inputRouterSO.ApplyModifiedPropertiesWithoutUndo();

        return (wireTool, inputRouter);
    }

    private static SimulationManager CreateSimulationRoot(Scene scene, GridManager gridManager)
    {
        var go = new GameObject("Simulation");
        SceneManager.MoveGameObjectToScene(go, scene);
        var simulationManager = go.AddComponent<SimulationManager>();
        var simSO = new SerializedObject(simulationManager);
        simSO.FindProperty("grid").objectReferenceValue = gridManager;
        simSO.ApplyModifiedPropertiesWithoutUndo();
        return simulationManager;
    }

    private static GameObject CreateCanvas(Scene scene)
    {
        var canvasGO = new GameObject("Canvas");
        SceneManager.MoveGameObjectToScene(canvasGO, scene);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();
        return canvasGO;
    }

    private static void CreateInventoryUI(GameObject canvas, Inventory inventory, InputRouter inputRouter, PlayerInput playerInput)
    {
        var inventoryUI = new GameObject("InventoryUI");
        inventoryUI.transform.SetParent(canvas.transform, false);
        var rect = inventoryUI.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0.3f, 0.4f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var gridLayout = inventoryUI.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(80f, 80f);

        var slotPrefab = new GameObject("SlotPrefab");
        slotPrefab.transform.SetParent(inventoryUI.transform, false);
        var image = slotPrefab.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        var textGO = new GameObject("Count");
        textGO.transform.SetParent(slotPrefab.transform, false);
        var text = textGO.AddComponent<Text>();
        text.text = "0";
        text.alignment = TextAnchor.MiddleCenter;
        slotPrefab.SetActive(false);

        var inventoryPanel = new GameObject("InventoryPanel");
        inventoryPanel.transform.SetParent(inventoryUI.transform, false);

        var inventoryScript = inventoryUI.AddComponent<SimpleInventoryUI>();
        inventoryScript.inventory = inventory;
        inventoryScript.inventoryPanel = inventoryPanel;

        var so = new SerializedObject(inventoryScript);
        so.FindProperty("inputRouter").objectReferenceValue = inputRouter;
        so.FindProperty("slotPrefab").objectReferenceValue = slotPrefab;
        so.FindProperty("gridLayout").objectReferenceValue = gridLayout;
        so.FindProperty("playerInput").objectReferenceValue = playerInput;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateInspectorUI(GameObject canvas, InputRouter inputRouter, GridManager gridManager, Inventory inventory, WirePlacementTool wireTool)
    {
        var inspector = new GameObject("InspectorUI");
        inspector.transform.SetParent(canvas.transform, false);
        var rect = inspector.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.7f, 0f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var inspectorScript = inspector.AddComponent<InspectorUI>();
        var title = CreateText(inspector.transform, "Title", FontStyle.Bold, 16);
        var description = CreateText(inspector.transform, "Description", FontStyle.Normal, 14);
        var connections = CreateText(inspector.transform, "Connections", FontStyle.Normal, 14);
        var parameters = CreateText(inspector.transform, "Parameters", FontStyle.Normal, 14);

        var so = new SerializedObject(inspectorScript);
        so.FindProperty("inputRouter").objectReferenceValue = inputRouter;
        so.FindProperty("grid").objectReferenceValue = gridManager;
        so.FindProperty("inventory").objectReferenceValue = inventory;
        so.FindProperty("wireTool").objectReferenceValue = wireTool;
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("descriptionText").objectReferenceValue = description;
        so.FindProperty("connectionsText").objectReferenceValue = connections;
        so.FindProperty("parametersText").objectReferenceValue = parameters;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateSimulationUI(GameObject canvas, SimulationManager simulationManager, GridManager gridManager, GameModeManager gameModeManager)
    {
        var simulation = new GameObject("SimulationUI");
        simulation.transform.SetParent(canvas.transform, false);
        var rect = simulation.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.3f, 0.8f);
        rect.anchorMax = new Vector2(0.7f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var vertical = simulation.AddComponent<VerticalLayoutGroup>();
        vertical.childControlHeight = true;
        vertical.childControlWidth = true;

        var startPowerButton = CreateButton(simulation.transform, "TogglePower", "Toggle Power");
        var startWaterButton = CreateButton(simulation.transform, "ToggleWater", "Toggle Water");

        var powerLabelGO = new GameObject("PowerLabel");
        powerLabelGO.transform.SetParent(simulation.transform, false);
        var powerLabel = powerLabelGO.AddComponent<TextMeshProUGUI>();
        powerLabel.text = "Power Off";

        var waterLabelGO = new GameObject("WaterLabel");
        waterLabelGO.transform.SetParent(simulation.transform, false);
        var waterLabel = waterLabelGO.AddComponent<TextMeshProUGUI>();
        waterLabel.text = "Water Off";

        var pipeArrowPrefabGO = new GameObject("PipeArrowPrefab");
        pipeArrowPrefabGO.transform.SetParent(simulation.transform, false);
        pipeArrowPrefabGO.SetActive(false);
        var pipeArrowPrefab = pipeArrowPrefabGO.AddComponent<SpriteRenderer>();

        var leakPrefabGO = new GameObject("LeakPrefab");
        leakPrefabGO.transform.SetParent(simulation.transform, false);
        leakPrefabGO.SetActive(false);
        var leakPrefab = leakPrefabGO.AddComponent<SpriteRenderer>();

        var simulationUI = simulation.AddComponent<SimulationUI>();
        var so = new SerializedObject(simulationUI);
        so.FindProperty("simulationManager").objectReferenceValue = simulationManager;
        so.FindProperty("gridManager").objectReferenceValue = gridManager;
        so.FindProperty("gameModeManager").objectReferenceValue = gameModeManager;
        so.FindProperty("startPowerButton").objectReferenceValue = startPowerButton;
        so.FindProperty("startWaterButton").objectReferenceValue = startWaterButton;
        so.FindProperty("powerLabel").objectReferenceValue = powerLabel;
        so.FindProperty("waterLabel").objectReferenceValue = waterLabel;
        so.FindProperty("pipeArrowPrefab").objectReferenceValue = pipeArrowPrefab;
        so.FindProperty("leakSpritePrefab").objectReferenceValue = leakPrefab;
        so.FindProperty("pipeArrowParent").objectReferenceValue = pipeArrowPrefabGO.transform;
        so.FindProperty("leakParent").objectReferenceValue = leakPrefabGO.transform;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateDebugUI(GameObject canvas, Camera camera, GridManager gridManager, InputRouter inputRouter, GameModeManager gameModeManager)
    {
        var debug = new GameObject("DebugUI");
        debug.transform.SetParent(canvas.transform, false);
        var rect = debug.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.8f);
        rect.anchorMax = new Vector2(0.3f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var cellText = debug.AddComponent<TextMeshProUGUI>();
        cellText.text = "Cell";
        var cellOccupancy = new GameObject("CellOccupancy").AddComponent<TextMeshProUGUI>();
        cellOccupancy.transform.SetParent(debug.transform, false);
        var modeText = new GameObject("Mode").AddComponent<TextMeshProUGUI>();
        modeText.transform.SetParent(debug.transform, false);

        var debugUI = debug.AddComponent<DebugUI>();
        var so = new SerializedObject(debugUI);
        so.FindProperty("cam").objectReferenceValue = camera;
        so.FindProperty("grid").objectReferenceValue = gridManager;
        so.FindProperty("router").objectReferenceValue = inputRouter;
        so.FindProperty("gameModeManager").objectReferenceValue = gameModeManager;
        so.FindProperty("cellText").objectReferenceValue = cellText;
        so.FindProperty("cellOccupancy").objectReferenceValue = cellOccupancy;
        so.FindProperty("gameMode").objectReferenceValue = modeText;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Text CreateText(Transform parent, string name, FontStyle style, int size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.fontStyle = style;
        text.fontSize = size;
        text.text = name;
        text.alignment = TextAnchor.UpperLeft;
        return text;
    }

    private static Button CreateButton(Transform parent, string name, string label)
    {
        var buttonGO = new GameObject(name);
        buttonGO.transform.SetParent(parent, false);
        var image = buttonGO.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        var button = buttonGO.AddComponent<Button>();

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Midline;

        return button;
    }

    private static GameObject CreateEventSystem(Scene scene, InputActionAsset actions)
    {
        var eventSystemGO = new GameObject("EventSystem");
        SceneManager.MoveGameObjectToScene(eventSystemGO, scene);
        eventSystemGO.AddComponent<EventSystem>();
        var uiModule = eventSystemGO.AddComponent<InputSystemUIInputModule>();
        uiModule.actionsAsset = actions;
        AssignIfFound(a => uiModule.point = a, actions, "UI/Point");
        AssignIfFound(a => uiModule.leftClick = a, actions, "UI/Click");
        AssignIfFound(a => uiModule.rightClick = a, actions, "UI/RightClick");
        AssignIfFound(a => uiModule.middleClick = a, actions, "UI/MiddleClick");
        AssignIfFound(a => uiModule.scrollWheel = a, actions, "UI/ScrollWheel");
        AssignIfFound(a => uiModule.move = a, actions, "UI/Navigate");
        AssignIfFound(a => uiModule.submit = a, actions, "UI/Submit");
        AssignIfFound(a => uiModule.cancel = a, actions, "UI/Cancel");
        AssignIfFound(a => uiModule.trackedDeviceOrientation = a, actions, "UI/TrackedDeviceOrientation");
        AssignIfFound(a => uiModule.trackedDevicePosition = a, actions, "UI/TrackedDevicePosition");
        return eventSystemGO;
    }

    private static void AssignIfFound(Action<InputActionReference> setter, InputActionAsset asset, string actionPath)
    {
        var action = asset.FindAction(actionPath, throwIfNotFound: false);
        if (action == null) return;
        setter(InputActionReference.Create(action));
    }

    private static void FocusHierarchyAndScene()
    {
        EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
        EditorApplication.ExecuteMenuItem("Window/General/Scene");
    }
}
