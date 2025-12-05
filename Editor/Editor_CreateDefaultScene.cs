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
using MachineRepair;
using MachineRepair.Grid;

public static class Editor_CreateDefaultScene
{
    private const string ScenePath = "Assets/Scenes/MachineRepair.unity";

    [MenuItem("Tools/MachineRepair/Create Default Scene")]
    public static void CreateDefaultScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);

        var camera = CreateCamera(scene);
        var gridManager = CreateGridRoot(scene);
        var gameModeManager = CreateGameModeManager(scene);
        var inventory = CreateInventory(scene);
        var simulationManager = CreateSimulationRoot(scene, gridManager);
        var toolsRoot = CreateToolsRoot(scene, gridManager, inventory, camera);

        var canvas = CreateCanvas(scene);
        CreateEventSystem(scene);
        CreateInventoryUI(canvas, inventory, toolsRoot.inputRouter);
        CreateInspectorUI(canvas, toolsRoot.inputRouter, gridManager, inventory, toolsRoot.wireTool);
        CreateSimulationUI(canvas, simulationManager, gridManager, gameModeManager);
        CreateDebugUI(canvas, camera, gridManager, toolsRoot.inputRouter, gameModeManager);

        FocusHierarchyAndScene();
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.MarkSceneDirty(scene);
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
        serialized.FindProperty("gridManager")?.objectReferenceValue = gridManager;
        serialized.FindProperty("tilemap")?.objectReferenceValue = tilemap;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameModeManager CreateGameModeManager(Scene scene)
    {
        var go = new GameObject("GameModeManager");
        SceneManager.MoveGameObjectToScene(go, scene);
        return go.AddComponent<GameModeManager>();
    }

    private static Inventory CreateInventory(Scene scene)
    {
        var go = new GameObject("Inventory");
        SceneManager.MoveGameObjectToScene(go, scene);
        return go.AddComponent<Inventory>();
    }

    private static (WirePlacementTool wireTool, InputRouter inputRouter) CreateToolsRoot(Scene scene, GridManager gridManager, Inventory inventory, Camera camera)
    {
        var go = new GameObject("Tools");
        SceneManager.MoveGameObjectToScene(go, scene);

        var wireTool = go.AddComponent<WirePlacementTool>();
        var wireToolSO = new SerializedObject(wireTool);
        wireToolSO.FindProperty("grid").objectReferenceValue = gridManager;
        wireToolSO.FindProperty("cameraOverride").objectReferenceValue = camera;
        wireToolSO.ApplyModifiedPropertiesWithoutUndo();

        var inputRouter = go.AddComponent<InputRouter>();
        var inputRouterSO = new SerializedObject(inputRouter);
        inputRouterSO.FindProperty("grid").objectReferenceValue = gridManager;
        inputRouterSO.FindProperty("inventory").objectReferenceValue = inventory;
        inputRouterSO.FindProperty("wireTool").objectReferenceValue = wireTool;
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

    private static void CreateInventoryUI(GameObject canvas, Inventory inventory, InputRouter inputRouter)
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

    private static GameObject CreateEventSystem(Scene scene)
    {
        var eventSystemGO = new GameObject("EventSystem");
        SceneManager.MoveGameObjectToScene(eventSystemGO, scene);
        eventSystemGO.AddComponent<EventSystem>();
        eventSystemGO.AddComponent<StandaloneInputModule>();
        return eventSystemGO;
    }

    private static void FocusHierarchyAndScene()
    {
        EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
        EditorApplication.ExecuteMenuItem("Window/General/Scene");
    }
}
