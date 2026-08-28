using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Playgraph
{
    [CustomPropertyDrawer(typeof(ObjectAnimationAction))]
    public sealed class ObjectAnimationActionDrawer : PropertyDrawer
    {
        private const float Gap = 2f;

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            int lineCount = 4;
            SerializedProperty source = property.FindPropertyRelative("source");
            if ((ObjectAnimationActionSource)source.enumValueIndex ==
                ObjectAnimationActionSource.GraphState)
            {
                lineCount += 3;
            }
            else
            {
                SerializedProperty clip = property.FindPropertyRelative("clip");
                return EditorGUIUtility.singleLineHeight * lineCount +
                       Gap * lineCount +
                       EditorGUI.GetPropertyHeight(clip, true);
            }

            return EditorGUIUtility.singleLineHeight * lineCount +
                   Gap * (lineCount - 1);
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty name = property.FindPropertyRelative("name");
            string title = string.IsNullOrWhiteSpace(name.stringValue)
                ? "Action"
                : name.stringValue;
            Rect row = NextRow(ref position);
            property.isExpanded = EditorGUI.Foldout(
                row,
                property.isExpanded,
                title,
                true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUI.PropertyField(NextRow(ref position), name);
            SerializedProperty source = property.FindPropertyRelative("source");
            EditorGUI.PropertyField(NextRow(ref position), source);

            if ((ObjectAnimationActionSource)source.enumValueIndex ==
                ObjectAnimationActionSource.GraphState)
            {
                EditorGUI.PropertyField(
                    NextRow(ref position),
                    property.FindPropertyRelative("layerName"));
                EditorGUI.PropertyField(
                    NextRow(ref position),
                    property.FindPropertyRelative("stateName"));
                EditorGUI.PropertyField(
                    NextRow(ref position),
                    property.FindPropertyRelative("oneShot"));
            }
            else
            {
                SerializedProperty clip = property.FindPropertyRelative("clip");
                float height = EditorGUI.GetPropertyHeight(clip, true);
                Rect clipRect = new Rect(
                    position.x,
                    position.y,
                    position.width,
                    height);
                EditorGUI.PropertyField(clipRect, clip, true);
                position.y += height + Gap;
            }

            EditorGUI.PropertyField(
                NextRow(ref position),
                property.FindPropertyRelative("objectAnimatorTrigger"));
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static Rect NextRow(ref Rect position)
        {
            Rect row = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            position.y += EditorGUIUtility.singleLineHeight + Gap;
            return row;
        }
    }

    [CustomEditor(typeof(ObjectAnimationProvider))]
    public sealed class ObjectAnimationProviderInspector : UnityEditor.Editor
    {
        private SerializedProperty animationId;
        private SerializedProperty characterGraph;
        private SerializedProperty layerName;
        private SerializedProperty enterState;
        private SerializedProperty loopState;
        private SerializedProperty exitState;
        private SerializedProperty blendIn;
        private SerializedProperty blendOut;
        private SerializedProperty actions;
        private SerializedProperty objectAnimator;
        private SerializedProperty enterObjectTrigger;
        private SerializedProperty exitObjectTrigger;
        private SerializedProperty interactionStarted;
        private SerializedProperty interactionEnded;
        private SerializedProperty actionPlayed;

        private void OnEnable()
        {
            animationId = serializedObject.FindProperty("animationId");
            characterGraph = serializedObject.FindProperty("characterGraph");
            layerName = serializedObject.FindProperty("layerName");
            enterState = serializedObject.FindProperty("enterState");
            loopState = serializedObject.FindProperty("loopState");
            exitState = serializedObject.FindProperty("exitState");
            blendIn = serializedObject.FindProperty("blendIn");
            blendOut = serializedObject.FindProperty("blendOut");
            actions = serializedObject.FindProperty("actions");
            objectAnimator = serializedObject.FindProperty("objectAnimator");
            enterObjectTrigger = serializedObject.FindProperty("enterObjectTrigger");
            exitObjectTrigger = serializedObject.FindProperty("exitObjectTrigger");
            interactionStarted = serializedObject.FindProperty("interactionStarted");
            interactionEnded = serializedObject.FindProperty("interactionEnded");
            actionPlayed = serializedObject.FindProperty("actionPlayed");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Network Identity", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(animationId, new GUIContent("Animation ID"));

            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField(
                "Character Animation",
                EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(characterGraph);
            using (new EditorGUI.DisabledScope(characterGraph.objectReferenceValue == null))
            {
                if (GUILayout.Button("Open Character Graph"))
                {
                    PlayableAnimatorWindow.Open(
                        (PlayableAnimatorGraph)characterGraph.objectReferenceValue);
                }
            }

            DrawLayerField();
            DrawStateField(enterState, "Enter State");
            DrawStateField(loopState, "Loop State");
            DrawStateField(exitState, "Exit State");
            EditorGUILayout.PropertyField(blendIn);
            EditorGUILayout.PropertyField(blendOut);

            EditorGUILayout.Space(5f);
            EditorGUILayout.PropertyField(
                actions,
                new GUIContent("Actions"),
                true);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Use Clip"))
                AddAction("Use", ObjectAnimationActionSource.AnimationClip);
            if (GUILayout.Button("Add Weapon Actions"))
            {
                AddAction("Fire", ObjectAnimationActionSource.GraphState);
                AddAction("Reload", ObjectAnimationActionSource.GraphState);
                AddAction("Unjam", ObjectAnimationActionSource.GraphState);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Object Playback", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(objectAnimator);
            EditorGUILayout.PropertyField(enterObjectTrigger);
            EditorGUILayout.PropertyField(exitObjectTrigger);

            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Callbacks", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(interactionStarted);
            EditorGUILayout.PropertyField(interactionEnded);
            EditorGUILayout.PropertyField(actionPlayed);

            DrawValidation();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLayerField()
        {
            PlayableAnimatorGraph graph =
                characterGraph.objectReferenceValue as PlayableAnimatorGraph;
            if (graph == null || graph.layers == null || graph.layers.Count == 0)
            {
                EditorGUILayout.PropertyField(layerName);
                return;
            }

            List<string> options = new List<string>
        {
            "(First Layer)"
        };
            for (int i = 0; i < graph.layers.Count; i++)
            {
                if (graph.layers[i] != null)
                    options.Add(graph.layers[i].name);
            }

            int selected = string.IsNullOrWhiteSpace(layerName.stringValue)
                ? 0
                : options.IndexOf(layerName.stringValue);
            if (selected < 0)
            {
                options.Add(layerName.stringValue);
                selected = options.Count - 1;
            }

            int next = EditorGUILayout.Popup("Layer", selected, options.ToArray());
            layerName.stringValue = next <= 0 ? string.Empty : options[next];
        }

        private void DrawStateField(SerializedProperty property, string label)
        {
            List<string> options = GatherStatePaths();
            if (options.Count <= 1)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
                return;
            }

            int selected = string.IsNullOrWhiteSpace(property.stringValue)
                ? 0
                : options.IndexOf(property.stringValue);
            if (selected < 0)
            {
                options.Add(property.stringValue);
                selected = options.Count - 1;
            }

            int next = EditorGUILayout.Popup(label, selected, options.ToArray());
            property.stringValue = next <= 0 ? string.Empty : options[next];
        }

        private List<string> GatherStatePaths()
        {
            List<string> results = new List<string>
        {
            "(None)"
        };
            PlayableAnimatorGraph graph =
                characterGraph.objectReferenceValue as PlayableAnimatorGraph;
            if (graph == null || graph.layers == null)
                return results;

            PlayableLayer selectedLayer = null;
            if (string.IsNullOrWhiteSpace(layerName.stringValue))
            {
                if (graph.layers.Count > 0)
                    selectedLayer = graph.layers[0];
            }
            else
            {
                for (int i = 0; i < graph.layers.Count; i++)
                {
                    if (graph.layers[i] != null &&
                        graph.layers[i].name == layerName.stringValue)
                    {
                        selectedLayer = graph.layers[i];
                        break;
                    }
                }
            }

            if (selectedLayer != null)
                GatherStatePaths(selectedLayer.states, string.Empty, results);
            return results;
        }

        private static void GatherStatePaths(
            List<PlayableState> states,
            string parentPath,
            List<string> results)
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
                    GatherStatePaths(state.subStates, path, results);
                else
                    results.Add(path);
            }
        }

        private void AddAction(
            string actionName,
            ObjectAnimationActionSource source)
        {
            int index = actions.arraySize;
            actions.InsertArrayElementAtIndex(index);
            SerializedProperty action = actions.GetArrayElementAtIndex(index);
            action.FindPropertyRelative("name").stringValue = actionName;
            action.FindPropertyRelative("source").enumValueIndex = (int)source;
            action.FindPropertyRelative("layerName").stringValue = string.Empty;
            action.FindPropertyRelative("stateName").stringValue = actionName;
            action.FindPropertyRelative("oneShot").boolValue = true;
            action.FindPropertyRelative("objectAnimatorTrigger").stringValue =
                string.Empty;

            SerializedProperty clip = action.FindPropertyRelative("clip");
            clip.FindPropertyRelative("name").stringValue = actionName;
            clip.FindPropertyRelative("clip").objectReferenceValue = null;
            action.isExpanded = true;
        }

        private void DrawValidation()
        {
            if (characterGraph.objectReferenceValue == null &&
                (!string.IsNullOrWhiteSpace(enterState.stringValue) ||
                 !string.IsNullOrWhiteSpace(loopState.stringValue) ||
                 !string.IsNullOrWhiteSpace(exitState.stringValue)))
            {
                EditorGUILayout.HelpBox(
                    "Enter, loop, and exit states require a character graph.",
                    MessageType.Warning);
            }
        }
    }

    [CustomEditor(typeof(ObjectAnimationPlayer))]
    public sealed class ObjectAnimationPlayerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ObjectAnimationPlayer player = (ObjectAnimationPlayer)target;
            if (!Application.isPlaying)
                return;

            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Provider",
                player.ActiveProvider != null
                    ? player.ActiveProvider.name
                    : "(none)");

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(player.IsActive))
            {
                if (GUILayout.Button("Begin Default"))
                    player.Begin();
            }
            using (new EditorGUI.DisabledScope(!player.IsActive))
            {
                if (GUILayout.Button("End"))
                    player.End();
                if (GUILayout.Button("Cancel"))
                    player.Cancel();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
