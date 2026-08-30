using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    internal sealed class MountedGraph
    {
        public int Handle;
        public PlayableAnimatorGraph Graph;
        public readonly List<RuntimeLayer> Layers =
            new List<RuntimeLayer>();
        public bool OwnsGraph;
        public bool RemoveWhenFaded;
        public float CurrentWeight;
        public float TargetWeight;
        public float FadeDuration;
    }

    internal sealed class RuntimeLayer
    {
        public readonly PlayableLayer Definition;
        public readonly PlayableAnimatorGraph SourceGraph;
        public readonly int MountHandle;
        public readonly List<RuntimeState> States = new List<RuntimeState>();
        public readonly Dictionary<PlayableState, RuntimeState> StateLookup =
            new Dictionary<PlayableState, RuntimeState>();
        public AnimationMixerPlayable StateMixer;
        public RuntimeState ActiveState;
        public RuntimeState ManualState;
        public RuntimeState OneShotState;
        public float ActiveStateStartTime;
        public float LocalWeight;
        public float Weight;
        public int MixerInputIndex = -1;

        public RuntimeLayer(
            PlayableLayer definition,
            PlayableAnimatorGraph sourceGraph,
            int mountHandle)
        {
            Definition = definition;
            SourceGraph = sourceGraph;
            MountHandle = mountHandle;
            LocalWeight = definition != null
                ? Mathf.Clamp01(definition.weight)
                : 0f;
            Weight = LocalWeight;
        }
    }

    internal sealed class RuntimeState
    {
        public readonly PlayableState Definition;
        public readonly int InputIndex;
        public readonly string Path;
        public readonly float DefaultFadeDuration;
        public readonly List<RuntimeMotion> Motions =
            new List<RuntimeMotion>();
        public Playable Playable;
        public AnimationMixerPlayable MotionMixer;
        public float[] BlendWeights = Array.Empty<float>();
        public bool HasMotionMixer;
        public int PlaylistMotionIndex = -1;
        public int PlaylistCycle = -1;
        public float ElapsedTime;
        public float PreviousNormalizedTime;
        public float FadeDurationOverride = -1f;
        public bool[] EventFired = Array.Empty<bool>();
        public float Weight;

        public RuntimeState(
            PlayableState definition,
            int inputIndex,
            string path,
            float defaultFadeDuration)
        {
            Definition = definition;
            InputIndex = inputIndex;
            Path = path;
            DefaultFadeDuration = defaultFadeDuration;
        }
    }

    internal sealed class RuntimeMotion
    {
        public readonly PlayableMotion Definition;
        public Playable Playable;
        public bool Connected;
        public float Weight;

        public RuntimeMotion(PlayableMotion definition)
        {
            Definition = definition;
        }
    }
}
