using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Playgraph
{
    public sealed class PlayableAnimatorDebuggerWindow : EditorWindow
    {
        private readonly List<PlayableAnimator> runtimeTargets =
            new List<PlayableAnimator>();
        private readonly List<PlayableParameterDebugInfo> parameterDebug =
            new List<PlayableParameterDebugInfo>();
        private readonly List<PlayableLayerDebugInfo> layerDebug =
            new List<PlayableLayerDebugInfo>();

        [SerializeField] private PlayableAnimator runtimeTarget;
        [SerializeField] private bool followSelection = true;
        private Vector2 scrollPosition;

        [MenuItem("Play Graph/Runtime Debugger")]
        public static void Open()
        {
            Open(null);
        }

        public static void Open(PlayableAnimator target)
        {
            PlayableAnimatorDebuggerWindow window =
                GetWindow<PlayableAnimatorDebuggerWindow>(
                    "Playable Animator Debugger");
            if (target != null)
                window.SetTarget(target, false);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            minSize = new Vector2(360f, 240f);
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RefreshTargets();
            if (followSelection)
                FollowCurrentSelection();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void Update()
        {
            if (Application.isPlaying)
                Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to inspect live playable weights.",
                    MessageType.Info);
            }

            if (runtimeTarget == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a GameObject with PlayableAnimator or choose a target from the toolbar.",
                    MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawRuntimeSummary();
            EditorGUILayout.Space(8f);
            DrawRuntimeParameters();
            EditorGUILayout.Space(8f);
            DrawRuntimeLayers();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            PlayableAnimator nextTarget =
                (PlayableAnimator)EditorGUILayout.ObjectField(
                    runtimeTarget,
                    typeof(PlayableAnimator),
                    true,
                    GUILayout.MinWidth(160f));
            if (EditorGUI.EndChangeCheck())
                SetTarget(nextTarget, false);

            if (GUILayout.Button(
                    "Targets",
                    EditorStyles.toolbarDropDown,
                    GUILayout.Width(72f)))
            {
                ShowTargetMenu();
            }

            GUILayout.FlexibleSpace();

            bool nextFollowSelection = GUILayout.Toggle(
                followSelection,
                "Follow Selection",
                EditorStyles.toolbarButton);
            if (nextFollowSelection != followSelection)
            {
                followSelection = nextFollowSelection;
                if (followSelection)
                    FollowCurrentSelection();
            }

            if (GUILayout.Button(
                    "Refresh",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(58f)))
            {
                RefreshTargets();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRuntimeSummary()
        {
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.ObjectField(
                "Animator",
                runtimeTarget.Animator,
                typeof(Animator),
                true);
            EditorGUILayout.ObjectField(
                "Graph",
                runtimeTarget.GraphAsset,
                typeof(PlayableAnimatorGraph),
                false);
            EditorGUILayout.LabelField(
                "Playable Graph",
                runtimeTarget.IsGraphValid ? "Valid" : "Not running");

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(runtimeTarget.GraphAsset == null))
            {
                if (GUILayout.Button("Open Graph"))
                {
                    PlayableAnimatorWindow.Open(
                        runtimeTarget.GraphAsset,
                        runtimeTarget);
                }

                if (GUILayout.Button("Ping Graph"))
                    EditorGUIUtility.PingObject(runtimeTarget.GraphAsset);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Rebuild"))
                    runtimeTarget.RebuildGraph();
                if (GUILayout.Button("Destroy"))
                    runtimeTarget.DestroyGraph();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
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
                EditorGUILayout.LabelField(
                    parameter.Name,
                    GUILayout.MinWidth(120f),
                    GUILayout.MaxWidth(220f));

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    DrawRuntimeParameterValue(parameter);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawRuntimeParameterValue(
            PlayableParameterDebugInfo parameter)
        {
            switch (parameter.Type)
            {
                case PlayableParameterType.Bool:
                    bool boolValue = EditorGUILayout.Toggle(parameter.BoolValue);
                    if (boolValue != parameter.BoolValue)
                        runtimeTarget.SetBool(parameter.Name, boolValue);
                    break;
                case PlayableParameterType.Integer:
                    int intValue = EditorGUILayout.IntField(parameter.IntValue);
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

        private void ShowTargetMenu()
        {
            RefreshTargets();
            GenericMenu menu = new GenericMenu();
            if (runtimeTargets.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No scene targets"));
            }
            else
            {
                for (int i = 0; i < runtimeTargets.Count; i++)
                {
                    PlayableAnimator target = runtimeTargets[i];
                    menu.AddItem(
                        new GUIContent(GetHierarchyPath(target.transform)),
                        target == runtimeTarget,
                        () => SetTarget(target, false));
                }
            }

            menu.ShowAsContext();
        }

        private void SetTarget(PlayableAnimator target, bool keepFollowingSelection)
        {
            runtimeTarget = target;
            if (!keepFollowingSelection)
                followSelection = false;
            Repaint();
        }

        private void OnSelectionChanged()
        {
            if (followSelection)
                FollowCurrentSelection();
            Repaint();
        }

        private void FollowCurrentSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
                return;

            PlayableAnimator selectedAnimator =
                selected.GetComponent<PlayableAnimator>();
            if (selectedAnimator != null)
                SetTarget(selectedAnimator, true);
        }

        private void OnHierarchyChanged()
        {
            RefreshTargets();
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            RefreshTargets();
            Repaint();
        }

        private void RefreshTargets()
        {
            runtimeTargets.Clear();
            PlayableAnimator[] targets =
                Resources.FindObjectsOfTypeAll<PlayableAnimator>();

            for (int i = 0; i < targets.Length; i++)
            {
                PlayableAnimator target = targets[i];
                if (target == null || EditorUtility.IsPersistent(target))
                    continue;

                runtimeTargets.Add(target);
            }

            runtimeTargets.Sort((left, right) =>
                string.CompareOrdinal(
                    GetHierarchyPath(left.transform),
                    GetHierarchyPath(right.transform)));
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return "(missing)";

            List<string> names = new List<string>();
            Transform current = target;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }
    }
}
