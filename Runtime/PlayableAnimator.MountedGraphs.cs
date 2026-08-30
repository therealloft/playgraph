using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
        private void InitializeMountedGraphMixer(AnimationPlayableOutput output)
        {
            animationOutput = output;
            mountedLayerMixer = AnimationLayerMixerPlayable.Create(
                playableGraph,
                1);
            playableGraph.Connect(
                layerMixer,
                0,
                mountedLayerMixer,
                0);
            mountedLayerMixer.SetInputWeight(0, 1f);
            animationOutput.SetSourcePlayable(mountedLayerMixer);
        }

        public int MountGraph(
            PlayableAnimatorGraph graph,
            float fadeDuration = 0.15f,
            bool playDefaultStates = true)
        {
            return MountGraphInternal(
                graph,
                fadeDuration,
                false,
                playDefaultStates);
        }

        public int MountClip(PlayableExternalClipSettings settings)
        {
            if (settings == null || settings.clip == null)
                return 0;

            PlayableAnimatorGraph graph =
                ScriptableObject.CreateInstance<PlayableAnimatorGraph>();
            graph.name = string.IsNullOrWhiteSpace(settings.name)
                ? settings.clip.name
                : settings.name;
            graph.hideFlags = HideFlags.HideAndDontSave;
            graph.defaultFadeDuration = Mathf.Max(0f, settings.fadeIn);
            graph.showInPlayableGraphVisualizer = false;

            PlayableState state = new PlayableState
            {
                name = graph.name,
                enabled = true,
                isDefault = true,
                manualOnly = false,
                output = PlayableStateOutput.OneShot,
                clip = settings.clip,
                loop = false,
                speed = Mathf.Max(0.01f, settings.speed),
                applyFootIK = settings.applyFootIK,
                applyRootMotion = settings.applyRootMotion,
                rootMotionPositionXZ = settings.rootMotionPositionXZ,
                rootMotionPositionY = settings.rootMotionPositionY,
                rootMotionRotation = settings.rootMotionRotation,
                fadeDuration = Mathf.Max(0f, settings.fadeIn)
            };
            PlayableLayer layer = new PlayableLayer
            {
                name = graph.name,
                avatarMask = settings.avatarMask,
                additive = settings.additive,
                weight = Mathf.Clamp01(settings.layerWeight),
                states = new List<PlayableState>
            {
                state
            }
            };
            graph.layers.Add(layer);

            return MountGraphInternal(graph, settings.fadeIn, true, true);
        }

        public bool UnmountGraph(int handle, float fadeDuration = 0.15f)
        {
            MountedGraph mounted = FindMountedGraph(handle);
            if (mounted == null)
                return false;

            mounted.TargetWeight = 0f;
            mounted.FadeDuration = Mathf.Max(0f, fadeDuration);
            mounted.RemoveWhenFaded = true;
            if (mounted.FadeDuration <= 0.0001f)
                RemoveMountedGraph(mounted);

            return true;
        }

        public bool IsGraphMounted(int handle)
        {
            return FindMountedGraph(handle) != null;
        }

        public bool PlayMountedState(
            int handle,
            string stateName,
            string layerName = null)
        {
            if (!TryFindMountedState(
                    handle,
                    stateName,
                    layerName,
                    out RuntimeLayer layer,
                    out RuntimeState state))
            {
                return false;
            }

            layer.ManualState = state;
            EnterState(layer, state);
            return true;
        }

        public bool SetMountedReturnState(
            int handle,
            string stateName,
            string layerName = null)
        {
            if (!TryFindMountedState(
                    handle,
                    stateName,
                    layerName,
                    out RuntimeLayer layer,
                    out RuntimeState state))
            {
                return false;
            }

            layer.ManualState = state;
            return true;
        }

        public bool ClearMountedReturnState(
            int handle,
            string layerName = null)
        {
            RuntimeLayer layer = FindMountedLayer(handle, layerName);
            if (layer == null)
                return false;

            layer.ManualState = null;
            return true;
        }

        public bool TriggerMountedOneShot(
            int handle,
            string stateName,
            string layerName = null)
        {
            if (!TryFindMountedState(
                    handle,
                    stateName,
                    layerName,
                    out RuntimeLayer layer,
                    out RuntimeState state))
            {
                return false;
            }

            layer.OneShotState = state;
            EnterState(layer, state);
            return true;
        }

        public bool TryGetMountedLayerState(
            int handle,
            string requestedLayerName,
            out string layerName,
            out string statePath,
            out float normalizedTime)
        {
            layerName = string.Empty;
            statePath = string.Empty;
            normalizedTime = 0f;

            RuntimeLayer layer = FindMountedLayer(handle, requestedLayerName);
            if (layer == null)
                return false;

            layerName = layer.Definition != null
                ? layer.Definition.name
                : string.Empty;
            if (layer.ActiveState == null)
                return true;

            statePath = layer.ActiveState.Path;
            normalizedTime = GetNormalizedStateTime(layer.ActiveState);
            return true;
        }

        public bool IsMountedStateComplete(
            int handle,
            string stateName,
            string layerName = null)
        {
            return TryFindMountedState(
                       handle,
                       stateName,
                       layerName,
                       out _,
                       out RuntimeState state) &&
                   IsStateComplete(state);
        }

        public bool IsMountedGraphComplete(int handle)
        {
            MountedGraph mounted = FindMountedGraph(handle);
            if (mounted == null || mounted.Layers.Count == 0)
                return false;

            bool foundState = false;
            for (int i = 0; i < mounted.Layers.Count; i++)
            {
                RuntimeState state = mounted.Layers[i].ActiveState;
                if (state == null)
                    continue;

                foundState = true;
                if (!IsStateComplete(state))
                    return false;
            }

            return foundState;
        }

        private int MountGraphInternal(
            PlayableAnimatorGraph graph,
            float fadeDuration,
            bool ownsGraph,
            bool playDefaultStates)
        {
            if (graph == null)
                return 0;

            if (!playableGraph.IsValid())
                Initialize();
            if (!playableGraph.IsValid() || !mountedLayerMixer.IsValid())
            {
                if (ownsGraph)
                    DestroyTransientGraph(graph);
                return 0;
            }

            graph.EnsureDefaults();
            if (graph.layers == null || graph.layers.Count == 0)
            {
                if (ownsGraph)
                    DestroyTransientGraph(graph);
                return 0;
            }

            parameterStore.AddDefinitions(graph);
            MountedGraph mounted = new MountedGraph
            {
                Handle = AllocateMountHandle(),
                Graph = graph,
                OwnsGraph = ownsGraph,
                CurrentWeight = fadeDuration <= 0.0001f ? 1f : 0f,
                TargetWeight = 1f,
                FadeDuration = Mathf.Max(0f, fadeDuration)
            };

            mountedGraphs.Add(mounted);
            for (int i = 0; i < graph.layers.Count; i++)
            {
                RuntimeLayer layer = BuildMountedLayer(
                    graph.layers[i],
                    graph,
                    mounted.Handle,
                    playDefaultStates);
                if (layer != null)
                    mounted.Layers.Add(layer);
            }

            if (mounted.Layers.Count == 0)
            {
                mountedGraphs.Remove(mounted);
                if (ownsGraph)
                    DestroyTransientGraph(graph);
                return 0;
            }

            RebuildMountedLayerMixer();
            if (animator != null)
                animator.applyRootMotion = GraphUsesRootMotion();

            return mounted.Handle;
        }

        private RuntimeLayer BuildMountedLayer(
            PlayableLayer definition,
            PlayableAnimatorGraph sourceGraph,
            int mountHandle,
            bool playDefaultState)
        {
            RuntimeLayer runtimeLayer = graphBuilder?.BuildLayer(
                definition,
                sourceGraph,
                mountHandle);
            if (runtimeLayer == null)
                return null;

            runtimeLayers.Add(runtimeLayer);

            RuntimeState defaultState = FindDefaultState(runtimeLayer);
            if (playDefaultState && defaultState != null)
                EnterState(runtimeLayer, defaultState, true, false);

            return runtimeLayer;
        }

        private void UpdateMountedGraphs(float deltaTime)
        {
            for (int i = mountedGraphs.Count - 1; i >= 0; i--)
            {
                MountedGraph mounted = mountedGraphs[i];
                mounted.CurrentWeight = mounted.FadeDuration <= 0.0001f
                    ? mounted.TargetWeight
                    : Mathf.MoveTowards(
                        mounted.CurrentWeight,
                        mounted.TargetWeight,
                        deltaTime / mounted.FadeDuration);

                ApplyMountedGraphWeights(mounted);
                if (mounted.RemoveWhenFaded && mounted.CurrentWeight <= 0.0001f)
                    RemoveMountedGraph(mounted);
            }
        }

        private void ApplyMountedGraphWeights(MountedGraph mounted)
        {
            if (mounted == null || !mountedLayerMixer.IsValid())
                return;

            for (int i = 0; i < mounted.Layers.Count; i++)
            {
                RuntimeLayer layer = mounted.Layers[i];
                if (layer.MixerInputIndex < 0 ||
                    layer.MixerInputIndex >= mountedLayerMixer.GetInputCount())
                {
                    continue;
                }

                layer.Weight = layer.LocalWeight * mounted.CurrentWeight;
                mountedLayerMixer.SetInputWeight(
                    layer.MixerInputIndex,
                    layer.Weight);
            }
        }

        private void RemoveMountedGraph(MountedGraph mounted)
        {
            if (mounted == null || !mountedGraphs.Remove(mounted))
                return;

            for (int i = 0; i < mounted.Layers.Count; i++)
            {
                RuntimeLayer layer = mounted.Layers[i];
                if (layer.ActiveState != null)
                    NotifyStateExit(layer, layer.ActiveState);
                runtimeLayers.Remove(layer);
            }

            RebuildMountedLayerMixer();

            for (int i = 0; i < mounted.Layers.Count; i++)
            {
                RuntimeLayer layer = mounted.Layers[i];
                if (playableGraph.IsValid() && layer.StateMixer.IsValid())
                    playableGraph.DestroySubgraph(layer.StateMixer);
            }

            if (mounted.OwnsGraph)
                DestroyTransientGraph(mounted.Graph);

            if (animator != null)
                animator.applyRootMotion = GraphUsesRootMotion();
        }

        private void RebuildMountedLayerMixer()
        {
            if (!playableGraph.IsValid())
                return;

            AnimationLayerMixerPlayable previous = mountedLayerMixer;
            if (previous.IsValid())
            {
                int previousInputCount = previous.GetInputCount();
                for (int i = 0; i < previousInputCount; i++)
                    playableGraph.Disconnect(previous, i);
            }

            int inputCount = 1;
            for (int i = 0; i < mountedGraphs.Count; i++)
                inputCount += mountedGraphs[i].Layers.Count;

            mountedLayerMixer = AnimationLayerMixerPlayable.Create(
                playableGraph,
                inputCount);
            playableGraph.Connect(layerMixer, 0, mountedLayerMixer, 0);
            mountedLayerMixer.SetInputWeight(0, 1f);

            int inputIndex = 1;
            for (int i = 0; i < mountedGraphs.Count; i++)
            {
                MountedGraph mounted = mountedGraphs[i];
                for (int j = 0; j < mounted.Layers.Count; j++)
                {
                    RuntimeLayer layer = mounted.Layers[j];
                    layer.MixerInputIndex = inputIndex;
                    playableGraph.Connect(
                        layer.StateMixer,
                        0,
                        mountedLayerMixer,
                        inputIndex);
                    mountedLayerMixer.SetLayerAdditive(
                        (uint)inputIndex,
                        layer.Definition.additive);
                    if (layer.Definition.avatarMask != null)
                    {
                        mountedLayerMixer.SetLayerMaskFromAvatarMask(
                            (uint)inputIndex,
                            layer.Definition.avatarMask);
                    }

                    inputIndex++;
                }

                ApplyMountedGraphWeights(mounted);
            }

            animationOutput.SetSourcePlayable(mountedLayerMixer);
            if (previous.IsValid())
                playableGraph.DestroyPlayable(previous);
        }

        private PlayableParameter FindParameterDefinition(string parameterName)
        {
            PlayableParameter definition = graphAsset != null
                ? graphAsset.FindParameter(parameterName)
                : null;
            if (definition != null)
                return definition;

            for (int i = mountedGraphs.Count - 1; i >= 0; i--)
            {
                PlayableAnimatorGraph graph = mountedGraphs[i].Graph;
                definition = graph != null
                    ? graph.FindParameter(parameterName)
                    : null;
                if (definition != null)
                    return definition;
            }

            return null;
        }

        private RuntimeLayer FindMountedLayer(int handle, string layerName)
        {
            MountedGraph mounted = FindMountedGraph(handle);
            if (mounted == null || mounted.Layers.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(layerName))
                return mounted.Layers[0];

            for (int i = 0; i < mounted.Layers.Count; i++)
            {
                RuntimeLayer layer = mounted.Layers[i];
                if (layer.Definition != null &&
                    string.Equals(
                        layer.Definition.name,
                        layerName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return layer;
                }
            }

            return null;
        }

        private bool TryFindMountedState(
            int handle,
            string stateName,
            string layerName,
            out RuntimeLayer layer,
            out RuntimeState state)
        {
            layer = FindMountedLayer(handle, layerName);
            state = null;
            if (layer == null || string.IsNullOrWhiteSpace(stateName))
                return false;

            for (int i = 0; i < layer.States.Count; i++)
            {
                RuntimeState candidate = layer.States[i];
                if (string.Equals(
                        candidate.Path,
                        stateName,
                        StringComparison.OrdinalIgnoreCase) ||
                    (candidate.Definition != null &&
                     string.Equals(
                         candidate.Definition.name,
                         stateName,
                         StringComparison.OrdinalIgnoreCase)))
                {
                    state = candidate;
                    return true;
                }
            }

            return false;
        }

        private MountedGraph FindMountedGraph(int handle)
        {
            if (handle <= 0)
                return null;

            for (int i = 0; i < mountedGraphs.Count; i++)
            {
                if (mountedGraphs[i].Handle == handle)
                    return mountedGraphs[i];
            }

            return null;
        }

        private int AllocateMountHandle()
        {
            do
            {
                nextMountHandle = unchecked(nextMountHandle + 1);
                if (nextMountHandle <= 0)
                    nextMountHandle = 1;
            }
            while (FindMountedGraph(nextMountHandle) != null);

            return nextMountHandle;
        }

        private void DestroyMountedGraphAssets()
        {
            for (int i = 0; i < mountedGraphs.Count; i++)
            {
                if (mountedGraphs[i].OwnsGraph)
                    DestroyTransientGraph(mountedGraphs[i].Graph);
            }

            mountedGraphs.Clear();
            mountedLayerMixer = default;
            animationOutput = default;
        }

        private static void DestroyTransientGraph(PlayableAnimatorGraph graph)
        {
            if (graph == null)
                return;

            if (Application.isPlaying)
                Destroy(graph);
            else
                DestroyImmediate(graph);
        }
    }
}
