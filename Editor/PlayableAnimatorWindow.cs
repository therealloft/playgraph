using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace Playgraph
{
    public sealed class PlayableAnimatorWindow : EditorWindow
    {
        private static readonly string[] Tabs =
        {
        "Graph Asset",
        "Runtime Debugger"
    };
        private static readonly string[] LeftPanelTabs =
        {
        "States",
        "Params"
    };
        private static readonly string[] StateOutputLabels =
        {
        "Clip",
        "Playlist",
        "Blend Tree 1D",
        "Blend Tree 2D",
        "Direct Blend",
        "One Shot"
    };
        private static readonly PlayableStateOutput[] StateOutputValues =
        {
        PlayableStateOutput.Clip,
        PlayableStateOutput.Playlist,
        PlayableStateOutput.BlendTree1D,
        PlayableStateOutput.BlendTree2D,
        PlayableStateOutput.DirectBlend,
        PlayableStateOutput.OneShot
    };

        private static readonly Color GridMajor = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color GridMinor = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color Accent = new Color(1f, 0.48f, 0.04f, 1f);
        private static readonly Color StateBlue = new Color(0.22f, 0.36f, 0.55f, 1f);
        private static readonly Color MotionBlue = new Color(0.15f, 0.18f, 0.24f, 1f);
        private const float DefaultSidebarWidth = 330f;
        private const float MinSidebarWidth = 240f;
        private const float MaxSidebarWidth = 520f;
        private const float DefaultInspectorWidth = 620f;
        private const float MinInspectorWidth = 390f;
        private const float MaxInspectorWidth = 940f;
        private const float MinCanvasWidth = 360f;
        private const float SplitterWidth = 5f;
        private const float StateRowHeight = 30f;
        private const float BlendNodeRowHeight = 42f;
        private const float BlendNodeGap = 8f;
        private const float BlendNodeColumnWidth = 215f;

        private readonly List<PlayableAnimator> runtimeTargets =
            new List<PlayableAnimator>();
        private readonly List<PlayableParameterDebugInfo> parameterDebug =
            new List<PlayableParameterDebugInfo>();
        private readonly List<PlayableLayerDebugInfo> layerDebug =
            new List<PlayableLayerDebugInfo>();
        private readonly List<PlayableState> stateMachinePath =
            new List<PlayableState>();

        private PlayableAnimatorGraph graphAsset;
        private PlayableAnimator runtimeTarget;
        private PreviewRenderUtility previewUtility;
        private GameObject previewSource;
        private GameObject previewInstance;
        private GameObject previewGrid;
        private Mesh previewGridMesh;
        private Material previewGridMaterial;
        private PlayableGraph previewGraph;
        private AnimationMixerPlayable previewMixer;
        private Vector2 stateListScroll;
        private Vector2 parameterScroll;
        private Vector2 inspectorScroll;
        private Vector2 debuggerScroll;
        private Vector2 graphPan;
        private Vector2 previewOrbit = new Vector2(145f, 12f);
        private float sidebarWidth = DefaultSidebarWidth;
        private float inspectorWidth = DefaultInspectorWidth;
        private int tabIndex;
        private int leftPanelTab;
        private int selectedLayer;
        private int selectedState;
        private int selectedMotion = -1;
        private int previewSignature;
        private float graphZoom = 1f;
        private float previewTime;
        private float previewRootMotionTime;
        private float previewPoseDuration = 1f;
        private float previewSpeed = 1f;
        private float previewZoom = 1f;
        private Vector3 previewBasePosition;
        private Quaternion previewBaseRotation = Quaternion.identity;
        private Vector3 previewRootMotionOffset;
        private float previewBlendX;
        private float previewBlendY;
        private double lastPreviewUpdateTime;
        private PlayableState previewBlendState;
        private string previewBlendParameterX;
        private string previewBlendParameterY;
        private bool showStateSettings = true;
        private bool showOutput = true;
        private bool showConditions = true;
        private bool showInterruptions = true;
        private bool showBehaviours = true;
        private bool showEvents = true;
        private bool showLayerSettings = true;
        private bool showPreview = true;
        private bool previewUseRuntimeTarget = true;
        private bool previewPlaying;
        private bool suspendPreviewForPlayModeChange;

        [MenuItem("Play Graph/Playable Animator")]
        public static void Open()
        {
            GetWindow<PlayableAnimatorWindow>(
                "Playable Animator");
        }

        public static void Open(
            PlayableAnimatorGraph graph,
            PlayableAnimator target = null)
        {
            PlayableAnimatorWindow window =
                GetWindow<PlayableAnimatorWindow>(
                    "Playable Animator");
            window.graphAsset = graph;
            window.runtimeTarget = target;
            window.Focus();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload += DisposePreviewResources;
            EditorApplication.quitting += DisposePreviewResources;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            PickSelection();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= DisposePreviewResources;
            EditorApplication.quitting -= DisposePreviewResources;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
            DisposePreviewResources();
        }

        private void OnDestroy()
        {
            DisposePreviewResources();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.ExitingPlayMode)
            {
                suspendPreviewForPlayModeChange = true;
                DisposePreviewResources();
            }
            else if (state == PlayModeStateChange.EnteredEditMode ||
                     state == PlayModeStateChange.EnteredPlayMode)
            {
                suspendPreviewForPlayModeChange = false;
                Repaint();
            }
        }

        private void Update()
        {
            if (Application.isPlaying && tabIndex == 1)
                Repaint();

            TickPreviewAnimation();
        }

        private void OnEditorUpdate()
        {
            TickPreviewAnimation();
        }

        private void TickPreviewAnimation()
        {
            if (!previewPlaying || tabIndex != 0 || !showPreview)
            {
                lastPreviewUpdateTime = EditorApplication.timeSinceStartup;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double deltaTime = lastPreviewUpdateTime > 0f
                ? now - lastPreviewUpdateTime
                : 0f;
            lastPreviewUpdateTime = now;

            if (deltaTime <= 0f)
                return;

            float timeAdvance = (float)deltaTime * previewSpeed;
            previewTime += timeAdvance;
            previewRootMotionTime += timeAdvance;
            EditorApplication.QueuePlayerLoopUpdate();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            tabIndex = GUILayout.Toolbar(tabIndex, Tabs);

            if (tabIndex == 0)
                DrawGraphAssetTab();
            else
                DrawRuntimeDebuggerTab();
        }

        private void OnSelectionChanged()
        {
            PickSelection();
            Repaint();
        }

        private void PickSelection()
        {
            Object selected = Selection.activeObject;
            if (selected is PlayableAnimatorGraph selectedGraph)
            {
                graphAsset = selectedGraph;
                return;
            }

            GameObject selectedGameObject = selected as GameObject;
            if (selectedGameObject == null)
                return;

            PlayableAnimator selectedTarget =
                selectedGameObject.GetComponent<PlayableAnimator>();
            if (selectedTarget == null)
                return;

            runtimeTarget = selectedTarget;
            if (runtimeTarget.GraphAsset != null)
                graphAsset = runtimeTarget.GraphAsset;

            if (previewUseRuntimeTarget)
            {
                previewSource = runtimeTarget.gameObject;
                DisposePreviewResources();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            graphAsset = (PlayableAnimatorGraph)EditorGUILayout.ObjectField(
                graphAsset,
                typeof(PlayableAnimatorGraph),
                false,
                GUILayout.MinWidth(180f));

            EditorGUI.BeginChangeCheck();
            PlayableAnimator nextRuntimeTarget =
                (PlayableAnimator)EditorGUILayout.ObjectField(
                runtimeTarget,
                typeof(PlayableAnimator),
                true,
                GUILayout.MinWidth(180f));
            if (EditorGUI.EndChangeCheck())
            {
                runtimeTarget = nextRuntimeTarget;
                if (runtimeTarget != null &&
                    runtimeTarget.GraphAsset != null)
                {
                    graphAsset = runtimeTarget.GraphAsset;
                }

                if (previewUseRuntimeTarget)
                {
                    previewSource = runtimeTarget != null
                        ? runtimeTarget.gameObject
                        : null;
                    DisposePreviewResources();
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("New Graph", EditorStyles.toolbarButton))
                CreateGraphAsset();

            GUI.enabled = graphAsset != null;
            if (GUILayout.Button("Ping", EditorStyles.toolbarButton))
                EditorGUIUtility.PingObject(graphAsset);
            GUI.enabled = true;

            GUI.enabled = runtimeTarget != null && Application.isPlaying;
            if (GUILayout.Button("Rebuild", EditorStyles.toolbarButton))
                runtimeTarget.RebuildGraph();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawGraphAssetTab()
        {
            if (graphAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "Create or assign a Playable Animator Graph.",
                    MessageType.Info);
                if (GUILayout.Button("Create Graph Asset", GUILayout.Height(28f)))
                    CreateGraphAsset();
                return;
            }

            graphAsset.EnsureDefaults();
            ClampSelection();
            ClampPanelWidths();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawStateSidebar();
            DrawResizeHandle(
                ref sidebarWidth,
                MinSidebarWidth,
                MaxSidebarWidth,
                true);
            DrawDecisionCanvas();
            DrawResizeHandle(
                ref inspectorWidth,
                MinInspectorWidth,
                MaxInspectorWidth,
                false);
            DrawInspectorPanel();
            EditorGUILayout.EndHorizontal();
            DrawLayerTabs();
            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                graphAsset.defaultFadeDuration =
                    Mathf.Max(0f, graphAsset.defaultFadeDuration);
                EditorUtility.SetDirty(graphAsset);
            }
        }

        private void DrawStateSidebar()
        {
            PlayableLayer layer = graphAsset.layers[selectedLayer];

            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Width(sidebarWidth),
                GUILayout.ExpandHeight(true));

            leftPanelTab = GUILayout.Toolbar(leftPanelTab, LeftPanelTabs);

            if (leftPanelTab == 0)
                DrawStatesPanel(layer);
            else
                DrawParametersPanel();

            EditorGUILayout.EndVertical();
        }

        private void DrawStatesPanel(PlayableLayer layer)
        {
            List<PlayableState> states = GetCurrentStateList(layer);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("States", EditorStyles.boldLabel);
            DrawStateBreadcrumbs(layer);

            stateListScroll = EditorGUILayout.BeginScrollView(
                stateListScroll,
                GUILayout.ExpandHeight(true));

            for (int i = 0; i < states.Count; i++)
            {
                PlayableState state = states[i];
                string label = GetStateRowLabel(state, i);
                bool selected = selectedState == i;

                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = selected
                    ? new Color(0.36f, 0.52f, 0.75f, 1f)
                    : previousBackground;

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Toggle(
                        selected,
                        label,
                        "Button",
                        GUILayout.Height(30f)))
                {
                    if (selectedState != i)
                    {
                        selectedState = i;
                        selectedMotion = -1;
                    }
                }

                if (state != null && state.IsSubStateMachine &&
                    GUILayout.Button(">", GUILayout.Width(30f), GUILayout.Height(30f)))
                {
                    OpenSubStateMachine(state);
                }

                EditorGUILayout.EndHorizontal();

                GUI.backgroundColor = previousBackground;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add State", GUILayout.Height(26f)))
                AddState(states);
            if (GUILayout.Button("Add Sub-State", GUILayout.Height(26f)))
                AddSubStateMachine(states);
            EditorGUILayout.EndHorizontal();

            GUI.enabled = states.Count > 1;
            if (GUILayout.Button("Remove Selected", GUILayout.Height(22f)))
                RemoveSelectedState(states);
            GUI.enabled = true;
        }

        private void DrawParametersPanel()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);

            parameterScroll = EditorGUILayout.BeginScrollView(
                parameterScroll,
                GUILayout.ExpandHeight(true));
            DrawParameters(false);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("Add Parameter", GUILayout.Height(26f)))
                AddParameter();
        }

        private void DrawDecisionCanvas()
        {
            PlayableLayer layer = graphAsset.layers[selectedLayer];
            PlayableState state = GetSelectedState(layer);
            if (state == null)
                return;

            Rect canvasRect = GUILayoutUtility.GetRect(
                320f,
                320f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));

            HandleDecisionCanvasInput(canvasRect);

            GUI.BeginGroup(canvasRect);
            Rect localCanvas = new Rect(0f, 0f, canvasRect.width, canvasRect.height);

            EditorGUI.DrawRect(localCanvas, new Color(0.12f, 0.12f, 0.12f, 1f));
            DrawGrid(localCanvas, 20f * graphZoom, GridMinor, graphPan);
            DrawGrid(localCanvas, 100f * graphZoom, GridMajor, graphPan);

            Rect titleRect = new Rect(
                localCanvas.x + 12f,
                localCanvas.y + 8f,
                localCanvas.width - 24f,
                20f);
            GUI.Label(
                titleRect,
                GetSelectedStatePath(layer),
                EditorStyles.miniLabel);

            GUI.Label(
                new Rect(localCanvas.xMax - 82f, localCanvas.y + 8f, 70f, 20f),
                $"{Mathf.RoundToInt(graphZoom * 100f)}%",
                EditorStyles.miniLabel);

            bool isBlendTree = state.output == PlayableStateOutput.Playlist ||
                               state.output == PlayableStateOutput.BlendTree1D ||
                               state.output == PlayableStateOutput.BlendTree2D ||
                               state.output == PlayableStateOutput.DirectBlend;

            if (state.IsSubStateMachine)
                DrawSubStateMachineCanvas(localCanvas, state);
            else if (isBlendTree)
                DrawBlendTreeCanvas(localCanvas, state);
            else
                DrawClipCanvas(localCanvas, state);

            GUI.EndGroup();
        }

        private void DrawClipCanvas(Rect canvasRect, PlayableState state)
        {
            Rect stateNode = WorldToCanvasRect(
                canvasRect,
                CenteredRect(Vector2.zero, 190f, 82f));
            DrawCanvasNode(
                stateNode,
                state.DisplayName,
                GetClipName(state.clip),
                StateBlue,
                true);
        }

        private void DrawSubStateMachineCanvas(
            Rect canvasRect,
            PlayableState state)
        {
            Rect machineNode = WorldToCanvasRect(
                canvasRect,
                CenteredRect(new Vector2(-120f, 0f), 210f, 92f));
            DrawCanvasNode(
                machineNode,
                state.DisplayName,
                "Sub-State Machine",
                StateBlue,
                true);

            if (state.subStates == null || state.subStates.Count == 0)
                return;

            float totalHeight = state.subStates.Count * BlendNodeRowHeight +
                                Mathf.Max(0, state.subStates.Count - 1) *
                                BlendNodeGap;
            for (int i = 0; i < state.subStates.Count; i++)
            {
                PlayableState child = state.subStates[i];
                float y = -totalHeight * 0.5f +
                          i * (BlendNodeRowHeight + BlendNodeGap);
                Rect childNode = WorldToCanvasRect(
                    canvasRect,
                    new Rect(210f, y, 210f, BlendNodeRowHeight));
                DrawConnection(machineNode, childNode);
                DrawCanvasNode(
                    childNode,
                    child != null ? child.DisplayName : "(none)",
                    child != null && child.IsSubStateMachine
                        ? "Sub-State Machine"
                        : child != null
                            ? GetStateOutputLabel(child.output)
                            : string.Empty,
                    MotionBlue,
                    child != null && child.isDefault);
            }
        }

        private void DrawBlendTreeCanvas(Rect canvasRect, PlayableState state)
        {
            Rect stateNode = WorldToCanvasRect(
                canvasRect,
                CenteredRect(new Vector2(-120f, 0f), 210f, 92f));
            DrawCanvasNode(
                stateNode,
                state.DisplayName,
                GetBlendParameterLabel(state),
                StateBlue,
                true);

            if (state.motions == null || state.motions.Count == 0)
            {
                Rect emptyNode = WorldToCanvasRect(
                    canvasRect,
                    CenteredRect(new Vector2(220f, 0f), 180f, 56f));
                DrawConnection(stateNode, emptyNode);
                DrawCanvasNode(
                    emptyNode,
                    state.output == PlayableStateOutput.Playlist
                        ? "No Clips"
                        : "No Motions",
                    "Add clips in the inspector",
                    MotionBlue,
                    false);
                return;
            }

            float visibleWorldHeight = canvasRect.height / Mathf.Max(0.01f, graphZoom);
            float maxColumnHeight = Mathf.Max(
                BlendNodeRowHeight,
                visibleWorldHeight - 120f);
            int maxRowsPerColumn = Mathf.Max(
                1,
                Mathf.FloorToInt(
                    (maxColumnHeight + BlendNodeGap) /
                    (BlendNodeRowHeight + BlendNodeGap)));
            int columnCount = Mathf.Max(
                1,
                Mathf.CeilToInt(state.motions.Count / (float)maxRowsPerColumn));
            int rowsPerColumn = Mathf.Max(
                1,
                Mathf.CeilToInt(state.motions.Count / (float)columnCount));

            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                int column = i / rowsPerColumn;
                int row = i % rowsPerColumn;
                int rowsInColumn = Mathf.Min(
                    rowsPerColumn,
                    state.motions.Count - column * rowsPerColumn);
                float columnHeight = rowsInColumn * BlendNodeRowHeight +
                                     Mathf.Max(0, rowsInColumn - 1) * BlendNodeGap;
                float y = -columnHeight * 0.5f +
                          row * (BlendNodeRowHeight + BlendNodeGap);

                Rect motionNode = WorldToCanvasRect(
                    canvasRect,
                    new Rect(
                        210f + column * BlendNodeColumnWidth,
                        y,
                        200f,
                        BlendNodeRowHeight));

                DrawConnection(stateNode, motionNode);
                DrawCanvasNode(
                    motionNode,
                    motion != null ? motion.DisplayName : "(none)",
                    motion != null ? GetMotionSubtitle(state, motion) : string.Empty,
                    MotionBlue,
                    false);
            }
        }

        private void DrawInspectorPanel()
        {
            PlayableLayer layer = graphAsset.layers[selectedLayer];
            PlayableState state = GetSelectedState(layer);
            if (state == null)
                return;

            EditorGUILayout.BeginVertical(
                GUILayout.Width(inspectorWidth),
                GUILayout.ExpandHeight(true));
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);

            EditorGUILayout.LabelField(state.DisplayName, EditorStyles.boldLabel);

            showStateSettings = DrawSectionHeader(showStateSettings, "State Settings");
            if (showStateSettings)
                DrawStateSettings(layer, state);

            if (!state.IsSubStateMachine)
            {
                showOutput = DrawSectionHeader(showOutput, "Output");
                if (showOutput)
                    DrawOutputFields(state);
            }

            showConditions = DrawSectionHeader(showConditions, "Conditions");
            if (showConditions)
                DrawConditions(state);

            if (!state.IsSubStateMachine)
            {
                showInterruptions = DrawSectionHeader(
                    showInterruptions,
                    "Interruptions");
                if (showInterruptions)
                    DrawInterruptions(layer, state);

                showBehaviours = DrawSectionHeader(showBehaviours, "Behaviours");
                if (showBehaviours)
                    DrawBehaviours(state);

                showEvents = DrawSectionHeader(showEvents, "Events");
                if (showEvents)
                    DrawEvents(state);
            }

            showLayerSettings = DrawSectionHeader(showLayerSettings, "Layer Settings");
            if (showLayerSettings)
                DrawLayerSettings(layer);

            if (!state.IsSubStateMachine)
            {
                showPreview = DrawSectionHeader(showPreview, "Animation Preview");
                if (showPreview)
                    DrawAnimationPreview(state);
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Add Locomotion Template", GUILayout.Height(26f)))
                AddLocomotionTemplate();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawStateSettings(
            PlayableLayer layer,
            PlayableState state)
        {
            state.name = EditorGUILayout.TextField("State Name", state.name);
            state.enabled = EditorGUILayout.Toggle("Enabled", state.enabled);
            state.manualOnly =
                EditorGUILayout.Toggle("Manual Only", state.manualOnly);
            if (!state.IsSubStateMachine && stateMachinePath.Count > 0)
            {
                bool nextExitState = EditorGUILayout.Toggle(
                    "Exit State",
                    state.isExitState);
                if (nextExitState && !state.isExitState)
                    state.loop = false;
                state.isExitState = nextExitState;
            }
            state.priority = EditorGUILayout.IntField("Priority", state.priority);

            if (state.IsSubStateMachine)
            {
                EditorGUILayout.LabelField(
                    "Child States",
                    state.subStates != null ? state.subStates.Count.ToString() : "0");
                if (GUILayout.Button("Open Sub-State Machine"))
                    OpenSubStateMachine(state);

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !state.isDefault;
                if (GUILayout.Button("Make Default"))
                    MakeDefaultState(GetCurrentStateList(layer), state);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                return;
            }

            state.output = DrawStateOutputPopup(state.output);
            state.fadeDuration = Mathf.Max(
                0f,
                EditorGUILayout.FloatField("Blend Time", state.fadeDuration));
            state.hasExitTime =
                EditorGUILayout.Toggle("Has Exit Time", state.hasExitTime);
            using (new EditorGUI.DisabledScope(!state.hasExitTime))
            {
                state.exitTime = Mathf.Max(
                    0f,
                    EditorGUILayout.FloatField("Exit Time", state.exitTime));
            }

            state.applyRootMotion =
                EditorGUILayout.Toggle("Root Motion", state.applyRootMotion);
            using (new EditorGUI.DisabledScope(!state.applyRootMotion))
            {
                EditorGUI.indentLevel++;
                state.rootMotionPositionXZ =
                    EditorGUILayout.Toggle(
                        "Position XZ",
                        state.rootMotionPositionXZ);
                state.rootMotionPositionY =
                    EditorGUILayout.Toggle(
                        "Position Y",
                        state.rootMotionPositionY);
                state.rootMotionRotation =
                    EditorGUILayout.Toggle(
                        "Rotation",
                        state.rootMotionRotation);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !state.isDefault;
            if (GUILayout.Button("Make Default"))
                MakeDefaultState(GetCurrentStateList(layer), state);
            GUI.enabled = Application.isPlaying && runtimeTarget != null;
            if (GUILayout.Button("Play"))
                runtimeTarget.PlayState(GetSelectedStatePath(false), layer.name);
            if (GUILayout.Button("One Shot"))
                runtimeTarget.TriggerOneShot(GetSelectedStatePath(false), layer.name);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private PlayableStateOutput DrawStateOutputPopup(
            PlayableStateOutput output)
        {
            int selected = 0;
            for (int i = 0; i < StateOutputValues.Length; i++)
            {
                if (StateOutputValues[i] == output)
                {
                    selected = i;
                    break;
                }
            }

            selected = EditorGUILayout.Popup(
                "Output",
                selected,
                StateOutputLabels);
            return StateOutputValues[Mathf.Clamp(
                selected,
                0,
                StateOutputValues.Length - 1)];
        }

        private void DrawOutputFields(PlayableState state)
        {
            switch (state.output)
            {
                case PlayableStateOutput.Playlist:
                    state.speed = Mathf.Max(
                        0.01f,
                        EditorGUILayout.FloatField("Playback Speed", state.speed));
                    state.loop = EditorGUILayout.Toggle("Loop Playlist", state.loop);
                    EditorGUILayout.HelpBox(
                        "Clips play from top to bottom. Disabled and empty entries are skipped.",
                        MessageType.None);
                    DrawMotionTable(state);
                    break;

                case PlayableStateOutput.BlendTree1D:
                case PlayableStateOutput.BlendTree2D:
                case PlayableStateOutput.DirectBlend:
                    DrawBlendTreeOutputFields(state);
                    break;

                default:
                    state.clip = (AnimationClip)EditorGUILayout.ObjectField(
                        "Animation Clip",
                        state.clip,
                        typeof(AnimationClip),
                        false);
                    state.speed = Mathf.Max(
                        0.01f,
                        EditorGUILayout.FloatField("Speed", state.speed));
                    state.loop = EditorGUILayout.Toggle("Loop", state.loop);
                    state.applyFootIK =
                        EditorGUILayout.Toggle("Apply Foot IK", state.applyFootIK);
                    break;
            }
        }

        private void DrawBlendTreeOutputFields(PlayableState state)
        {
            switch (state.output)
            {
                case PlayableStateOutput.BlendTree1D:
                    state.blendParameterX =
                        ParameterPopup("Blend Parameter", state.blendParameterX);
                    DrawBlendSpace1D(state);
                    break;

                case PlayableStateOutput.BlendTree2D:
                    state.blendTree2DType =
                        (PlayableBlendTree2DType)EditorGUILayout.EnumPopup(
                            "Blend Type",
                            state.blendTree2DType);
                    EditorGUILayout.BeginHorizontal();
                    state.blendParameterX =
                        ParameterPopup("X", state.blendParameterX);
                    state.blendParameterY =
                        ParameterPopup("Y", state.blendParameterY);
                    EditorGUILayout.EndHorizontal();
                    DrawBlendSpace2D(state);
                    break;

                case PlayableStateOutput.DirectBlend:
                    EditorGUILayout.HelpBox(
                        "Direct blend uses each motion's weight parameter.",
                        MessageType.None);
                    break;
            }

            DrawMotionTable(state);
        }

        private void DrawAnimationPreview(PlayableState state)
        {
            if (previewUseRuntimeTarget && runtimeTarget != null)
                previewSource = runtimeTarget.gameObject;

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            GameObject nextPreviewSource = (GameObject)EditorGUILayout.ObjectField(
                "Model",
                previewSource,
                typeof(GameObject),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                previewSource = nextPreviewSource;
                previewUseRuntimeTarget =
                    previewSource == null ||
                    (runtimeTarget != null &&
                     previewSource == runtimeTarget.gameObject);
                DisposePreviewResources();
            }

            GUI.enabled = runtimeTarget != null;
            if (GUILayout.Button("Use Target", GUILayout.Width(82f)))
            {
                if (runtimeTarget != null && previewSource != runtimeTarget.gameObject)
                    DisposePreviewResources();

                previewSource = runtimeTarget.gameObject;
                previewUseRuntimeTarget = true;
                Repaint();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GameObject source = GetPreviewSource();
            if (source == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a model or select a PlayableAnimator target.",
                    MessageType.Info);
                return;
            }

            if (!StateHasPreviewClips(state))
            {
                EditorGUILayout.HelpBox(
                    "Assign at least one clip to preview this state.",
                    MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            DrawPreviewBlendControls(state);

            float duration = Mathf.Max(0.01f, GetPreviewDuration(state));
            previewPoseDuration = duration;
            if (previewTime > duration)
                previewTime = previewPlaying ? Mathf.Repeat(previewTime, duration) : duration;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(
                    previewPlaying ? "Pause" : "Play",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(54f)))
            {
                previewPlaying = !previewPlaying;
                lastPreviewUpdateTime = EditorApplication.timeSinceStartup;
            }

            if (GUILayout.Button(
                    "Reset",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(52f)))
            {
                previewTime = 0f;
                previewRootMotionTime = 0f;
            }

            GUILayout.Label(
                $"{previewTime:0.00}s / {duration:0.00}s",
                EditorStyles.miniLabel,
                GUILayout.Width(96f));
            GUILayout.Label("Speed", EditorStyles.miniLabel, GUILayout.Width(42f));
            previewSpeed = EditorGUILayout.Slider(
                previewSpeed,
                0.05f,
                2f);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            float nextPreviewTime = EditorGUILayout.Slider(
                previewTime,
                0f,
                duration);
            bool previewTimeChanged = EditorGUI.EndChangeCheck();
            if (previewTimeChanged)
            {
                previewTime = nextPreviewTime;
                previewRootMotionTime = nextPreviewTime;
            }

            if (EditorGUI.EndChangeCheck() || previewTimeChanged)
                Repaint();

            float previewHeight = Mathf.Clamp(
                position.height * 0.32f,
                220f,
                420f);
            Rect previewRect = GUILayoutUtility.GetRect(
                1f,
                previewHeight,
                GUILayout.ExpandWidth(true));

            DrawPreviewScene(previewRect, state, source);
        }

        private void DrawPreviewBlendControls(PlayableState state)
        {
            switch (state.output)
            {
                case PlayableStateOutput.BlendTree1D:
                    SyncPreviewBlendValues(state);
                    using (new EditorGUI.DisabledScope(
                               Application.isPlaying && runtimeTarget != null))
                    {
                        previewBlendX = EditorGUILayout.FloatField(
                            state.blendParameterX,
                            previewBlendX);
                    }

                    break;

                case PlayableStateOutput.BlendTree2D:
                    SyncPreviewBlendValues(state);
                    using (new EditorGUI.DisabledScope(
                               Application.isPlaying && runtimeTarget != null))
                    {
                        EditorGUILayout.BeginHorizontal();
                        previewBlendX = EditorGUILayout.FloatField(
                            state.blendParameterX,
                            previewBlendX);
                        previewBlendY = EditorGUILayout.FloatField(
                            state.blendParameterY,
                            previewBlendY);
                        EditorGUILayout.EndHorizontal();
                    }

                    break;
            }
        }

        private void SyncPreviewBlendValues(PlayableState state)
        {
            if (ReferenceEquals(previewBlendState, state) &&
                string.Equals(previewBlendParameterX, state.blendParameterX) &&
                string.Equals(previewBlendParameterY, state.blendParameterY))
            {
                return;
            }

            previewBlendState = state;
            previewBlendParameterX = state.blendParameterX;
            previewBlendParameterY = state.blendParameterY;
            previewBlendX = GetDefaultFloatParameter(state.blendParameterX);
            previewBlendY = GetDefaultFloatParameter(state.blendParameterY);
        }

        private void DrawPreviewScene(
            Rect previewRect,
            PlayableState state,
            GameObject source)
        {
            HandlePreviewInput(previewRect);
            EnsurePreviewResources(state, source);

            if (previewUtility == null || previewInstance == null)
            {
                EditorGUI.HelpBox(
                    previewRect,
                    "Could not create preview.",
                    MessageType.Info);
                return;
            }

            ApplyPreviewPose(state);

            if (Event.current.type != EventType.Repaint)
                return;

            Bounds bounds = CalculatePreviewBounds(previewInstance);
            if (bounds.size == Vector3.zero)
            {
                EditorGUI.HelpBox(
                    previewRect,
                    "Preview model has no renderers.",
                    MessageType.Info);
                return;
            }

            RenderPreview(previewRect, bounds);
        }

        private GameObject GetPreviewSource()
        {
            if (previewSource != null)
                return previewSource;

            if (runtimeTarget != null)
                return runtimeTarget.gameObject;

            GameObject selected = Selection.activeGameObject;
            if (selected != null &&
                selected.GetComponentInChildren<Animator>() != null)
            {
                return selected;
            }

            return null;
        }

        private void EnsurePreviewResources(
            PlayableState state,
            GameObject source)
        {
            if (suspendPreviewForPlayModeChange)
                return;

            GameObject sourceRoot = GetPreviewRoot(source);
            if (sourceRoot == null)
                return;

            int signature = GetPreviewSignature(state, sourceRoot);
            if (previewUtility != null && previewSignature == signature)
                return;

            DisposePreviewResources();
            previewSignature = signature;

            previewUtility = new PreviewRenderUtility();
            previewUtility.lights[0].intensity = 1.25f;
            previewUtility.lights[0].transform.rotation =
                Quaternion.Euler(35f, 35f, 0f);
            previewUtility.lights[1].intensity = 0.7f;
            previewUtility.lights[1].transform.rotation =
                Quaternion.Euler(340f, 218f, 177f);
            previewUtility.camera.nearClipPlane = 0.01f;
            previewUtility.camera.farClipPlane = 500f;
            previewUtility.camera.clearFlags = CameraClearFlags.Color;
            previewUtility.camera.backgroundColor =
                new Color(0.15f, 0.15f, 0.15f, 1f);

            previewInstance = CreatePreviewInstance(sourceRoot);
            previewBasePosition = previewInstance.transform.position;
            previewBaseRotation = previewInstance.transform.rotation;
            previewRootMotionOffset = Vector3.zero;
            previewUtility.AddSingleGO(previewInstance);
            EnsurePreviewGrid();

            Animator animator = previewInstance.GetComponentInChildren<Animator>();
            if (animator == null)
                return;

            previewGraph = PlayableGraph.Create("Playable Animator Preview");
            previewGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            int inputCount = GetPreviewInputCount(state);
            previewMixer = AnimationMixerPlayable.Create(
                previewGraph,
                inputCount);

            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                previewGraph,
                "Preview",
                animator);
            output.SetSourcePlayable(previewMixer);

            if (UsesMotionList(state))
            {
                EnsureMotionList(state);
                for (int i = 0; i < state.motions.Count; i++)
                {
                    PlayableMotion motion = state.motions[i];
                    if (!IsPreviewMotionValid(motion))
                        continue;

                    ConnectPreviewClip(
                        motion.clip,
                        i,
                        motion.speed,
                        motion.applyFootIK);
                }
            }
            else
            {
                ConnectPreviewClip(
                    state.clip,
                    0,
                    state.speed,
                    state.applyFootIK);
            }

            previewGraph.Play();
        }

        private void ConnectPreviewClip(
            AnimationClip clip,
            int inputIndex,
            float speed,
            bool applyFootIk)
        {
            if (clip == null || !previewGraph.IsValid())
                return;

            AnimationClipPlayable clipPlayable =
                AnimationClipPlayable.Create(previewGraph, clip);
            clipPlayable.SetSpeed(0f);
            clipPlayable.SetApplyFootIK(applyFootIk);

            previewGraph.Connect(
                clipPlayable,
                0,
                previewMixer,
                inputIndex);
            previewMixer.SetInputWeight(inputIndex, 0f);
        }

        private void ApplyPreviewPose(PlayableState state)
        {
            if (!previewGraph.IsValid() || !previewMixer.IsValid())
                return;

            previewRootMotionOffset = GetPreviewRootMotionOffset(state);
            if (previewInstance != null)
            {
                previewInstance.transform.SetPositionAndRotation(
                    previewBasePosition,
                    previewBaseRotation);
            }

            for (int i = 0; i < previewMixer.GetInputCount(); i++)
            {
                previewMixer.SetInputWeight(i, 0f);
                Playable input = previewMixer.GetInput(i);
                if (input.IsValid())
                    input.SetTime(0f);
            }

            switch (state.output)
            {
                case PlayableStateOutput.Playlist:
                    ApplyPreviewPlaylist(state);
                    break;
                case PlayableStateOutput.BlendTree1D:
                    ApplyPreviewBlendTree1D(state);
                    break;
                case PlayableStateOutput.BlendTree2D:
                    ApplyPreviewBlendTree2D(state);
                    break;
                case PlayableStateOutput.DirectBlend:
                    ApplyPreviewDirectBlend(state);
                    break;
                default:
                    SetPreviewInput(
                        0,
                        1f,
                        state.clip,
                        state.speed,
                        0f,
                        false);
                    break;
            }

            previewGraph.Evaluate(0.0001f);

            if (previewInstance != null && state.applyRootMotion)
                previewInstance.transform.position += previewRootMotionOffset;
        }

        private void ApplyPreviewPlaylist(PlayableState state)
        {
            if (!TryGetPreviewPlaylistPosition(
                    state,
                    previewTime,
                    out int motionIndex,
                    out float clipTime))
            {
                return;
            }

            Playable input = previewMixer.GetInput(motionIndex);
            if (!input.IsValid())
                return;

            input.SetTime(clipTime);
            previewMixer.SetInputWeight(motionIndex, 1f);
        }

        private void ApplyPreviewBlendTree1D(PlayableState state)
        {
            float[] weights = new float[state.motions.Count];
            if (!CalculatePreviewBlendTree1DWeights(state, weights))
            {
                ApplyFirstPreviewMotion(state);
                return;
            }

            for (int i = 0; i < weights.Length; i++)
                SetPreviewMotionInput(state, i, weights[i]);
        }

        private bool CalculatePreviewBlendTree1DWeights(
            PlayableState state,
            float[] weights)
        {
            if (state == null || state.motions == null || weights == null)
                return false;

            float value = GetPreviewBlendValue(state, state.blendParameterX, false);
            int lower = -1;
            int upper = -1;

            for (int i = 0; i < state.motions.Count && i < weights.Length; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (!IsPreviewMotionValid(motion))
                    continue;

                if (motion.threshold <= value &&
                    (lower < 0 ||
                     motion.threshold > state.motions[lower].threshold))
                {
                    lower = i;
                }

                if (motion.threshold >= value &&
                    (upper < 0 ||
                     motion.threshold < state.motions[upper].threshold))
                {
                    upper = i;
                }
            }

            if (lower < 0)
                lower = upper;
            if (upper < 0)
                upper = lower;

            if (lower < 0 || upper < 0)
                return false;

            if (lower == upper)
            {
                weights[lower] = 1f;
                return true;
            }

            float lowerThreshold = state.motions[lower].threshold;
            float upperThreshold = state.motions[upper].threshold;
            float t = Mathf.InverseLerp(lowerThreshold, upperThreshold, value);
            weights[lower] = 1f - t;
            weights[upper] = t;
            return true;
        }

        private void ApplyPreviewBlendTree2D(PlayableState state)
        {
            float[] weights = new float[state.motions.Count];

            if (!TryCalculatePreviewMotionWeights(state, weights))
            {
                ApplyFirstPreviewMotion(state);
                return;
            }

            for (int i = 0; i < weights.Length; i++)
                SetPreviewMotionInput(state, i, weights[i]);
        }

        private void ApplyPreviewDirectBlend(PlayableState state)
        {
            float[] weights = new float[state.motions.Count];

            if (!TryCalculatePreviewMotionWeights(state, weights))
            {
                ApplyFirstPreviewMotion(state);
                return;
            }

            for (int i = 0; i < weights.Length; i++)
                SetPreviewMotionInput(state, i, weights[i]);
        }

        private bool TryCalculatePreviewMotionWeights(
            PlayableState state,
            float[] weights)
        {
            if (state == null ||
                state.motions == null ||
                weights == null ||
                weights.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < weights.Length; i++)
                weights[i] = 0f;

            switch (state.output)
            {
                case PlayableStateOutput.BlendTree1D:
                    return CalculatePreviewBlendTree1DWeights(state, weights);

                case PlayableStateOutput.BlendTree2D:
                    Vector2 value = new Vector2(
                        GetPreviewBlendValue(state, state.blendParameterX, false),
                        GetPreviewBlendValue(state, state.blendParameterY, true));
                    return PlayableBlendMath.Calculate2DWeights(
                        state.motions,
                        value,
                        weights,
                        state.blendTree2DType);

                case PlayableStateOutput.DirectBlend:
                    float total = 0f;
                    int count = Mathf.Min(state.motions.Count, weights.Length);
                    for (int i = 0; i < count; i++)
                    {
                        PlayableMotion motion = state.motions[i];
                        if (!IsPreviewMotionValid(motion))
                            continue;

                        float weight = Mathf.Clamp01(
                            GetPreviewFloatParameter(motion.directParameter));
                        weights[i] = weight;
                        total += weight;
                    }

                    if (total <= 0.0001f)
                        return false;

                    float scale = total > 1f ? 1f / total : 1f;
                    for (int i = 0; i < count; i++)
                        weights[i] *= scale;
                    return true;

                default:
                    return false;
            }
        }

        private void ApplyFirstPreviewMotion(PlayableState state)
        {
            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (!IsPreviewMotionValid(motion))
                    continue;

                SetPreviewMotionInput(state, i, 1f);
                return;
            }
        }

        private void SetPreviewMotionInput(
            PlayableState state,
            int motionIndex,
            float weight)
        {
            if (motionIndex < 0 || motionIndex >= state.motions.Count)
                return;

            PlayableMotion motion = state.motions[motionIndex];
            SetPreviewInput(
                motionIndex,
                weight,
                motion.clip,
                motion.speed,
                motion.cycleOffset,
                true);
        }

        private void SetPreviewInput(
            int inputIndex,
            float weight,
            AnimationClip clip,
            float speed,
            float cycleOffset,
            bool synchronizePhase)
        {
            if (!previewMixer.IsValid() ||
                inputIndex < 0 ||
                inputIndex >= previewMixer.GetInputCount() ||
                clip == null)
            {
                return;
            }

            Playable input = previewMixer.GetInput(inputIndex);
            if (!input.IsValid())
                return;

            input.SetTime(
                GetPreviewClipTime(
                    clip,
                    speed,
                    cycleOffset,
                    synchronizePhase));
            previewMixer.SetInputWeight(inputIndex, Mathf.Clamp01(weight));
        }

        private float GetPreviewClipTime(
            AnimationClip clip,
            float speed,
            float cycleOffset,
            bool synchronizePhase)
        {
            float clipLength = Mathf.Max(0.01f, clip.length);
            float offset = Mathf.Repeat(cycleOffset, 1f);

            if (synchronizePhase)
            {
                float phase = Mathf.Repeat(
                    previewTime / Mathf.Max(0.01f, previewPoseDuration),
                    1f);
                return Mathf.Repeat((phase + offset) * clipLength, clipLength);
            }

            float scaledTime = previewTime * Mathf.Max(0.01f, speed);
            return Mathf.Repeat(scaledTime + offset * clipLength, clipLength);
        }

        private Vector3 GetPreviewRootMotionOffset(PlayableState state)
        {
            if (state == null || !state.applyRootMotion)
                return Vector3.zero;

            Vector3 offset = state.output == PlayableStateOutput.Playlist
                ? GetPreviewPlaylistRootMotionOffset(
                    state,
                    previewRootMotionTime)
                : GetPreviewRootMotionVelocity(state) * previewRootMotionTime;
            if (!state.rootMotionPositionXZ)
            {
                offset.x = 0f;
                offset.z = 0f;
            }

            if (!state.rootMotionPositionY)
                offset.y = 0f;

            return offset;
        }

        private Vector3 GetPreviewRootMotionVelocity(PlayableState state)
        {
            if (state == null)
                return Vector3.zero;

            if (!IsBlendOutput(state))
            {
                return state.clip != null
                    ? state.clip.averageSpeed * Mathf.Max(0.01f, state.speed)
                    : Vector3.zero;
            }

            if (state.motions == null || state.motions.Count == 0)
                return Vector3.zero;

            float[] weights = new float[state.motions.Count];
            if (!TryCalculatePreviewMotionWeights(state, weights))
                return Vector3.zero;

            Vector3 velocity = Vector3.zero;
            int count = Mathf.Min(state.motions.Count, weights.Length);
            for (int i = 0; i < count; i++)
            {
                float weight = Mathf.Clamp01(weights[i]);
                if (weight <= 0.0001f)
                    continue;

                PlayableMotion motion = state.motions[i];
                if (!IsPreviewMotionValid(motion))
                    continue;

                velocity += motion.clip.averageSpeed *
                            Mathf.Max(0.01f, motion.speed) *
                            weight;
            }

            return velocity;
        }

        private bool TryGetPreviewPlaylistPosition(
            PlayableState state,
            float elapsedTime,
            out int motionIndex,
            out float clipTime)
        {
            motionIndex = -1;
            clipTime = 0f;
            float duration = GetPreviewPlaylistDuration(state);
            if (duration <= 0.0001f)
                return false;

            float timeline = state.loop
                ? Mathf.Repeat(Mathf.Max(0f, elapsedTime), duration)
                : Mathf.Clamp(elapsedTime, 0f, duration);
            int lastValid = -1;
            float cursor = 0f;

            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (!IsPreviewMotionValid(motion))
                    continue;

                lastValid = i;
                float speed = GetPreviewPlaylistMotionSpeed(state, motion);
                float motionDuration = motion.clip.length / speed;
                bool selected = timeline < cursor + motionDuration;
                if (!state.loop && Mathf.Approximately(timeline, duration))
                    selected = false;

                if (selected)
                {
                    motionIndex = i;
                    clipTime = Mathf.Clamp(
                        (timeline - cursor) * speed,
                        0f,
                        motion.clip.length);
                    return true;
                }

                cursor += motionDuration;
            }

            if (lastValid < 0)
                return false;

            motionIndex = lastValid;
            clipTime = state.motions[lastValid].clip.length;
            return true;
        }

        private float GetPreviewPlaylistDuration(PlayableState state)
        {
            if (state == null || state.motions == null)
                return 0f;

            float duration = 0f;
            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (!IsPreviewMotionValid(motion))
                    continue;

                duration += motion.clip.length /
                            GetPreviewPlaylistMotionSpeed(state, motion);
            }

            return duration;
        }

        private static float GetPreviewPlaylistMotionSpeed(
            PlayableState state,
            PlayableMotion motion)
        {
            return Mathf.Max(0.01f, state.speed) *
                   Mathf.Max(0.01f, motion.speed);
        }

        private Vector3 GetPreviewPlaylistRootMotionOffset(
            PlayableState state,
            float elapsedTime)
        {
            float duration = GetPreviewPlaylistDuration(state);
            if (duration <= 0.0001f)
                return Vector3.zero;

            float positiveTime = Mathf.Max(0f, elapsedTime);
            int cycles = Mathf.FloorToInt(positiveTime / duration);
            float remaining = Mathf.Repeat(positiveTime, duration);
            Vector3 cycleOffset = Vector3.zero;
            Vector3 partialOffset = Vector3.zero;

            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (!IsPreviewMotionValid(motion))
                    continue;

                float speed = GetPreviewPlaylistMotionSpeed(state, motion);
                float motionDuration = motion.clip.length / speed;
                cycleOffset += motion.clip.averageSpeed * motion.clip.length;

                float playedTime = Mathf.Min(remaining, motionDuration);
                partialOffset += motion.clip.averageSpeed * playedTime * speed;
                remaining = Mathf.Max(0f, remaining - motionDuration);
            }

            return cycleOffset * cycles + partialOffset;
        }

        private void RenderPreview(Rect previewRect, Bounds bounds)
        {
            UpdatePreviewGrid(bounds);

            Camera camera = previewUtility.camera;
            float radius = Mathf.Max(0.4f, bounds.extents.magnitude);
            Quaternion rotation = Quaternion.Euler(
                previewOrbit.y,
                previewOrbit.x,
                0f);
            Vector3 forward = rotation * Vector3.forward;
            float zoomT = Mathf.InverseLerp(0f, 1.5f, previewZoom);
            float distance = radius * Mathf.Lerp(1.65f, 8.8f, zoomT);

            camera.transform.position = bounds.center - forward * distance;
            camera.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 3f);
            camera.farClipPlane = distance + radius * 5f;

            previewUtility.BeginPreview(previewRect, GUIStyle.none);
            camera.Render();
            Texture texture = previewUtility.EndPreview();
            GUI.DrawTexture(previewRect, texture, ScaleMode.StretchToFill, false);

            GUI.Box(previewRect, GUIContent.none);
        }

        private void EnsurePreviewGrid()
        {
            if (previewUtility == null || previewGrid != null)
                return;

            previewGrid = new GameObject("Thieves Preview Grid");
            previewGrid.hideFlags = HideFlags.HideAndDontSave;
            previewGridMesh = new Mesh
            {
                name = "Thieves Preview Grid Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };

            MeshFilter filter = previewGrid.AddComponent<MeshFilter>();
            filter.sharedMesh = previewGridMesh;

            MeshRenderer renderer = previewGrid.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                DestroyImmediate(previewGrid);
                DestroyImmediate(previewGridMesh);
                previewGrid = null;
                previewGridMesh = null;
                return;
            }

            previewGridMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            previewGridMaterial.SetColor("_Color", Color.white);
            previewGridMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            previewGridMaterial.SetInt(
                "_DstBlend",
                (int)BlendMode.OneMinusSrcAlpha);
            previewGridMaterial.SetInt("_Cull", (int)CullMode.Off);
            previewGridMaterial.SetInt("_ZWrite", 0);
            previewGridMaterial.renderQueue = (int)RenderQueue.Transparent;

            renderer.sharedMaterial = previewGridMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            previewUtility.AddSingleGO(previewGrid);
        }

        private void UpdatePreviewGrid(Bounds bounds)
        {
            EnsurePreviewGrid();
            if (previewGridMesh == null)
                return;

            Vector3 gridCenter = previewInstance != null
                ? previewBasePosition
                : bounds.center;
            float horizontalExtent = Mathf.Max(bounds.extents.x, bounds.extents.z);
            float travelExtent = Mathf.Max(
                Mathf.Abs(previewRootMotionOffset.x),
                Mathf.Abs(previewRootMotionOffset.z));
            float extent = Mathf.Max(
                3f,
                horizontalExtent * 4f,
                travelExtent + horizontalExtent * 3f);
            float step = Mathf.Max(0.25f, extent / 12f);
            int lineRadius = Mathf.CeilToInt(extent / step);
            float floorY = Mathf.Min(bounds.min.y, gridCenter.y) + 0.01f;
            Vector3 center = new Vector3(gridCenter.x, floorY, gridCenter.z);

            List<Vector3> vertices = new List<Vector3>();
            List<Color> colors = new List<Color>();
            List<int> indices = new List<int>();
            Color minor = new Color(1f, 1f, 1f, 0.14f);
            Color major = new Color(1f, 1f, 1f, 0.28f);
            Color trail = new Color(1f, 0.5f, 0f, 0.9f);

            for (int i = -lineRadius; i <= lineRadius; i++)
            {
                float offset = i * step;
                Color lineColor = i == 0 || i % 4 == 0 ? major : minor;

                AddPreviewGridLine(
                    vertices,
                    colors,
                    indices,
                    new Vector3(center.x + offset, floorY, center.z - extent),
                    new Vector3(center.x + offset, floorY, center.z + extent),
                    lineColor);
                AddPreviewGridLine(
                    vertices,
                    colors,
                    indices,
                    new Vector3(center.x - extent, floorY, center.z + offset),
                    new Vector3(center.x + extent, floorY, center.z + offset),
                    lineColor);
            }

            if (previewRootMotionOffset.sqrMagnitude > 0.0001f)
            {
                Vector3 rootMotionEnd = center + new Vector3(
                    previewRootMotionOffset.x,
                    0.03f,
                    previewRootMotionOffset.z);
                AddPreviewGridLine(
                    vertices,
                    colors,
                    indices,
                    center + Vector3.up * 0.03f,
                    rootMotionEnd,
                    trail);
            }

            previewGridMesh.Clear();
            previewGridMesh.SetVertices(vertices);
            previewGridMesh.SetColors(colors);
            previewGridMesh.SetIndices(
                indices.ToArray(),
                MeshTopology.Lines,
                0);
            previewGridMesh.bounds = new Bounds(
                new Vector3(center.x, floorY, center.z),
                new Vector3(extent * 2f, 0.1f, extent * 2f));
        }

        private static void AddPreviewGridLine(
            List<Vector3> vertices,
            List<Color> colors,
            List<int> indices,
            Vector3 from,
            Vector3 to,
            Color color)
        {
            int index = vertices.Count;
            vertices.Add(from);
            vertices.Add(to);
            colors.Add(color);
            colors.Add(color);
            indices.Add(index);
            indices.Add(index + 1);
        }

        private void HandlePreviewInput(Rect rect)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition))
                return;

            if (current.type == EventType.ScrollWheel)
            {
                previewZoom = Mathf.Clamp(
                    previewZoom + current.delta.y * 0.035f,
                    0f,
                    1.5f);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseDrag &&
                     current.button == 0)
            {
                previewOrbit.x += current.delta.x * 0.6f;
                previewOrbit.y = Mathf.Clamp(
                    previewOrbit.y - current.delta.y * 0.4f,
                    -25f,
                    70f);
                current.Use();
                Repaint();
            }
        }

        private void DrawBlendSpace1D(PlayableState state)
        {
            EnsureMotionList(state);
            ClampSelectedMotion(state);

            Rect rect = GUILayoutUtility.GetRect(
                1f,
                104f,
                GUILayout.ExpandWidth(true));
            Rect plot = new Rect(
                rect.x + 10f,
                rect.y + 12f,
                rect.width - 20f,
                58f);

            EditorGUI.DrawRect(plot, new Color(0.16f, 0.16f, 0.16f, 1f));
            GUI.Box(plot, GUIContent.none);

            GetThresholdBounds(state, out float min, out float max);

            Handles.BeginGUI();
            Handles.color = new Color(0.55f, 0.55f, 0.55f, 1f);
            Handles.DrawLine(
                new Vector3(plot.xMin + 12f, plot.center.y),
                new Vector3(plot.xMax - 12f, plot.center.y));
            Handles.color = Color.white;
            Handles.EndGUI();

            Event current = Event.current;
            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (motion == null)
                    continue;

                float x = ThresholdToPlotX(plot, min, max, motion.threshold);
                Rect pointRect = new Rect(x - 6f, plot.center.y - 6f, 12f, 12f);
                Color pointColor = selectedMotion == i
                    ? Accent
                    : new Color(0.45f, 0.68f, 1f, 1f);
                EditorGUI.DrawRect(pointRect, pointColor);

                if (current.type == EventType.MouseDown &&
                    current.button == 0 &&
                    pointRect.Contains(current.mousePosition))
                {
                    selectedMotion = i;
                    GUI.changed = true;
                    current.Use();
                }
            }

            if (selectedMotion >= 0 &&
                selectedMotion < state.motions.Count &&
                current.type == EventType.MouseDrag &&
                current.button == 0 &&
                plot.Contains(current.mousePosition))
            {
                Undo.RecordObject(graphAsset, "Move Blend Motion");
                state.motions[selectedMotion].threshold =
                    PlotXToThreshold(plot, min, max, current.mousePosition.x);
                EditorUtility.SetDirty(graphAsset);
                Repaint();
                current.Use();
            }

            if (Application.isPlaying && runtimeTarget != null)
            {
                float value = runtimeTarget.GetFloat(state.blendParameterX);
                float x = ThresholdToPlotX(plot, min, max, value);
                DrawRuntimeMarker(new Vector2(x, plot.center.y), 7f);
            }

            Rect labels = new Rect(plot.x, plot.yMax + 4f, plot.width, 18f);
            EditorGUI.LabelField(
                labels,
                $"{min:0.##}",
                $"{max:0.##}",
                EditorStyles.miniLabel);
        }

        private void DrawBlendSpace2D(PlayableState state)
        {
            EnsureMotionList(state);
            ClampSelectedMotion(state);

            float targetSize = Mathf.Clamp(
                Mathf.Min(inspectorWidth - 44f, position.height * 0.38f),
                240f,
                440f);
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                targetSize + 34f,
                GUILayout.ExpandWidth(true));
            float size = Mathf.Min(rect.width - 20f, targetSize);
            Rect plot = new Rect(
                rect.x + (rect.width - size) * 0.5f,
                rect.y + 12f,
                size,
                size);

            EditorGUI.DrawRect(plot, new Color(0.16f, 0.16f, 0.16f, 1f));
            DrawGrid(plot, size / 4f, GridMinor);
            GUI.Box(plot, GUIContent.none);

            GetBlendBounds2D(
                state,
                out float minX,
                out float maxX,
                out float minY,
                out float maxY);
            DrawBlendSpaceAxes(plot, minX, maxX, minY, maxY);

            Event current = Event.current;
            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (motion == null)
                    continue;

                Vector2 point = BlendToPlot2D(
                    plot,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    motion.position);
                Rect hitRect = new Rect(point.x - 8f, point.y - 8f, 16f, 16f);
                DrawBlendPoint(point, selectedMotion == i);

                if (current.type == EventType.MouseDown &&
                    current.button == 0 &&
                    hitRect.Contains(current.mousePosition))
                {
                    selectedMotion = i;
                    GUI.changed = true;
                    current.Use();
                }
            }

            if (selectedMotion >= 0 &&
                selectedMotion < state.motions.Count &&
                current.type == EventType.MouseDrag &&
                current.button == 0 &&
                plot.Contains(current.mousePosition))
            {
                Undo.RecordObject(graphAsset, "Move Blend Motion");
                state.motions[selectedMotion].position = PlotToBlend2D(
                    plot,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    current.mousePosition);
                EditorUtility.SetDirty(graphAsset);
                Repaint();
                current.Use();
            }

            if (Application.isPlaying && runtimeTarget != null)
            {
                Vector2 value = new Vector2(
                    runtimeTarget.GetFloat(state.blendParameterX),
                    runtimeTarget.GetFloat(state.blendParameterY));
                Vector2 point = BlendToPlot2D(
                    plot,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    value);
                DrawRuntimeMarker(point, 8f);
            }

            Rect labels = new Rect(plot.x, plot.yMax + 4f, plot.width, 18f);
            EditorGUI.LabelField(
                labels,
                $"X {minX:0.##} to {maxX:0.##}",
                $"Y {minY:0.##} to {maxY:0.##}",
                EditorStyles.miniLabel);
        }

        private void DrawMotionTable(PlayableState state)
        {
            if (state.motions == null)
                state.motions = new List<PlayableMotion>();

            ClampSelectedMotion(state);
            DrawMotionTableHeader(state);

            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (motion == null)
                {
                    motion = new PlayableMotion();
                    state.motions[i] = motion;
                }

                bool remove = false;
                EditorGUILayout.BeginHorizontal();
                bool selected = GUILayout.Toggle(
                    selectedMotion == i,
                    GUIContent.none,
                    "Button",
                    GUILayout.Width(18f));
                if (selected)
                    selectedMotion = i;

                motion.enabled = EditorGUILayout.Toggle(
                    motion.enabled,
                    GUILayout.Width(18f));
                AnimationClip previousClip = motion.clip;
                motion.clip = (AnimationClip)EditorGUILayout.ObjectField(
                    motion.clip,
                    typeof(AnimationClip),
                    false,
                    GUILayout.MinWidth(110f));
                if (motion.clip != previousClip &&
                    motion.clip != null &&
                    (string.IsNullOrWhiteSpace(motion.name) ||
                     motion.name.StartsWith("Motion")))
                {
                    motion.name = motion.clip.name;
                }

                switch (state.output)
                {
                    case PlayableStateOutput.BlendTree1D:
                        motion.threshold =
                            EditorGUILayout.FloatField(
                                motion.threshold,
                                GUILayout.Width(58f));
                        break;
                    case PlayableStateOutput.BlendTree2D:
                        motion.position.x =
                            EditorGUILayout.FloatField(
                                motion.position.x,
                                GUILayout.Width(58f));
                        motion.position.y =
                            EditorGUILayout.FloatField(
                                motion.position.y,
                                GUILayout.Width(58f));
                        break;
                    case PlayableStateOutput.DirectBlend:
                        motion.directParameter =
                            ParameterPopupInline(
                                motion.directParameter,
                                GUILayout.Width(104f));
                        break;
                }

                if (IsBlendOutput(state))
                {
                    motion.cycleOffset = Mathf.Clamp01(
                        EditorGUILayout.FloatField(
                            motion.cycleOffset,
                            GUILayout.Width(58f)));
                }

                motion.speed = Mathf.Max(
                    0.01f,
                    EditorGUILayout.FloatField(motion.speed, GUILayout.Width(48f)));
                motion.applyFootIK =
                    EditorGUILayout.Toggle(motion.applyFootIK, GUILayout.Width(24f));
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                    remove = true;

                EditorGUILayout.EndHorizontal();

                if (remove)
                {
                    state.motions.RemoveAt(i);
                    break;
                }
            }

            string addLabel = state.output == PlayableStateOutput.Playlist
                ? "Add Clip"
                : "Add Motion";
            if (GUILayout.Button(addLabel))
            {
                state.motions.Add(new PlayableMotion
                {
                    name = $"Motion {state.motions.Count + 1}",
                    threshold = state.motions.Count
                });
                selectedMotion = state.motions.Count - 1;
            }
        }

        private void DrawMotionTableHeader(PlayableState state)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40f);
            EditorGUILayout.LabelField(
                state.output == PlayableStateOutput.Playlist ? "Clip" : "Motion",
                EditorStyles.miniBoldLabel);

            switch (state.output)
            {
                case PlayableStateOutput.BlendTree1D:
                    EditorGUILayout.LabelField(
                        "Threshold",
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(58f));
                    break;
                case PlayableStateOutput.BlendTree2D:
                    EditorGUILayout.LabelField(
                        "Pos X",
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(58f));
                    EditorGUILayout.LabelField(
                        "Pos Y",
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(58f));
                    break;
                case PlayableStateOutput.DirectBlend:
                    EditorGUILayout.LabelField(
                        "Weight",
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(104f));
                    break;
            }

            if (IsBlendOutput(state))
            {
                EditorGUILayout.LabelField(
                    "Phase",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(58f));
            }

            EditorGUILayout.LabelField(
                "Speed",
                EditorStyles.miniBoldLabel,
                GUILayout.Width(48f));
            EditorGUILayout.LabelField(
                "IK",
                EditorStyles.miniBoldLabel,
                GUILayout.Width(24f));
            GUILayout.Space(24f);
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureMotionList(PlayableState state)
        {
            if (state.motions == null)
                state.motions = new List<PlayableMotion>();
        }

        private void ClampSelectedMotion(PlayableState state)
        {
            if (state.motions == null || state.motions.Count == 0)
            {
                selectedMotion = -1;
                return;
            }

            selectedMotion = Mathf.Clamp(
                selectedMotion,
                -1,
                state.motions.Count - 1);
        }

        private void GetThresholdBounds(
            PlayableState state,
            out float min,
            out float max)
        {
            min = 0f;
            max = 1f;

            if (state.motions != null)
            {
                for (int i = 0; i < state.motions.Count; i++)
                {
                    PlayableMotion motion = state.motions[i];
                    if (motion == null)
                        continue;

                    min = Mathf.Min(min, motion.threshold);
                    max = Mathf.Max(max, motion.threshold);
                }
            }

            if (Application.isPlaying && runtimeTarget != null)
            {
                float value = runtimeTarget.GetFloat(state.blendParameterX);
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }

            if (Mathf.Approximately(min, max))
            {
                min -= 1f;
                max += 1f;
            }

            float padding = Mathf.Max(0.1f, (max - min) * 0.1f);
            min -= padding;
            max += padding;
        }

        private float ThresholdToPlotX(
            Rect plot,
            float min,
            float max,
            float value)
        {
            float t = Mathf.InverseLerp(min, max, value);
            return Mathf.Lerp(plot.xMin + 12f, plot.xMax - 12f, t);
        }

        private float PlotXToThreshold(
            Rect plot,
            float min,
            float max,
            float x)
        {
            float t = Mathf.InverseLerp(plot.xMin + 12f, plot.xMax - 12f, x);
            return Mathf.Lerp(min, max, t);
        }

        private void GetBlendBounds2D(
            PlayableState state,
            out float minX,
            out float maxX,
            out float minY,
            out float maxY)
        {
            minX = -1f;
            maxX = 1f;
            minY = -1f;
            maxY = 1f;

            if (state.motions != null)
            {
                for (int i = 0; i < state.motions.Count; i++)
                {
                    PlayableMotion motion = state.motions[i];
                    if (motion == null)
                        continue;

                    minX = Mathf.Min(minX, motion.position.x);
                    maxX = Mathf.Max(maxX, motion.position.x);
                    minY = Mathf.Min(minY, motion.position.y);
                    maxY = Mathf.Max(maxY, motion.position.y);
                }
            }

            if (Application.isPlaying && runtimeTarget != null)
            {
                minX = Mathf.Min(minX, runtimeTarget.GetFloat(state.blendParameterX));
                maxX = Mathf.Max(maxX, runtimeTarget.GetFloat(state.blendParameterX));
                minY = Mathf.Min(minY, runtimeTarget.GetFloat(state.blendParameterY));
                maxY = Mathf.Max(maxY, runtimeTarget.GetFloat(state.blendParameterY));
            }

            if (Mathf.Approximately(minX, maxX))
            {
                minX -= 1f;
                maxX += 1f;
            }

            if (Mathf.Approximately(minY, maxY))
            {
                minY -= 1f;
                maxY += 1f;
            }

            float xPadding = Mathf.Max(0.1f, (maxX - minX) * 0.1f);
            float yPadding = Mathf.Max(0.1f, (maxY - minY) * 0.1f);
            minX -= xPadding;
            maxX += xPadding;
            minY -= yPadding;
            maxY += yPadding;
        }

        private void DrawBlendSpaceAxes(
            Rect plot,
            float minX,
            float maxX,
            float minY,
            float maxY)
        {
            Vector2 zero = BlendToPlot2D(
                plot,
                minX,
                maxX,
                minY,
                maxY,
                Vector2.zero);

            Handles.BeginGUI();
            Handles.color = new Color(0.65f, 0.65f, 0.65f, 0.5f);
            if (zero.x >= plot.xMin && zero.x <= plot.xMax)
            {
                Handles.DrawLine(
                    new Vector3(zero.x, plot.yMin),
                    new Vector3(zero.x, plot.yMax));
            }

            if (zero.y >= plot.yMin && zero.y <= plot.yMax)
            {
                Handles.DrawLine(
                    new Vector3(plot.xMin, zero.y),
                    new Vector3(plot.xMax, zero.y));
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private Vector2 BlendToPlot2D(
            Rect plot,
            float minX,
            float maxX,
            float minY,
            float maxY,
            Vector2 value)
        {
            float x = Mathf.Lerp(
                plot.xMin,
                plot.xMax,
                Mathf.InverseLerp(minX, maxX, value.x));
            float y = Mathf.Lerp(
                plot.yMax,
                plot.yMin,
                Mathf.InverseLerp(minY, maxY, value.y));
            return new Vector2(x, y);
        }

        private Vector2 PlotToBlend2D(
            Rect plot,
            float minX,
            float maxX,
            float minY,
            float maxY,
            Vector2 point)
        {
            float x = Mathf.Lerp(
                minX,
                maxX,
                Mathf.InverseLerp(plot.xMin, plot.xMax, point.x));
            float y = Mathf.Lerp(
                minY,
                maxY,
                Mathf.InverseLerp(plot.yMax, plot.yMin, point.y));
            return new Vector2(x, y);
        }

        private void DrawBlendPoint(Vector2 point, bool selected)
        {
            float radius = selected ? 6f : 5f;
            Color color = selected ? Accent : new Color(0.45f, 0.68f, 1f, 1f);

            Handles.BeginGUI();
            Handles.color = new Color(0.03f, 0.05f, 0.08f, 1f);
            Handles.DrawAAConvexPolygon(
                new Vector3(point.x, point.y - radius - 1f),
                new Vector3(point.x + radius + 1f, point.y),
                new Vector3(point.x, point.y + radius + 1f),
                new Vector3(point.x - radius - 1f, point.y));
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                new Vector3(point.x, point.y - radius),
                new Vector3(point.x + radius, point.y),
                new Vector3(point.x, point.y + radius),
                new Vector3(point.x - radius, point.y));
            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private void DrawRuntimeMarker(Vector2 point, float radius)
        {
            Handles.BeginGUI();
            Handles.color = new Color(0.03f, 0.05f, 0.08f, 1f);
            Handles.DrawSolidDisc(point, Vector3.forward, radius + 2f);
            Handles.color = new Color(1f, 0.28f, 0.34f, 1f);
            Handles.DrawSolidDisc(point, Vector3.forward, radius);
            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private string ParameterPopupInline(
            string current,
            params GUILayoutOption[] options)
        {
            if (graphAsset == null ||
                graphAsset.parameters == null ||
                graphAsset.parameters.Count == 0)
            {
                return EditorGUILayout.TextField(current, options);
            }

            List<string> names = new List<string>();
            for (int i = 0; i < graphAsset.parameters.Count; i++)
            {
                PlayableParameter parameter = graphAsset.parameters[i];
                if (parameter == null ||
                    string.IsNullOrWhiteSpace(parameter.name))
                {
                    continue;
                }

                names.Add(parameter.name);
            }

            if (names.Count == 0)
                return EditorGUILayout.TextField(current, options);

            int index = names.IndexOf(current);
            if (index < 0)
            {
                names.Add(string.IsNullOrWhiteSpace(current)
                    ? "(none)"
                    : current);
                index = names.Count - 1;
            }

            int nextIndex = EditorGUILayout.Popup(index, names.ToArray(), options);
            return names[nextIndex] == "(none)" ? string.Empty : names[nextIndex];
        }

        private void DrawConditions(PlayableState state)
        {
            if (state.conditions == null)
                state.conditions = new List<PlayableCondition>();

            for (int i = 0; i < state.conditions.Count; i++)
            {
                PlayableCondition condition = state.conditions[i];
                if (condition == null)
                {
                    condition = new PlayableCondition();
                    state.conditions[i] = condition;
                }

                bool remove = false;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                condition.parameter =
                    ParameterPopup("Parameter", condition.parameter);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                    remove = true;
                EditorGUILayout.EndHorizontal();

                PlayableParameter parameter =
                    graphAsset.FindParameter(condition.parameter);
                PlayableParameterType type = parameter != null
                    ? parameter.type
                    : PlayableParameterType.Float;

                condition.mode = DrawConditionMode(condition.mode, type);
                DrawConditionValue(condition, parameter, type);

                EditorGUILayout.EndVertical();

                if (remove)
                {
                    state.conditions.RemoveAt(i);
                    break;
                }
            }

            if (GUILayout.Button("Add Condition"))
            {
                state.conditions.Add(new PlayableCondition
                {
                    parameter = FirstParameterName(),
                    boolValue = true
                });
            }
        }

        private void DrawInterruptions(
            PlayableLayer ownerLayer,
            PlayableState state)
        {
            if (state.interruptions == null)
                state.interruptions = new List<PlayableInterruption>();

            for (int i = 0; i < state.interruptions.Count; i++)
            {
                PlayableInterruption rule = state.interruptions[i];
                if (rule == null)
                {
                    rule = new PlayableInterruption();
                    state.interruptions[i] = rule;
                }

                bool remove = false;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                rule.enabled = EditorGUILayout.Toggle(
                    rule.enabled,
                    GUILayout.Width(18f));
                rule.scope =
                    (PlayableInterruptionScope)EditorGUILayout.EnumPopup(
                        rule.scope);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                    remove = true;
                EditorGUILayout.EndHorizontal();

                rule.timing =
                    (PlayableInterruptionTiming)EditorGUILayout.EnumPopup(
                        "Timing",
                        rule.timing);
                rule.fadeDurationOverride = Mathf.Max(
                    -1f,
                    EditorGUILayout.FloatField(
                        "Blend Override",
                        rule.fadeDurationOverride));

                if (rule.scope == PlayableInterruptionScope.SpecificState)
                {
                    rule.layerName = LayerNamePopup(
                        "Layer",
                        rule.layerName);
                    rule.stateName = StateNamePopup(
                        rule.layerName,
                        rule.stateName);
                }
                else if (rule.scope == PlayableInterruptionScope.SameLayer)
                {
                    rule.layerName = ownerLayer != null ? ownerLayer.name : string.Empty;
                    rule.stateName = string.Empty;
                }

                EditorGUILayout.EndVertical();

                if (remove)
                {
                    Undo.RecordObject(graphAsset, "Remove Playable Animator Interruption");
                    state.interruptions.RemoveAt(i);
                    EditorUtility.SetDirty(graphAsset);
                    break;
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Same Layer"))
                AddInterruption(
                    state,
                    PlayableInterruptionScope.SameLayer);
            if (GUILayout.Button("Add Self"))
                AddInterruption(
                    state,
                    PlayableInterruptionScope.Self);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Other Layers"))
                AddInterruption(
                    state,
                    PlayableInterruptionScope.OtherLayers);
            if (GUILayout.Button("Add All States"))
                AddInterruption(
                    state,
                    PlayableInterruptionScope.AllLayers);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Add Specific State"))
                AddInterruption(
                    state,
                    PlayableInterruptionScope.SpecificState);
        }

        private void DrawBehaviours(PlayableState state)
        {
            if (state.behaviours == null)
                state.behaviours = new List<PlayableStateBehaviour>();

            for (int i = 0; i < state.behaviours.Count; i++)
            {
                bool remove = false;
                EditorGUILayout.BeginHorizontal();
                state.behaviours[i] =
                    (PlayableStateBehaviour)EditorGUILayout.ObjectField(
                        state.behaviours[i],
                        typeof(PlayableStateBehaviour),
                        false);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                    remove = true;
                EditorGUILayout.EndHorizontal();

                if (remove)
                {
                    Undo.RecordObject(graphAsset, "Remove Playable State Behaviour");
                    state.behaviours.RemoveAt(i);
                    EditorUtility.SetDirty(graphAsset);
                    break;
                }
            }

            if (GUILayout.Button("Add Behaviour"))
            {
                Undo.RecordObject(graphAsset, "Add Playable State Behaviour");
                state.behaviours.Add(null);
                EditorUtility.SetDirty(graphAsset);
            }
        }

        private void DrawEvents(PlayableState state)
        {
            if (state.events == null)
                state.events = new List<PlayableStateEvent>();

            for (int i = 0; i < state.events.Count; i++)
            {
                PlayableStateEvent stateEvent = state.events[i];
                if (stateEvent == null)
                {
                    stateEvent = new PlayableStateEvent();
                    state.events[i] = stateEvent;
                }

                bool remove = false;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                stateEvent.enabled = EditorGUILayout.Toggle(
                    stateEvent.enabled,
                    GUILayout.Width(18f));
                stateEvent.name = EditorGUILayout.TextField(stateEvent.name);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                    remove = true;
                EditorGUILayout.EndHorizontal();

                stateEvent.timeMode =
                    (PlayableEventTimeMode)EditorGUILayout.EnumPopup(
                        "Time Mode",
                        stateEvent.timeMode);
                if (stateEvent.timeMode == PlayableEventTimeMode.Seconds)
                {
                    stateEvent.seconds = Mathf.Max(
                        0f,
                        EditorGUILayout.FloatField("Seconds", stateEvent.seconds));
                }
                else
                {
                    stateEvent.normalizedTime = Mathf.Max(
                        0f,
                        EditorGUILayout.FloatField(
                            "Normalized Time",
                            stateEvent.normalizedTime));
                }

                stateEvent.everyLoop =
                    EditorGUILayout.Toggle("Every Loop", stateEvent.everyLoop);
                DrawStateEventCallback(i);
                EditorGUILayout.EndVertical();

                if (remove)
                {
                    Undo.RecordObject(graphAsset, "Remove Playable State Event");
                    state.events.RemoveAt(i);
                    EditorUtility.SetDirty(graphAsset);
                    break;
                }
            }

            if (GUILayout.Button("Add Event"))
            {
                Undo.RecordObject(graphAsset, "Add Playable State Event");
                state.events.Add(new PlayableStateEvent());
                EditorUtility.SetDirty(graphAsset);
            }
        }

        private void DrawLayerSettings(PlayableLayer layer)
        {
            layer.name = EditorGUILayout.TextField("Layer Name", layer.name);
            layer.weight = EditorGUILayout.Slider("Weight", layer.weight, 0f, 1f);
            layer.additive = EditorGUILayout.Toggle("Additive", layer.additive);
            layer.avatarMask = (AvatarMask)EditorGUILayout.ObjectField(
                "Avatar Mask",
                layer.avatarMask,
                typeof(AvatarMask),
                false);

            graphAsset.defaultFadeDuration = Mathf.Max(
                0f,
                EditorGUILayout.FloatField(
                    "Default Blend",
                    graphAsset.defaultFadeDuration));
            graphAsset.showInPlayableGraphVisualizer =
                EditorGUILayout.Toggle(
                    "Show In Visualizer",
                    graphAsset.showInPlayableGraphVisualizer);
        }

        private void AddInterruption(
            PlayableState state,
            PlayableInterruptionScope scope)
        {
            if (state == null)
                return;

            if (state.interruptions == null)
                state.interruptions = new List<PlayableInterruption>();

            Undo.RecordObject(graphAsset, "Add Playable Animator Interruption");
            state.interruptions.Add(new PlayableInterruption
            {
                scope = scope
            });
            EditorUtility.SetDirty(graphAsset);
        }

        private void DrawStateEventCallback(int eventIndex)
        {
            if (graphAsset == null ||
                eventIndex < 0 ||
                selectedLayer < 0 ||
                selectedLayer >= graphAsset.layers.Count)
            {
                return;
            }

            string statePropertyPath = GetSelectedStateSerializedPath();
            if (string.IsNullOrWhiteSpace(statePropertyPath))
                return;

            SerializedObject serializedGraph = new SerializedObject(graphAsset);
            serializedGraph.Update();
            SerializedProperty callback = serializedGraph.FindProperty(
                statePropertyPath +
                ".events.Array.data[" + eventIndex +
                "].callback");

            if (callback == null)
                return;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                callback,
                new GUIContent("Callback"),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                serializedGraph.ApplyModifiedProperties();
                EditorUtility.SetDirty(graphAsset);
            }
            else
            {
                serializedGraph.ApplyModifiedProperties();
            }
        }

        private string GetSelectedStateSerializedPath()
        {
            if (graphAsset == null ||
                selectedLayer < 0 ||
                selectedLayer >= graphAsset.layers.Count)
            {
                return null;
            }

            PlayableLayer layer = graphAsset.layers[selectedLayer];
            if (layer == null || layer.states == null)
                return null;

            string path = "layers.Array.data[" + selectedLayer + "].states";
            List<PlayableState> states = layer.states;
            for (int i = 0; i < stateMachinePath.Count; i++)
            {
                PlayableState machine = stateMachinePath[i];
                int machineIndex = states.IndexOf(machine);
                if (machineIndex < 0 ||
                    machine == null ||
                    machine.subStates == null)
                {
                    return null;
                }

                path += ".Array.data[" + machineIndex + "].subStates";
                states = machine.subStates;
            }

            if (selectedState < 0 || selectedState >= states.Count)
                return null;

            return path + ".Array.data[" + selectedState + "]";
        }

        private void DrawParameters(bool includeAddButton = true)
        {
            if (graphAsset.parameters == null)
                graphAsset.parameters = new List<PlayableParameter>();

            for (int i = 0; i < graphAsset.parameters.Count; i++)
            {
                PlayableParameter parameter = graphAsset.parameters[i];
                if (parameter == null)
                {
                    parameter = new PlayableParameter();
                    graphAsset.parameters[i] = parameter;
                }

                bool remove = false;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                parameter.name = EditorGUILayout.TextField(
                    parameter.name,
                    GUILayout.MinWidth(92f));
                parameter.type =
                    (PlayableParameterType)EditorGUILayout.EnumPopup(
                        parameter.type,
                        GUILayout.Width(70f));
                DrawParameterDefaultInline(parameter);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                    remove = true;
                EditorGUILayout.EndHorizontal();

                if (parameter.type == PlayableParameterType.Enum)
                    DrawEnumOptions(parameter);

                EditorGUILayout.EndVertical();

                if (remove)
                {
                    graphAsset.parameters.RemoveAt(i);
                    break;
                }
            }

            if (includeAddButton && GUILayout.Button("Add Parameter"))
                AddParameter();
        }

        private void DrawParameterDefaultInline(
            PlayableParameter parameter)
        {
            GUILayout.Label(
                "Default",
                EditorStyles.miniLabel,
                GUILayout.Width(42f));

            switch (parameter.type)
            {
                case PlayableParameterType.Bool:
                case PlayableParameterType.Trigger:
                    parameter.boolValue =
                        EditorGUILayout.Toggle(
                            parameter.boolValue,
                            GUILayout.Width(18f));
                    break;
                case PlayableParameterType.Integer:
                    parameter.intValue =
                        EditorGUILayout.IntField(
                            parameter.intValue,
                            GUILayout.Width(52f));
                    break;
                case PlayableParameterType.Enum:
                    EnsureEnumOptions(parameter);
                    parameter.enumValue = EnumOptionPopupInline(
                        parameter,
                        parameter.enumValue,
                        GUILayout.MinWidth(62f));
                    break;
                default:
                    parameter.floatValue =
                        EditorGUILayout.FloatField(
                            parameter.floatValue,
                            GUILayout.Width(52f));
                    break;
            }
        }

        private void DrawRuntimeDebuggerTab()
        {
            DrawRuntimeTargetPicker();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to inspect live playable weights.",
                    MessageType.Info);
            }

            if (runtimeTarget == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a GameObject with PlayableAnimator.",
                    MessageType.Info);
                return;
            }

            debuggerScroll = EditorGUILayout.BeginScrollView(debuggerScroll);

            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Animator", ObjectName(runtimeTarget.Animator));
            EditorGUILayout.LabelField("Graph", ObjectName(runtimeTarget.GraphAsset));
            EditorGUILayout.LabelField(
                "Playable Graph",
                runtimeTarget.IsGraphValid ? "Valid" : "Not running");

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Initialize/Rebuild"))
                runtimeTarget.RebuildGraph();
            if (GUILayout.Button("Destroy Graph"))
                runtimeTarget.DestroyGraph();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            DrawRuntimeParameters();
            EditorGUILayout.Space(8f);
            DrawRuntimeLayers();

            EditorGUILayout.EndScrollView();
        }

        private void DrawRuntimeTargetPicker()
        {
            EditorGUILayout.BeginHorizontal();
            runtimeTarget = (PlayableAnimator)EditorGUILayout.ObjectField(
                "Target",
                runtimeTarget,
                typeof(PlayableAnimator),
                true);

            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                GatherRuntimeTargets(runtimeTargets);

            EditorGUILayout.EndHorizontal();

            GatherRuntimeTargets(runtimeTargets);
            if (runtimeTargets.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Scene Targets", GUILayout.Width(110f));
            for (int i = 0; i < runtimeTargets.Count; i++)
            {
                PlayableAnimator target = runtimeTargets[i];
                if (GUILayout.Button(target.name, GUILayout.MaxWidth(180f)))
                {
                    runtimeTarget = target;
                    if (runtimeTarget.GraphAsset != null)
                        graphAsset = runtimeTarget.GraphAsset;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRuntimeParameters()
        {
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);

            runtimeTarget.GetParameterSnapshot(parameterDebug);
            if (parameterDebug.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No runtime parameters are defined.",
                    MessageType.None);
                return;
            }

            for (int i = 0; i < parameterDebug.Count; i++)
            {
                PlayableParameterDebugInfo parameter = parameterDebug[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(parameter.Name, GUILayout.Width(160f));

                GUI.enabled = Application.isPlaying;
                switch (parameter.Type)
                {
                    case PlayableParameterType.Bool:
                        bool boolValue =
                            EditorGUILayout.Toggle(parameter.BoolValue);
                        if (boolValue != parameter.BoolValue)
                            runtimeTarget.SetBool(parameter.Name, boolValue);
                        break;

                    case PlayableParameterType.Integer:
                        int intValue =
                            EditorGUILayout.IntField(parameter.IntValue);
                        if (intValue != parameter.IntValue)
                            runtimeTarget.SetInteger(parameter.Name, intValue);
                        break;

                    case PlayableParameterType.Trigger:
                        if (GUILayout.Button("Trigger"))
                            runtimeTarget.SetTrigger(parameter.Name);
                        break;

                    case PlayableParameterType.Enum:
                        string enumValue = RuntimeEnumPopup(parameter);
                        if (enumValue != parameter.EnumValue)
                            runtimeTarget.SetEnum(parameter.Name, enumValue);
                        break;

                    default:
                        float floatValue =
                            EditorGUILayout.FloatField(parameter.FloatValue);
                        if (!Mathf.Approximately(floatValue, parameter.FloatValue))
                            runtimeTarget.SetFloat(parameter.Name, floatValue);
                        break;
                }

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawRuntimeLayers()
        {
            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);

            runtimeTarget.GetLayerSnapshot(layerDebug);
            if (layerDebug.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No live layers. Rebuild the runtime graph in Play Mode.",
                    MessageType.None);
                return;
            }

            for (int i = 0; i < layerDebug.Count; i++)
            {
                PlayableLayerDebugInfo layer = layerDebug[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawWeightBar(
                    $"{layer.Name} - {layer.ActiveState}",
                    layer.Weight,
                    18f);

                for (int j = 0; j < layer.States.Count; j++)
                {
                    PlayableStateDebugInfo state = layer.States[j];
                    DrawWeightBar(
                        $"{state.Name} [{state.Output}]",
                        state.Weight,
                        16f);

                    for (int k = 0; k < state.Motions.Count; k++)
                    {
                        PlayableMotionDebugInfo motion = state.Motions[k];
                        EditorGUI.indentLevel++;
                        DrawWeightBar(
                            $"{motion.Name} ({motion.ClipName})",
                            motion.Weight,
                            14f);
                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawLayerTabs()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24f));

            GUILayout.Label("Layers", EditorStyles.miniLabel, GUILayout.Width(54f));

            for (int i = 0; i < graphAsset.layers.Count; i++)
            {
                PlayableLayer layer = graphAsset.layers[i];
                string layerName = string.IsNullOrWhiteSpace(layer.name)
                    ? $"Layer {i + 1}"
                    : layer.name;

                bool selected = selectedLayer == i;
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = selected
                    ? new Color(0.42f, 0.52f, 0.66f, 1f)
                    : previousBackground;

                if (GUILayout.Toggle(
                        selected,
                        layerName,
                        EditorStyles.toolbarButton,
                        GUILayout.MinWidth(80f)))
                {
                    if (selectedLayer != i)
                    {
                        selectedLayer = i;
                        selectedState = 0;
                        selectedMotion = -1;
                        stateMachinePath.Clear();
                    }
                }

                GUI.backgroundColor = previousBackground;
            }

            if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(28f)))
                AddLayer();

            GUI.enabled = graphAsset.layers.Count > 1;
            if (GUILayout.Button("-", EditorStyles.toolbarButton, GUILayout.Width(28f)))
                RemoveSelectedLayer();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.Label("Playable Animator", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private bool DrawSectionHeader(bool open, string title)
        {
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                22f,
                GUILayout.ExpandWidth(true));
            rect.x += 2f;
            rect.width -= 4f;

            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 1f));
            Rect line = new Rect(rect.x, rect.yMax - 2f, rect.width, 2f);
            EditorGUI.DrawRect(line, Accent);

            Rect foldoutRect = new Rect(
                rect.x + 4f,
                rect.y + 2f,
                rect.width - 8f,
                rect.height - 4f);
            return EditorGUI.Foldout(foldoutRect, open, title, true);
        }

        private void ClampPanelWidths()
        {
            float maxSidebar = Mathf.Max(
                MinSidebarWidth,
                position.width -
                MinInspectorWidth -
                MinCanvasWidth -
                SplitterWidth * 2f);
            sidebarWidth = Mathf.Clamp(
                sidebarWidth,
                MinSidebarWidth,
                Mathf.Min(MaxSidebarWidth, maxSidebar));

            float maxInspector = Mathf.Max(
                MinInspectorWidth,
                position.width -
                sidebarWidth -
                MinCanvasWidth -
                SplitterWidth * 2f);
            inspectorWidth = Mathf.Clamp(
                inspectorWidth,
                MinInspectorWidth,
                Mathf.Min(MaxInspectorWidth, maxInspector));
        }

        private void DrawResizeHandle(
            ref float width,
            float minWidth,
            float maxWidth,
            bool growsWithMouse)
        {
            Rect rect = GUILayoutUtility.GetRect(
                SplitterWidth,
                1f,
                GUILayout.Width(SplitterWidth),
                GUILayout.ExpandHeight(true));

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            int controlId = GUIUtility.GetControlID(
                FocusType.Passive,
                rect);
            Event current = Event.current;

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && rect.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        current.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        float delta = growsWithMouse
                            ? current.delta.x
                            : -current.delta.x;
                        width = Mathf.Clamp(width + delta, minWidth, maxWidth);
                        current.Use();
                        Repaint();
                    }

                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }

                    break;
            }

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
                Rect line = new Rect(
                    rect.center.x - 0.5f,
                    rect.y,
                    1f,
                    rect.height);
                EditorGUI.DrawRect(line, new Color(1f, 1f, 1f, 0.12f));
            }
        }

        private PlayableConditionMode DrawConditionMode(
            PlayableConditionMode mode,
            PlayableParameterType type)
        {
            if (type != PlayableParameterType.Bool &&
                type != PlayableParameterType.Trigger &&
                type != PlayableParameterType.Enum)
            {
                return (PlayableConditionMode)EditorGUILayout.EnumPopup(
                    "Mode",
                    mode);
            }

            string[] labels =
            {
            "Equals",
            "Not Equals"
        };
            int index = mode == PlayableConditionMode.NotEquals ? 1 : 0;
            index = EditorGUILayout.Popup("Mode", index, labels);
            return index == 0
                ? PlayableConditionMode.Equals
                : PlayableConditionMode.NotEquals;
        }

        private void DrawConditionValue(
            PlayableCondition condition,
            PlayableParameter parameter,
            PlayableParameterType type)
        {
            switch (type)
            {
                case PlayableParameterType.Bool:
                case PlayableParameterType.Trigger:
                    condition.boolValue =
                        EditorGUILayout.Toggle("Value", condition.boolValue);
                    break;
                case PlayableParameterType.Integer:
                    condition.intValue =
                        EditorGUILayout.IntField("Value", condition.intValue);
                    break;
                case PlayableParameterType.Enum:
                    if (parameter != null)
                    {
                        condition.enumValue = EnumOptionPopup(
                            "Value",
                            parameter,
                            condition.enumValue);
                    }
                    else
                    {
                        condition.enumValue =
                            EditorGUILayout.TextField(
                                "Value",
                                condition.enumValue);
                    }
                    break;
                default:
                    condition.floatValue =
                        EditorGUILayout.FloatField(
                            "Value",
                            condition.floatValue);
                    break;
            }
        }

        private void DrawEnumOptions(PlayableParameter parameter)
        {
            EnsureEnumOptions(parameter);

            EditorGUILayout.LabelField("Enum Options", EditorStyles.miniBoldLabel);
            for (int i = 0; i < parameter.enumOptions.Count; i++)
            {
                bool remove = false;
                EditorGUILayout.BeginHorizontal();
                parameter.enumOptions[i] = EditorGUILayout.TextField(
                    parameter.enumOptions[i]);

                GUI.enabled = parameter.enumOptions.Count > 1;
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                    remove = true;
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();

                if (remove)
                {
                    parameter.enumOptions.RemoveAt(i);
                    if (!parameter.enumOptions.Contains(parameter.enumValue))
                        parameter.enumValue = parameter.enumOptions[0];
                    break;
                }
            }

            if (GUILayout.Button("Add Enum Option"))
                parameter.enumOptions.Add($"Value {parameter.enumOptions.Count + 1}");
        }

        private string ParameterPopup(string label, string current)
        {
            if (graphAsset == null ||
                graphAsset.parameters == null ||
                graphAsset.parameters.Count == 0)
            {
                return EditorGUILayout.TextField(label, current);
            }

            List<string> names = new List<string>();
            for (int i = 0; i < graphAsset.parameters.Count; i++)
            {
                PlayableParameter parameter = graphAsset.parameters[i];
                if (parameter == null ||
                    string.IsNullOrWhiteSpace(parameter.name))
                {
                    continue;
                }

                names.Add(parameter.name);
            }

            if (names.Count == 0)
                return EditorGUILayout.TextField(label, current);

            int index = names.IndexOf(current);
            if (index < 0)
            {
                names.Add(string.IsNullOrWhiteSpace(current)
                    ? "(none)"
                    : current);
                index = names.Count - 1;
            }

            int nextIndex = EditorGUILayout.Popup(label, index, names.ToArray());
            return names[nextIndex] == "(none)" ? string.Empty : names[nextIndex];
        }

        private string LayerNamePopup(string label, string current)
        {
            if (graphAsset == null ||
                graphAsset.layers == null ||
                graphAsset.layers.Count == 0)
            {
                return EditorGUILayout.TextField(label, current);
            }

            List<string> names = new List<string>
        {
            "(Any Layer)"
        };
            for (int i = 0; i < graphAsset.layers.Count; i++)
            {
                PlayableLayer layer = graphAsset.layers[i];
                if (layer == null || string.IsNullOrWhiteSpace(layer.name))
                    continue;

                names.Add(layer.name);
            }

            int index = string.IsNullOrWhiteSpace(current)
                ? 0
                : names.IndexOf(current);
            if (index < 0)
            {
                names.Add(current);
                index = names.Count - 1;
            }

            int nextIndex = EditorGUILayout.Popup(label, index, names.ToArray());
            return nextIndex == 0 ? string.Empty : names[nextIndex];
        }

        private string StateNamePopup(string layerName, string current)
        {
            if (graphAsset == null ||
                graphAsset.layers == null ||
                graphAsset.layers.Count == 0)
            {
                return EditorGUILayout.TextField("State", current);
            }

            List<string> names = new List<string>
        {
            "(Any State)"
        };
            for (int i = 0; i < graphAsset.layers.Count; i++)
            {
                PlayableLayer layer = graphAsset.layers[i];
                if (layer == null ||
                    layer.states == null ||
                    (!string.IsNullOrWhiteSpace(layerName) &&
                     !string.Equals(layer.name, layerName)))
                {
                    continue;
                }

                AddStateNames(layer.states, string.Empty, names);
            }

            int index = string.IsNullOrWhiteSpace(current)
                ? 0
                : names.IndexOf(current);
            if (index < 0)
            {
                names.Add(current);
                index = names.Count - 1;
            }

            int nextIndex = EditorGUILayout.Popup("State", index, names.ToArray());
            return nextIndex == 0 ? string.Empty : names[nextIndex];
        }

        private static void AddStateNames(
            List<PlayableState> states,
            string parentPath,
            List<string> names)
        {
            if (states == null)
                return;

            for (int i = 0; i < states.Count; i++)
            {
                PlayableState state = states[i];
                if (state == null)
                    continue;

                string path = string.IsNullOrWhiteSpace(parentPath)
                    ? state.DisplayName
                    : parentPath + "/" + state.DisplayName;
                if (state.IsSubStateMachine)
                {
                    AddStateNames(state.subStates, path, names);
                }
                else if (!names.Contains(path))
                {
                    names.Add(path);
                }
            }
        }

        private string EnumOptionPopup(
            string label,
            PlayableParameter parameter,
            string current)
        {
            EnsureEnumOptions(parameter);

            int index = parameter.enumOptions.IndexOf(current);
            if (index < 0)
            {
                current = parameter.enumOptions[0];
                index = 0;
            }

            int nextIndex = EditorGUILayout.Popup(
                label,
                index,
                parameter.enumOptions.ToArray());
            return parameter.enumOptions[nextIndex];
        }

        private string EnumOptionPopupInline(
            PlayableParameter parameter,
            string current,
            params GUILayoutOption[] options)
        {
            EnsureEnumOptions(parameter);

            int index = parameter.enumOptions.IndexOf(current);
            if (index < 0)
            {
                current = parameter.enumOptions[0];
                index = 0;
            }

            int nextIndex = EditorGUILayout.Popup(
                index,
                parameter.enumOptions.ToArray(),
                options);
            return parameter.enumOptions[nextIndex];
        }

        private string RuntimeEnumPopup(PlayableParameterDebugInfo parameter)
        {
            if (parameter.EnumOptions == null || parameter.EnumOptions.Count == 0)
                return EditorGUILayout.TextField(parameter.EnumValue);

            int index = parameter.EnumOptions.IndexOf(parameter.EnumValue);
            if (index < 0)
                index = 0;

            int nextIndex = EditorGUILayout.Popup(
                index,
                parameter.EnumOptions.ToArray());
            return parameter.EnumOptions[nextIndex];
        }

        private void EnsureEnumOptions(PlayableParameter parameter)
        {
            if (parameter.enumOptions == null)
                parameter.enumOptions = new List<string>();

            if (parameter.enumOptions.Count == 0)
                parameter.enumOptions.Add("Value");

            if (string.IsNullOrWhiteSpace(parameter.enumValue) ||
                !parameter.enumOptions.Contains(parameter.enumValue))
            {
                parameter.enumValue = parameter.enumOptions[0];
            }
        }

        private bool StateHasPreviewClips(PlayableState state)
        {
            if (state == null)
                return false;

            if (!UsesMotionList(state))
                return state.clip != null;

            if (state.motions == null)
                return false;

            for (int i = 0; i < state.motions.Count; i++)
            {
                if (IsPreviewMotionValid(state.motions[i]))
                    return true;
            }

            return false;
        }

        private float GetPreviewDuration(PlayableState state)
        {
            if (state == null)
                return 1f;

            if (state.output == PlayableStateOutput.Playlist)
                return Mathf.Max(0.01f, GetPreviewPlaylistDuration(state));

            if (!IsBlendOutput(state))
            {
                return state.clip != null
                    ? state.clip.length / Mathf.Max(0.01f, state.speed)
                    : 1f;
            }

            if (TryGetSynchronizedPreviewBlendDuration(
                    state,
                    out float synchronizedDuration))
            {
                return synchronizedDuration;
            }

            if (TryGetDominantPreviewMotionDuration(state, out float blendDuration))
                return blendDuration;

            float duration = 0f;
            if (state.motions != null)
            {
                for (int i = 0; i < state.motions.Count; i++)
                {
                    PlayableMotion motion = state.motions[i];
                    if (!IsPreviewMotionValid(motion))
                        continue;

                    duration = Mathf.Max(
                        duration,
                        motion.clip.length / Mathf.Max(0.01f, motion.speed));
                }
            }

            return Mathf.Max(0.01f, duration);
        }

        private bool TryGetSynchronizedPreviewBlendDuration(
            PlayableState state,
            out float duration)
        {
            duration = 0f;
            if (state == null ||
                state.motions == null ||
                state.motions.Count == 0 ||
                (state.output != PlayableStateOutput.BlendTree1D &&
                 state.output != PlayableStateOutput.BlendTree2D))
            {
                return false;
            }

            float[] weights = new float[state.motions.Count];
            bool hasWeights;
            if (state.output == PlayableStateOutput.BlendTree1D)
            {
                hasWeights = CalculatePreviewBlendTree1DWeights(state, weights);
            }
            else
            {
                Vector2 value = new Vector2(
                    GetPreviewBlendValue(state, state.blendParameterX, false),
                    GetPreviewBlendValue(state, state.blendParameterY, true));
                hasWeights = PlayableBlendMath.Calculate2DWeights(
                    state.motions,
                    value,
                    weights,
                    state.blendTree2DType);
            }

            return hasWeights &&
                   TryGetWeightedPreviewMotionDuration(state, weights, out duration);
        }

        private bool TryGetWeightedPreviewMotionDuration(
            PlayableState state,
            float[] weights,
            out float duration)
        {
            duration = 0f;
            if (state == null ||
                state.motions == null ||
                weights == null)
            {
                return false;
            }

            float weightedDuration = 0f;
            float totalWeight = 0f;
            float fallbackDuration = 0f;
            int count = Mathf.Min(state.motions.Count, weights.Length);

            for (int i = 0; i < count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (!IsPreviewMotionValid(motion))
                    continue;

                float motionDuration = motion.clip.length /
                    Mathf.Max(0.01f, motion.speed);
                if (fallbackDuration <= 0f)
                    fallbackDuration = motionDuration;

                float weight = Mathf.Clamp01(weights[i]);
                if (weight <= 0.0001f)
                    continue;

                weightedDuration += motionDuration * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0.0001f)
            {
                duration = Mathf.Max(0.01f, weightedDuration / totalWeight);
                return true;
            }

            if (fallbackDuration > 0f)
            {
                duration = Mathf.Max(0.01f, fallbackDuration);
                return true;
            }

            return false;
        }

        private bool TryGetDominantPreviewMotionDuration(
            PlayableState state,
            out float duration)
        {
            duration = 0f;
            if (state == null || state.motions == null || state.motions.Count == 0)
                return false;

            int motionIndex = -1;
            float bestWeight = 0f;

            switch (state.output)
            {
                case PlayableStateOutput.BlendTree1D:
                    motionIndex = GetDominantBlendTree1DMotion(state);
                    break;
                case PlayableStateOutput.BlendTree2D:
                    motionIndex = GetDominantBlendTree2DMotion(state);
                    break;
                case PlayableStateOutput.DirectBlend:
                    for (int i = 0; i < state.motions.Count; i++)
                    {
                        PlayableMotion motion = state.motions[i];
                        if (!IsPreviewMotionValid(motion))
                            continue;

                        float weight = GetPreviewFloatParameter(motion.directParameter);
                        if (motionIndex >= 0 && weight <= bestWeight)
                            continue;

                        bestWeight = weight;
                        motionIndex = i;
                    }

                    break;
            }

            if (motionIndex < 0 || motionIndex >= state.motions.Count)
                return false;

            PlayableMotion selectedMotion = state.motions[motionIndex];
            if (!IsPreviewMotionValid(selectedMotion))
                return false;

            duration = Mathf.Max(
                0.01f,
                selectedMotion.clip.length / Mathf.Max(0.01f, selectedMotion.speed));
            return true;
        }

        private int GetDominantBlendTree1DMotion(PlayableState state)
        {
            float value = GetPreviewBlendValue(state, state.blendParameterX, false);
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (!IsPreviewMotionValid(motion))
                    continue;

                float distance = Mathf.Abs(motion.threshold - value);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = i;
            }

            return best;
        }

        private int GetDominantBlendTree2DMotion(PlayableState state)
        {
            Vector2 value = new Vector2(
                GetPreviewBlendValue(state, state.blendParameterX, false),
                GetPreviewBlendValue(state, state.blendParameterY, true));
            float[] weights = new float[state.motions.Count];
            if (!PlayableBlendMath.Calculate2DWeights(
                    state.motions,
                    value,
                    weights,
                    state.blendTree2DType))
            {
                return -1;
            }

            int best = -1;
            float bestWeight = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (!IsPreviewMotionValid(state.motions[i]) ||
                    (best >= 0 && weights[i] <= bestWeight))
                {
                    continue;
                }

                bestWeight = weights[i];
                best = i;
            }

            return best;
        }

        private int GetPreviewInputCount(PlayableState state)
        {
            return UsesMotionList(state) && state.motions != null
                ? Mathf.Max(1, state.motions.Count)
                : 1;
        }

        private int GetPreviewSignature(
            PlayableState state,
            GameObject source)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 +
                       (source != null ? source.GetEntityId().GetHashCode() : 0);
                hash = hash * 31 + (state != null ? state.GetHashCode() : 0);
                hash = hash * 31 + (int)(state != null ? state.output : 0);

                if (state == null)
                    return hash;

                if (!UsesMotionList(state))
                {
                    hash = hash * 31 +
                           (state.clip != null
                               ? state.clip.GetEntityId().GetHashCode()
                               : 0);
                    hash = hash * 31 + (state.applyFootIK ? 1 : 0);
                    return hash;
                }

                int motionCount = state.motions != null ? state.motions.Count : 0;
                hash = hash * 31 + motionCount;
                for (int i = 0; i < motionCount; i++)
                {
                    PlayableMotion motion = state.motions[i];
                    hash = hash * 31 +
                           (motion != null &&
                            motion.clip != null
                                ? motion.clip.GetEntityId().GetHashCode()
                                : 0);
                    hash = hash * 31 + (motion != null && motion.enabled ? 1 : 0);
                    hash = hash * 31 +
                           (motion != null && motion.applyFootIK ? 1 : 0);
                }

                return hash;
            }
        }

        private bool IsBlendOutput(PlayableState state)
        {
            return state != null &&
                   (state.output == PlayableStateOutput.BlendTree1D ||
                    state.output == PlayableStateOutput.BlendTree2D ||
                    state.output == PlayableStateOutput.DirectBlend);
        }

        private bool UsesMotionList(PlayableState state)
        {
            return state != null &&
                   (state.output == PlayableStateOutput.Playlist ||
                    IsBlendOutput(state));
        }

        private bool IsPreviewMotionValid(PlayableMotion motion)
        {
            return motion != null && motion.enabled && motion.clip != null;
        }

        private GameObject GetPreviewRoot(GameObject source)
        {
            if (source == null)
                return null;

            Animator animator = source.GetComponent<Animator>();
            if (animator != null)
                return animator.gameObject;

            animator = source.GetComponentInChildren<Animator>();
            return animator != null ? animator.gameObject : source;
        }

        private float GetDefaultFloatParameter(string parameterName)
        {
            PlayableParameter parameter = graphAsset != null
                ? graphAsset.FindParameter(parameterName)
                : null;
            return parameter != null ? parameter.floatValue : 0f;
        }

        private float GetPreviewBlendValue(
            PlayableState state,
            string parameterName,
            bool yAxis)
        {
            if (Application.isPlaying && runtimeTarget != null)
                return runtimeTarget.GetFloat(parameterName);

            if (state != null &&
                string.Equals(
                    parameterName,
                    yAxis ? state.blendParameterY : state.blendParameterX))
            {
                return yAxis ? previewBlendY : previewBlendX;
            }

            return GetDefaultFloatParameter(parameterName);
        }

        private float GetPreviewFloatParameter(string parameterName)
        {
            if (Application.isPlaying && runtimeTarget != null)
                return runtimeTarget.GetFloat(parameterName);

            return GetDefaultFloatParameter(parameterName);
        }

        private Bounds CalculatePreviewBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.zero);
        }

        private void SetHideFlagsRecursive(GameObject root, HideFlags flags)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.hideFlags = flags;
        }

        private GameObject CreatePreviewInstance(GameObject sourceRoot)
        {
            GameObject stagingRoot = new GameObject(
                "Playable Animator Preview Staging");
            stagingRoot.hideFlags = HideFlags.HideAndDontSave;
            stagingRoot.SetActive(false);

            GameObject instance = Instantiate(
                sourceRoot,
                stagingRoot.transform,
                true);
            instance.name = $"{sourceRoot.name} Preview";
            SetHideFlagsRecursive(instance, HideFlags.HideAndDontSave);
            DisablePreviewBehaviours(instance);
            DisablePreviewPhysics(instance);

            instance.transform.SetParent(null, true);
            instance.SetActive(true);
            DestroyImmediate(stagingRoot);
            return instance;
        }

        private void DisablePreviewBehaviours(GameObject root)
        {
            Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour is Animator)
                    continue;

                behaviour.enabled = false;
            }

            Animator previewAnimator = root.GetComponentInChildren<Animator>();
            if (previewAnimator != null)
            {
                previewAnimator.enabled = true;
                previewAnimator.applyRootMotion = false;
                previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                previewAnimator.runtimeAnimatorController = null;
            }
        }

        private static void DisablePreviewPhysics(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            Rigidbody[] rigidbodies =
                root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].detectCollisions = false;
                rigidbodies[i].isKinematic = true;
            }
        }

        private void DisposePreviewResources()
        {
            if (previewGraph.IsValid())
                previewGraph.Destroy();

            if (previewInstance != null)
                DestroyImmediate(previewInstance);

            if (previewGrid != null)
                DestroyImmediate(previewGrid);

            if (previewGridMesh != null)
                DestroyImmediate(previewGridMesh);

            if (previewGridMaterial != null)
                DestroyImmediate(previewGridMaterial);

            if (previewUtility != null)
            {
                previewUtility.Cleanup();
                previewUtility = null;
            }

            previewInstance = null;
            previewGrid = null;
            previewGridMesh = null;
            previewGridMaterial = null;
            previewMixer = default;
            previewSignature = 0;
            previewBasePosition = Vector3.zero;
            previewBaseRotation = Quaternion.identity;
            previewRootMotionOffset = Vector3.zero;
            previewRootMotionTime = previewTime;
        }

        private void DrawWeightBar(string label, float weight, float height)
        {
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                height,
                GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(
                rect,
                Mathf.Clamp01(weight),
                $"{label} {weight:0.00}");
        }

        private void DrawGrid(Rect rect, float spacing, Color color)
        {
            DrawGrid(rect, spacing, color, Vector2.zero);
        }

        private void DrawGrid(Rect rect, float spacing, Color color, Vector2 offset)
        {
            spacing = Mathf.Max(4f, spacing);

            Handles.BeginGUI();
            Handles.color = color;

            float x = rect.xMin + offset.x % spacing;
            while (x > rect.xMin)
                x -= spacing;

            while (x < rect.xMax)
            {
                Handles.DrawLine(new Vector3(x, rect.yMin), new Vector3(x, rect.yMax));
                x += spacing;
            }

            float y = rect.yMin + offset.y % spacing;
            while (y > rect.yMin)
                y -= spacing;

            while (y < rect.yMax)
            {
                Handles.DrawLine(new Vector3(rect.xMin, y), new Vector3(rect.xMax, y));
                y += spacing;
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private void HandleDecisionCanvasInput(Rect canvasRect)
        {
            Event current = Event.current;
            if (!canvasRect.Contains(current.mousePosition))
                return;

            if (current.type == EventType.ScrollWheel)
            {
                float previousZoom = graphZoom;
                float zoomFactor = 1f - current.delta.y * 0.06f;
                graphZoom = Mathf.Clamp(previousZoom * zoomFactor, 0.35f, 2.5f);

                Vector2 localMouse = current.mousePosition - canvasRect.position;
                Vector2 centerOffset = localMouse - new Vector2(
                    canvasRect.width * 0.5f,
                    canvasRect.height * 0.5f);
                float ratio = graphZoom / previousZoom;
                graphPan = (graphPan - centerOffset) * ratio + centerOffset;

                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 2)
            {
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 2)
            {
                graphPan += current.delta;
                current.Use();
                Repaint();
            }
        }

        private Rect WorldToCanvasRect(Rect canvasRect, Rect worldRect)
        {
            Vector2 center = canvasRect.center + graphPan;
            return new Rect(
                center.x + worldRect.x * graphZoom,
                center.y + worldRect.y * graphZoom,
                worldRect.width * graphZoom,
                worldRect.height * graphZoom);
        }

        private void DrawConnection(Rect from, Rect to)
        {
            Handles.BeginGUI();
            Handles.color = new Color(0.8f, 0.8f, 0.8f, 0.8f);
            Vector3 fromPoint = new Vector3(from.xMax, from.center.y);
            Vector3 toPoint = new Vector3(to.xMin, to.center.y);
            Handles.DrawBezier(
                fromPoint,
                toPoint,
                fromPoint + Vector3.right * 80f,
                toPoint + Vector3.left * 80f,
                Handles.color,
                null,
                2f);
            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private void DrawCanvasNode(
            Rect rect,
            string title,
            string subtitle,
            Color color,
            bool highlighted)
        {
            EditorGUI.DrawRect(rect, color);
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, 2f),
                highlighted ? Accent : new Color(0.35f, 0.42f, 0.5f, 1f));
            GUI.Box(rect, GUIContent.none);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(12f * graphZoom, 8f, 12f))
            };
            titleStyle.normal.textColor = Color.white;

            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(10f * graphZoom, 7f, 10f))
            };
            subtitleStyle.normal.textColor = new Color(0.75f, 0.82f, 0.9f, 1f);

            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 22f),
                title,
                titleStyle);
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 34f, rect.width - 16f, rect.height - 42f),
                subtitle,
                subtitleStyle);
        }

        private Rect CenteredRect(Vector2 center, float width, float height)
        {
            return new Rect(
                center.x - width * 0.5f,
                center.y - height * 0.5f,
                width,
                height);
        }

        private List<PlayableState> GetCurrentStateList(
            PlayableLayer layer)
        {
            List<PlayableState> states = layer.states;
            int validDepth = 0;

            while (validDepth < stateMachinePath.Count)
            {
                PlayableState machine = stateMachinePath[validDepth];
                if (machine == null ||
                    !machine.IsSubStateMachine ||
                    states == null ||
                    !states.Contains(machine))
                {
                    break;
                }

                if (machine.subStates == null)
                    machine.subStates = new List<PlayableState>();

                states = machine.subStates;
                validDepth++;
            }

            if (validDepth < stateMachinePath.Count)
            {
                stateMachinePath.RemoveRange(
                    validDepth,
                    stateMachinePath.Count - validDepth);
            }

            return states ?? layer.states;
        }

        private PlayableState GetSelectedState(PlayableLayer layer)
        {
            List<PlayableState> states = GetCurrentStateList(layer);
            return selectedState >= 0 && selectedState < states.Count
                ? states[selectedState]
                : null;
        }

        private void DrawStateBreadcrumbs(PlayableLayer layer)
        {
            EditorGUILayout.BeginHorizontal();
            string layerName = string.IsNullOrWhiteSpace(layer.name)
                ? "Layer"
                : layer.name;
            if (GUILayout.Button(layerName, EditorStyles.miniButton))
                NavigateToStateMachineDepth(layer, 0);

            for (int i = 0; i < stateMachinePath.Count; i++)
            {
                GUILayout.Label("/", EditorStyles.miniLabel, GUILayout.Width(8f));
                PlayableState machine = stateMachinePath[i];
                if (GUILayout.Button(
                        machine != null ? machine.DisplayName : "(missing)",
                        EditorStyles.miniButton))
                {
                    NavigateToStateMachineDepth(layer, i + 1);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void NavigateToStateMachineDepth(
            PlayableLayer layer,
            int depth)
        {
            depth = Mathf.Clamp(depth, 0, stateMachinePath.Count);
            PlayableState selection = depth < stateMachinePath.Count
                ? stateMachinePath[depth]
                : null;

            if (depth < stateMachinePath.Count)
            {
                stateMachinePath.RemoveRange(
                    depth,
                    stateMachinePath.Count - depth);
            }

            List<PlayableState> states = GetCurrentStateList(layer);
            int selectionIndex = selection != null ? states.IndexOf(selection) : 0;
            selectedState = Mathf.Clamp(selectionIndex, 0, states.Count - 1);
            selectedMotion = -1;
        }

        private void OpenSubStateMachine(PlayableState state)
        {
            if (state == null || !state.IsSubStateMachine)
                return;

            if (state.subStates == null)
                state.subStates = new List<PlayableState>();
            if (state.subStates.Count == 0)
            {
                state.subStates.Add(new PlayableState
                {
                    name = "State 1",
                    isDefault = true,
                    fadeDuration = graphAsset.defaultFadeDuration
                });
            }

            stateMachinePath.Add(state);
            selectedState = 0;
            selectedMotion = -1;
            stateListScroll = Vector2.zero;
            graphPan = Vector2.zero;
        }

        private string GetSelectedStatePath(PlayableLayer layer)
        {
            string layerName = string.IsNullOrWhiteSpace(layer.name)
                ? "Layer"
                : layer.name;
            return layerName + " / " + GetSelectedStatePath(false);
        }

        private string GetSelectedStatePath(bool includeLayer)
        {
            if (graphAsset == null ||
                selectedLayer < 0 ||
                selectedLayer >= graphAsset.layers.Count)
            {
                return string.Empty;
            }

            PlayableLayer layer = graphAsset.layers[selectedLayer];
            List<string> names = new List<string>();
            for (int i = 0; i < stateMachinePath.Count; i++)
            {
                PlayableState machine = stateMachinePath[i];
                if (machine != null)
                    names.Add(machine.DisplayName);
            }

            PlayableState state = GetSelectedState(layer);
            if (state != null)
                names.Add(state.DisplayName);

            string path = string.Join("/", names.ToArray());
            if (!includeLayer)
                return path;

            string layerName = string.IsNullOrWhiteSpace(layer.name)
                ? "Layer"
                : layer.name;
            return layerName + "/" + path;
        }

        private string GetStateRowLabel(PlayableState state, int index)
        {
            if (state == null)
                return $"State {index + 1}";

            string prefix = state.isDefault ? "* " : string.Empty;
            string suffix = state.enabled ? string.Empty : " [off]";
            if (state.IsSubStateMachine)
                suffix += "  >";
            return prefix + state.DisplayName + suffix;
        }

        private string GetBlendParameterLabel(PlayableState state)
        {
            switch (state.output)
            {
                case PlayableStateOutput.Playlist:
                    return "Ordered clips";
                case PlayableStateOutput.BlendTree1D:
                    return state.blendParameterX;
                case PlayableStateOutput.BlendTree2D:
                    return $"{state.blendParameterX} / {state.blendParameterY}";
                case PlayableStateOutput.DirectBlend:
                    return "Direct weights";
                default:
                    return string.Empty;
            }
        }

        private string GetMotionSubtitle(
            PlayableState state,
            PlayableMotion motion)
        {
            switch (state.output)
            {
                case PlayableStateOutput.BlendTree1D:
                    return $"Threshold {motion.threshold:0.##}";
                case PlayableStateOutput.BlendTree2D:
                    return $"X {motion.position.x:0.##}, Y {motion.position.y:0.##}";
                case PlayableStateOutput.DirectBlend:
                    return motion.directParameter;
                default:
                    return GetClipName(motion.clip);
            }
        }

        private static string GetClipName(AnimationClip clip)
        {
            return clip != null ? clip.name : "(none)";
        }

        private static string GetStateOutputLabel(PlayableStateOutput output)
        {
            switch (output)
            {
                case PlayableStateOutput.Playlist:
                    return "Playlist";
                case PlayableStateOutput.BlendTree1D:
                    return "Blend Tree 1D";
                case PlayableStateOutput.BlendTree2D:
                    return "Blend Tree 2D";
                case PlayableStateOutput.DirectBlend:
                    return "Direct Blend";
                case PlayableStateOutput.OneShot:
                    return "One Shot";
                default:
                    return "Clip";
            }
        }

        private string FirstParameterName()
        {
            if (graphAsset == null || graphAsset.parameters == null)
                return string.Empty;

            for (int i = 0; i < graphAsset.parameters.Count; i++)
            {
                PlayableParameter parameter = graphAsset.parameters[i];
                if (parameter != null &&
                    !string.IsNullOrWhiteSpace(parameter.name))
                {
                    return parameter.name;
                }
            }

            return string.Empty;
        }

        private void AddLayer()
        {
            Undo.RecordObject(graphAsset, "Add Playable Animator Layer");
            graphAsset.layers.Add(new PlayableLayer
            {
                name = $"Layer {graphAsset.layers.Count + 1}"
            });
            selectedLayer = graphAsset.layers.Count - 1;
            selectedState = 0;
            stateMachinePath.Clear();
            graphAsset.EnsureDefaults();
            EditorUtility.SetDirty(graphAsset);
        }

        private void RemoveSelectedLayer()
        {
            Undo.RecordObject(graphAsset, "Remove Playable Animator Layer");
            graphAsset.layers.RemoveAt(selectedLayer);
            selectedLayer = Mathf.Clamp(
                selectedLayer,
                0,
                graphAsset.layers.Count - 1);
            selectedState = 0;
            stateMachinePath.Clear();
            graphAsset.EnsureDefaults();
            EditorUtility.SetDirty(graphAsset);
        }

        private void AddState(List<PlayableState> states)
        {
            Undo.RecordObject(graphAsset, "Add Playable Animator State");
            states.Add(new PlayableState
            {
                name = $"State {states.Count + 1}",
                fadeDuration = graphAsset.defaultFadeDuration
            });
            selectedState = states.Count - 1;
            EditorUtility.SetDirty(graphAsset);
        }

        private void AddSubStateMachine(List<PlayableState> states)
        {
            Undo.RecordObject(graphAsset, "Add Playable Animator Sub-State Machine");
            PlayableState machine = new PlayableState
            {
                name = $"Sub-State Machine {states.Count + 1}",
                kind = PlayableStateKind.SubStateMachine,
                fadeDuration = graphAsset.defaultFadeDuration
            };
            machine.subStates.Add(new PlayableState
            {
                name = "State 1",
                isDefault = true,
                fadeDuration = graphAsset.defaultFadeDuration
            });
            states.Add(machine);
            selectedState = states.Count - 1;
            EditorUtility.SetDirty(graphAsset);
        }

        private void RemoveSelectedState(List<PlayableState> states)
        {
            Undo.RecordObject(graphAsset, "Remove Playable Animator State");
            states.RemoveAt(selectedState);
            selectedState = Mathf.Clamp(
                selectedState,
                0,
                states.Count - 1);
            graphAsset.EnsureDefaults();
            EditorUtility.SetDirty(graphAsset);
        }

        private void MakeDefaultState(
            List<PlayableState> states,
            PlayableState state)
        {
            Undo.RecordObject(graphAsset, "Set Default Playable Animator State");
            for (int i = 0; i < states.Count; i++)
                states[i].isDefault = false;

            state.isDefault = true;
            EditorUtility.SetDirty(graphAsset);
        }

        private void AddParameter()
        {
            Undo.RecordObject(graphAsset, "Add Playable Animator Parameter");
            graphAsset.parameters.Add(new PlayableParameter
            {
                name = UniqueParameterName("Parameter")
            });
            EditorUtility.SetDirty(graphAsset);
        }

        private void AddLocomotionTemplate()
        {
            Undo.RecordObject(graphAsset, "Add Playable Animator Template");

            AddParameterIfMissing("Speed", PlayableParameterType.Float);
            AddParameterIfMissing("MoveX", PlayableParameterType.Float);
            AddParameterIfMissing("MoveY", PlayableParameterType.Float);
            AddParameterIfMissing("Stance", PlayableParameterType.Enum);

            PlayableLayer layer = new PlayableLayer
            {
                name = "Locomotion",
                weight = 1f
            };

            layer.states.Add(new PlayableState
            {
                name = "Idle",
                isDefault = true,
                output = PlayableStateOutput.Clip,
                fadeDuration = graphAsset.defaultFadeDuration
            });

            PlayableState locomotion = new PlayableState
            {
                name = "Move",
                output = PlayableStateOutput.BlendTree2D,
                blendParameterX = "MoveX",
                blendParameterY = "MoveY",
                blendTree2DType = PlayableBlendTree2DType.FreeformDirectional,
                priority = 10,
                fadeDuration = graphAsset.defaultFadeDuration
            };
            locomotion.conditions.Add(new PlayableCondition
            {
                parameter = "Speed",
                mode = PlayableConditionMode.Greater,
                floatValue = 0.05f
            });
            locomotion.motions.Add(new PlayableMotion
            {
                name = "Forward",
                position = new Vector2(0f, 1f)
            });
            locomotion.motions.Add(new PlayableMotion
            {
                name = "Right",
                position = new Vector2(1f, 0f)
            });
            locomotion.motions.Add(new PlayableMotion
            {
                name = "Back",
                position = new Vector2(0f, -1f)
            });
            locomotion.motions.Add(new PlayableMotion
            {
                name = "Left",
                position = new Vector2(-1f, 0f)
            });
            layer.states.Add(locomotion);

            graphAsset.layers.Add(layer);
            selectedLayer = graphAsset.layers.Count - 1;
            selectedState = 0;
            stateMachinePath.Clear();
            EditorUtility.SetDirty(graphAsset);
        }

        private void AddParameterIfMissing(
            string parameterName,
            PlayableParameterType type)
        {
            if (graphAsset.FindParameter(parameterName) != null)
                return;

            PlayableParameter parameter = new PlayableParameter
            {
                name = parameterName,
                type = type
            };

            if (type == PlayableParameterType.Enum)
            {
                parameter.enumOptions = new List<string>
            {
                "Relaxed",
                "Combat",
                "Crouched"
            };
                parameter.enumValue = "Relaxed";
            }

            graphAsset.parameters.Add(parameter);
        }

        private string UniqueParameterName(string baseName)
        {
            string candidate = baseName;
            int suffix = 1;
            while (graphAsset.FindParameter(candidate) != null)
            {
                suffix++;
                candidate = $"{baseName} {suffix}";
            }

            return candidate;
        }

        private void ClampSelection()
        {
            selectedLayer = Mathf.Clamp(
                selectedLayer,
                0,
                graphAsset.layers.Count - 1);

            PlayableLayer layer = graphAsset.layers[selectedLayer];
            List<PlayableState> states = GetCurrentStateList(layer);
            selectedState = Mathf.Clamp(
                selectedState,
                0,
                states.Count - 1);
        }

        private void CreateGraphAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Playable Animator Graph",
                "PlayableAnimatorGraph",
                "asset",
                "Choose where to save the graph asset.");

            if (string.IsNullOrWhiteSpace(path))
                return;

            PlayableAnimatorGraph asset =
                CreateInstance<PlayableAnimatorGraph>();
            asset.EnsureDefaults();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            graphAsset = asset;
            stateMachinePath.Clear();
            selectedLayer = 0;
            selectedState = 0;
            Selection.activeObject = asset;
        }

        private static void GatherRuntimeTargets(
            List<PlayableAnimator> results)
        {
            results.Clear();
            PlayableAnimator[] targets =
                Resources.FindObjectsOfTypeAll<PlayableAnimator>();

            for (int i = 0; i < targets.Length; i++)
            {
                PlayableAnimator target = targets[i];
                if (target == null || EditorUtility.IsPersistent(target))
                    continue;

                results.Add(target);
            }
        }

        private static string ObjectName(Object target)
        {
            return target != null ? target.name : "(none)";
        }
    }

    [CustomEditor(typeof(PlayableAnimator))]
    public sealed class PlayableAnimatorInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayableAnimator animator =
                (PlayableAnimator)target;

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Open Playable Animator Window"))
            {
                PlayableAnimatorWindow.Open(
                    animator.GraphAsset,
                    animator);
            }

            GUI.enabled = Application.isPlaying;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild Graph"))
                animator.RebuildGraph();
            if (GUILayout.Button("Destroy Graph"))
                animator.DestroyGraph();
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            if (Application.isPlaying && animator.IsGraphValid)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "Runtime Layer Weights",
                    EditorStyles.boldLabel);

                for (int i = 0; i < animator.RuntimeLayerCount; i++)
                {
                    if (!animator.TryGetLayerWeight(
                            i,
                            out string layerName,
                            out float weight))
                    {
                        continue;
                    }

                    EditorGUI.BeginChangeCheck();
                    float nextWeight = EditorGUILayout.Slider(
                        layerName,
                        weight,
                        0f,
                        1f);
                    if (EditorGUI.EndChangeCheck())
                        animator.SetLayerWeight(i, nextWeight);
                }
            }
        }
    }

    [CustomEditor(typeof(PlayableAnimatorGraph))]
    public sealed class PlayableAnimatorGraphInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Open Playable Animator Window"))
            {
                PlayableAnimatorWindow.Open(
                    (PlayableAnimatorGraph)target);
            }
        }
    }
}
