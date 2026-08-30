using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    /// <summary>
    /// Constructs runtime layers, states, and Playables from graph definitions.
    /// </summary>
    internal sealed class PlayableRuntimeGraphBuilder
    {
        private readonly PlayableGraph graph;

        public PlayableRuntimeGraphBuilder(PlayableGraph graph)
        {
            this.graph = graph;
        }

        public RuntimeLayer BuildLayer(
            PlayableLayer definition,
            PlayableAnimatorGraph sourceGraph,
            int mountHandle)
        {
            if (definition == null)
                return null;

            RuntimeLayer layer = new RuntimeLayer(
                definition,
                sourceGraph,
                mountHandle);
            int stateCount = Mathf.Max(1, CountLeafStates(definition.states));
            layer.StateMixer = AnimationMixerPlayable.Create(
                graph,
                stateCount);
            BuildLeafStates(layer, definition.states, string.Empty);
            return layer;
        }

        private RuntimeState BuildState(
            PlayableState definition,
            int stateIndex,
            string statePath,
            PlayableAnimatorGraph sourceGraph)
        {
            RuntimeState state = new RuntimeState(
                definition,
                stateIndex,
                statePath,
                definition != null && sourceGraph != null
                    ? sourceGraph.defaultFadeDuration
                    : 0f);

            if (definition == null)
            {
                state.Playable = AnimationMixerPlayable.Create(graph, 0);
                return state;
            }

            switch (definition.output)
            {
                case PlayableStateOutput.Playlist:
                    BuildPlaylistState(state);
                    break;
                case PlayableStateOutput.BlendTree1D:
                case PlayableStateOutput.BlendTree2D:
                case PlayableStateOutput.DirectBlend:
                    BuildBlendState(state);
                    break;
                default:
                    BuildClipState(state);
                    break;
            }

            return state;
        }

        private void BuildLeafStates(
            RuntimeLayer layer,
            List<PlayableState> definitions,
            string parentPath)
        {
            if (layer == null || definitions == null)
                return;

            for (int i = 0; i < definitions.Count; i++)
            {
                PlayableState definition = definitions[i];
                if (definition == null)
                    continue;

                string path = CombineStatePath(parentPath, definition.DisplayName);
                if (definition.IsSubStateMachine)
                {
                    BuildLeafStates(layer, definition.subStates, path);
                    continue;
                }

                int inputIndex = layer.States.Count;
                RuntimeState state = BuildState(
                    definition,
                    inputIndex,
                    path,
                    layer.SourceGraph);
                layer.States.Add(state);
                layer.StateLookup[definition] = state;

                if (state.Playable.IsValid())
                {
                    graph.Connect(
                        state.Playable,
                        0,
                        layer.StateMixer,
                        inputIndex);
                }

                layer.StateMixer.SetInputWeight(inputIndex, 0f);
            }
        }

        private static int CountLeafStates(List<PlayableState> states)
        {
            if (states == null)
                return 0;

            int count = 0;
            for (int i = 0; i < states.Count; i++)
            {
                PlayableState state = states[i];
                if (state == null)
                    continue;

                count += state.IsSubStateMachine
                    ? CountLeafStates(state.subStates)
                    : 1;
            }

            return count;
        }

        private static string CombineStatePath(string parentPath, string stateName)
        {
            return string.IsNullOrWhiteSpace(parentPath)
                ? stateName
                : parentPath + "/" + stateName;
        }

        private void BuildClipState(RuntimeState state)
        {
            PlayableState definition = state.Definition;
            if (definition.clip == null)
            {
                state.Playable = AnimationMixerPlayable.Create(graph, 0);
                return;
            }

            AnimationClipPlayable clipPlayable =
                AnimationClipPlayable.Create(graph, definition.clip);
            clipPlayable.SetApplyFootIK(definition.applyFootIK);
            clipPlayable.SetSpeed(Mathf.Max(0.01f, definition.speed));
            state.Playable = clipPlayable;
        }

        private void BuildBlendState(RuntimeState state)
        {
            List<PlayableMotion> motions = state.Definition.motions;
            int inputCount = motions != null ? Mathf.Max(1, motions.Count) : 1;
            state.MotionMixer = AnimationMixerPlayable.Create(
                graph,
                inputCount);
            state.HasMotionMixer = true;
            state.Playable = state.MotionMixer;
            state.BlendWeights = new float[inputCount];

            if (motions == null)
                return;

            for (int i = 0; i < motions.Count; i++)
            {
                PlayableMotion motion = motions[i];
                if (motion == null)
                {
                    motion = new PlayableMotion
                    {
                        name = "(none)",
                        enabled = false
                    };
                    motions[i] = motion;
                }

                RuntimeMotion runtimeMotion = new RuntimeMotion(motion);
                state.Motions.Add(runtimeMotion);

                if (!motion.enabled || motion.clip == null)
                {
                    continue;
                }

                AnimationClipPlayable clipPlayable =
                    AnimationClipPlayable.Create(graph, motion.clip);
                clipPlayable.SetApplyFootIK(motion.applyFootIK);
                clipPlayable.SetSpeed(Mathf.Max(0.01f, motion.speed));
                clipPlayable.SetTime(
                    PlayableAnimationTime.GetCycleOffsetTime(motion.clip, motion.cycleOffset));

                graph.Connect(
                    clipPlayable,
                    0,
                    state.MotionMixer,
                    i);
                state.MotionMixer.SetInputWeight(i, 0f);
                runtimeMotion.Connected = true;
                runtimeMotion.Playable = clipPlayable;
            }
        }

        private void BuildPlaylistState(RuntimeState state)
        {
            List<PlayableMotion> motions = state.Definition.motions;
            int inputCount = motions != null ? Mathf.Max(1, motions.Count) : 1;
            state.MotionMixer = AnimationMixerPlayable.Create(
                graph,
                inputCount);
            state.HasMotionMixer = true;
            state.Playable = state.MotionMixer;
            state.BlendWeights = new float[inputCount];

            if (motions == null)
                return;

            float stateSpeed = Mathf.Max(0.01f, state.Definition.speed);
            for (int i = 0; i < motions.Count; i++)
            {
                PlayableMotion motion = motions[i];
                if (motion == null)
                {
                    motion = new PlayableMotion
                    {
                        name = "(none)",
                        enabled = false
                    };
                    motions[i] = motion;
                }

                RuntimeMotion runtimeMotion = new RuntimeMotion(motion);
                state.Motions.Add(runtimeMotion);
                if (!motion.enabled || motion.clip == null)
                    continue;

                AnimationClipPlayable clipPlayable =
                    AnimationClipPlayable.Create(graph, motion.clip);
                clipPlayable.SetApplyFootIK(motion.applyFootIK);
                clipPlayable.SetSpeed(
                    stateSpeed * Mathf.Max(0.01f, motion.speed));

                graph.Connect(
                    clipPlayable,
                    0,
                    state.MotionMixer,
                    i);
                state.MotionMixer.SetInputWeight(i, 0f);
                runtimeMotion.Connected = true;
                runtimeMotion.Playable = clipPlayable;
            }
        }
    }
}
