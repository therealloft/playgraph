using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Playgraph
{
    internal sealed class InterruptionTargetPopup : PopupWindowContent
    {
        private static readonly Color Accent =
            new Color(0.18f, 0.74f, 0.46f, 1f);
        private static readonly Color HeaderBackground =
            new Color(0.12f, 0.12f, 0.12f, 1f);

        private const float WindowWidth = 410f;
        private const float WindowHeight = 430f;
        private const float RowHeight = 21f;
        private const float IndentWidth = 16f;
        private const float CountWidth = 52f;

        private readonly PlayableAnimatorGraph graph;
        private readonly PlayableState ownerState;
        private readonly Action changed;
        private readonly HashSet<string> expandedBranches =
            new HashSet<string>();

        private Vector2 scrollPosition;

        public InterruptionTargetPopup(
            PlayableAnimatorGraph graph,
            PlayableState ownerState,
            Action changed)
        {
            this.graph = graph;
            this.ownerState = ownerState;
            this.changed = changed;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(WindowWidth, WindowHeight);
        }

        public override void OnGUI(Rect rect)
        {
            if (graph == null || ownerState == null)
            {
                EditorGUILayout.HelpBox(
                    "The interruption target is no longer available.",
                    MessageType.Info);
                return;
            }

            DrawHeader();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            GUILayout.Space(3f);
            DrawSpecialTarget("All States", PlayableInterruptionTarget.AllStates);
            DrawSpecialTarget(
                "All States From Other Layers",
                PlayableInterruptionTarget.AllStatesFromOtherLayers);
            DrawSpecialTarget("Self", PlayableInterruptionTarget.Self);
            DrawDivider();
            DrawLayers();
            GUILayout.Space(4f);
            EditorGUILayout.EndScrollView();

            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape)
            {
                editorWindow.Close();
                Event.current.Use();
            }
        }

        private static void DrawHeader()
        {
            Rect headerRect = GUILayoutUtility.GetRect(
                1f,
                28f,
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, HeaderBackground);
            EditorGUI.DrawRect(
                new Rect(headerRect.x, headerRect.yMax - 1f, headerRect.width, 1f),
                Accent);

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = Accent;
            EditorGUI.LabelField(
                new Rect(
                    headerRect.x + 9f,
                    headerRect.y + 4f,
                    headerRect.width - 18f,
                    EditorGUIUtility.singleLineHeight),
                "Interrupts",
                style);
        }

        private void DrawSpecialTarget(
            string label,
            PlayableInterruptionTarget target)
        {
            Rect row = EditorGUILayout.GetControlRect(false, RowHeight);
            Rect toggleRect = new Rect(
                row.x + 8f,
                row.y + 1f,
                18f,
                EditorGUIUtility.singleLineHeight);
            Rect labelRect = new Rect(
                toggleRect.xMax + 5f,
                row.y + 1f,
                row.width - 40f,
                EditorGUIUtility.singleLineHeight);

            bool selected = HasTarget(target, string.Empty, string.Empty);
            bool nextSelected = EditorGUI.Toggle(toggleRect, selected);
            EditorGUI.LabelField(labelRect, label);
            if (nextSelected != selected)
                SetSpecialTarget(target, nextSelected);
        }

        private static void DrawDivider()
        {
            GUILayout.Space(3f);
            Rect divider = GUILayoutUtility.GetRect(
                1f,
                1f,
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(divider, new Color(1f, 1f, 1f, 0.16f));
            GUILayout.Space(3f);
        }

        private void DrawLayers()
        {
            if (graph.layers == null || graph.layers.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "No layers available.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (int i = 0; i < graph.layers.Count; i++)
            {
                PlayableLayer layer = graph.layers[i];
                if (layer == null)
                    continue;

                string branchKey = "layer:" + i;
                int total = CountSelectableLeaves(layer.states);
                int selected = CountSelectedLeaves(
                    layer.name,
                    layer.states,
                    string.Empty);
                string label = i + " - " +
                    (string.IsNullOrWhiteSpace(layer.name)
                        ? "Layer"
                        : layer.name);

                bool expanded = DrawBranchRow(
                    branchKey,
                    label,
                    0,
                    selected,
                    total,
                    next => SetBranchTargets(
                        layer.name,
                        layer.states,
                        string.Empty,
                        next));

                if (expanded)
                {
                    DrawStateBranches(
                        layer.name,
                        layer.states,
                        string.Empty,
                        branchKey,
                        1);
                }
            }
        }

        private void DrawStateBranches(
            string layerName,
            List<PlayableState> states,
            string parentStatePath,
            string parentBranchKey,
            int depth)
        {
            if (states == null)
                return;

            for (int i = 0; i < states.Count; i++)
            {
                PlayableState state = states[i];
                if (state == null || state == ownerState)
                    continue;

                string displayName = state.DisplayName;
                string statePath = string.IsNullOrWhiteSpace(parentStatePath)
                    ? displayName
                    : parentStatePath + "/" + displayName;

                if (!state.IsSubStateMachine)
                {
                    DrawLeafRow(layerName, statePath, displayName, depth);
                    continue;
                }

                int total = CountSelectableLeaves(state.subStates);
                if (total == 0)
                    continue;

                int selected = CountSelectedLeaves(
                    layerName,
                    state.subStates,
                    statePath);
                string branchKey = parentBranchKey + "/" + i;
                bool expanded = DrawBranchRow(
                    branchKey,
                    displayName,
                    depth,
                    selected,
                    total,
                    next => SetBranchTargets(
                        layerName,
                        state.subStates,
                        statePath,
                        next));

                if (expanded)
                {
                    DrawStateBranches(
                        layerName,
                        state.subStates,
                        statePath,
                        branchKey,
                        depth + 1);
                }
            }
        }

        private bool DrawBranchRow(
            string branchKey,
            string label,
            int depth,
            int selected,
            int total,
            Action<bool> setSelected)
        {
            Rect row = EditorGUILayout.GetControlRect(false, RowHeight);
            float indent = depth * IndentWidth;
            Rect foldoutRect = new Rect(
                row.x + indent,
                row.y + 1f,
                16f,
                EditorGUIUtility.singleLineHeight);
            Rect toggleRect = new Rect(
                foldoutRect.xMax + 1f,
                row.y + 1f,
                18f,
                EditorGUIUtility.singleLineHeight);
            Rect countRect = new Rect(
                row.xMax - CountWidth,
                row.y + 1f,
                CountWidth - 4f,
                EditorGUIUtility.singleLineHeight);
            Rect labelRect = new Rect(
                toggleRect.xMax + 4f,
                row.y + 1f,
                Mathf.Max(20f, countRect.x - toggleRect.xMax - 8f),
                EditorGUIUtility.singleLineHeight);

            bool expanded = expandedBranches.Contains(branchKey);
            bool nextExpanded = EditorGUI.Foldout(
                foldoutRect,
                expanded,
                GUIContent.none,
                false);
            if (nextExpanded != expanded)
            {
                if (nextExpanded)
                    expandedBranches.Add(branchKey);
                else
                    expandedBranches.Remove(branchKey);
            }

            bool allSelected = total > 0 && selected == total;
            EditorGUI.showMixedValue = selected > 0 && !allSelected;
            EditorGUI.BeginDisabledGroup(total == 0);
            EditorGUI.BeginChangeCheck();
            bool nextSelected = EditorGUI.Toggle(toggleRect, allSelected);
            if (EditorGUI.EndChangeCheck())
                setSelected(nextSelected);
            EditorGUI.EndDisabledGroup();
            EditorGUI.showMixedValue = false;

            EditorGUI.LabelField(labelRect, label);
            GUIStyle countStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            if (selected > 0)
                countStyle.normal.textColor = Accent;
            EditorGUI.LabelField(
                countRect,
                selected + "/" + total,
                countStyle);
            return nextExpanded;
        }

        private void DrawLeafRow(
            string layerName,
            string statePath,
            string label,
            int depth)
        {
            Rect row = EditorGUILayout.GetControlRect(false, RowHeight);
            float indent = depth * IndentWidth;
            Rect toggleRect = new Rect(
                row.x + indent + 17f,
                row.y + 1f,
                18f,
                EditorGUIUtility.singleLineHeight);
            Rect labelRect = new Rect(
                toggleRect.xMax + 4f,
                row.y + 1f,
                row.xMax - toggleRect.xMax - 8f,
                EditorGUIUtility.singleLineHeight);

            bool selected = HasTarget(
                PlayableInterruptionTarget.State,
                layerName,
                statePath);
            bool nextSelected = EditorGUI.Toggle(toggleRect, selected);
            EditorGUI.LabelField(labelRect, label);
            if (nextSelected != selected)
                SetStateTarget(layerName, statePath, nextSelected);
        }

        private int CountSelectableLeaves(List<PlayableState> states)
        {
            if (states == null)
                return 0;

            int count = 0;
            for (int i = 0; i < states.Count; i++)
            {
                PlayableState state = states[i];
                if (state == null || state == ownerState)
                    continue;

                count += state.IsSubStateMachine
                    ? CountSelectableLeaves(state.subStates)
                    : 1;
            }

            return count;
        }

        private int CountSelectedLeaves(
            string layerName,
            List<PlayableState> states,
            string parentStatePath)
        {
            if (states == null)
                return 0;

            int count = 0;
            for (int i = 0; i < states.Count; i++)
            {
                PlayableState state = states[i];
                if (state == null || state == ownerState)
                    continue;

                string displayName = state.DisplayName;
                string statePath = string.IsNullOrWhiteSpace(parentStatePath)
                    ? displayName
                    : parentStatePath + "/" + displayName;
                if (state.IsSubStateMachine)
                {
                    count += CountSelectedLeaves(
                        layerName,
                        state.subStates,
                        statePath);
                }
                else if (HasTarget(
                             PlayableInterruptionTarget.State,
                             layerName,
                             statePath))
                {
                    count++;
                }
            }

            return count;
        }

        private void SetSpecialTarget(
            PlayableInterruptionTarget target,
            bool selected)
        {
            ChangeTargets(() =>
            {
                if (selected)
                {
                    AddTarget(target, string.Empty, string.Empty);
                    if (target ==
                        PlayableInterruptionTarget.AllStatesFromOtherLayers)
                    {
                        ownerState.interruptOtherLayers = true;
                    }
                }
                else
                {
                    RemoveTarget(target, string.Empty, string.Empty);
                }
            });
        }

        private void SetStateTarget(
            string layerName,
            string statePath,
            bool selected)
        {
            ChangeTargets(() =>
            {
                if (selected)
                {
                    AddTarget(
                        PlayableInterruptionTarget.State,
                        layerName,
                        statePath);
                }
                else
                {
                    RemoveTarget(
                        PlayableInterruptionTarget.State,
                        layerName,
                        statePath);
                }
            });
        }

        private void SetBranchTargets(
            string layerName,
            List<PlayableState> states,
            string parentStatePath,
            bool selected)
        {
            ChangeTargets(() => SetBranchTargetsWithoutUndo(
                layerName,
                states,
                parentStatePath,
                selected));
        }

        private void SetBranchTargetsWithoutUndo(
            string layerName,
            List<PlayableState> states,
            string parentStatePath,
            bool selected)
        {
            if (states == null)
                return;

            for (int i = 0; i < states.Count; i++)
            {
                PlayableState state = states[i];
                if (state == null || state == ownerState)
                    continue;

                string displayName = state.DisplayName;
                string statePath = string.IsNullOrWhiteSpace(parentStatePath)
                    ? displayName
                    : parentStatePath + "/" + displayName;
                if (state.IsSubStateMachine)
                {
                    SetBranchTargetsWithoutUndo(
                        layerName,
                        state.subStates,
                        statePath,
                        selected);
                }
                else if (selected)
                {
                    AddTarget(
                        PlayableInterruptionTarget.State,
                        layerName,
                        statePath);
                }
                else
                {
                    RemoveTarget(
                        PlayableInterruptionTarget.State,
                        layerName,
                        statePath);
                }
            }
        }

        private void ChangeTargets(Action change)
        {
            if (ownerState.interruptions == null)
                ownerState.interruptions = new List<PlayableInterruption>();

            Undo.RecordObject(graph, "Change Interruption Targets");
            change();
            EditorUtility.SetDirty(graph);
            changed?.Invoke();
            editorWindow.Repaint();
        }

        private void AddTarget(
            PlayableInterruptionTarget target,
            string layerName,
            string stateName)
        {
            if (HasTarget(target, layerName, stateName))
                return;

            ownerState.interruptions.Add(new PlayableInterruption
            {
                target = target,
                layerName = layerName,
                stateName = stateName
            });
        }

        private void RemoveTarget(
            PlayableInterruptionTarget target,
            string layerName,
            string stateName)
        {
            if (ownerState.interruptions == null)
                return;

            for (int i = ownerState.interruptions.Count - 1; i >= 0; i--)
            {
                PlayableInterruption rule = ownerState.interruptions[i];
                if (rule != null &&
                    rule.target == target &&
                    string.Equals(rule.layerName, layerName) &&
                    string.Equals(rule.stateName, stateName))
                {
                    ownerState.interruptions.RemoveAt(i);
                }
            }
        }

        private bool HasTarget(
            PlayableInterruptionTarget target,
            string layerName,
            string stateName)
        {
            if (ownerState.interruptions == null)
                return false;

            for (int i = 0; i < ownerState.interruptions.Count; i++)
            {
                PlayableInterruption rule = ownerState.interruptions[i];
                if (rule != null &&
                    rule.target == target &&
                    string.Equals(rule.layerName, layerName) &&
                    string.Equals(rule.stateName, stateName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
