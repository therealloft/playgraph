using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
        private RuntimeLayer FindRuntimeLayer(string layerName)
        {
            if (runtimeLayers.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(layerName))
                return runtimeLayers[0];

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                if (string.Equals(
                        runtimeLayers[i].Definition.name,
                        layerName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return runtimeLayers[i];
                }
            }

            return null;
        }

        private bool SetRuntimeLayerWeight(RuntimeLayer layer, float weight)
        {
            if (layer == null)
                return false;

            layer.LocalWeight = Mathf.Clamp01(weight);
            if (layer.MountHandle == 0)
            {
                layer.Weight = layer.LocalWeight;
                if (layerMixer.IsValid() &&
                    layer.MixerInputIndex >= 0 &&
                    layer.MixerInputIndex < layerMixer.GetInputCount())
                {
                    layerMixer.SetInputWeight(
                        layer.MixerInputIndex,
                        layer.Weight);
                }

                return true;
            }

            MountedGraph mounted = FindMountedGraph(layer.MountHandle);
            if (mounted == null)
                return false;

            layer.Weight = layer.LocalWeight * mounted.CurrentWeight;
            if (mountedLayerMixer.IsValid() &&
                layer.MixerInputIndex >= 0 &&
                layer.MixerInputIndex < mountedLayerMixer.GetInputCount())
            {
                mountedLayerMixer.SetInputWeight(
                    layer.MixerInputIndex,
                    layer.Weight);
            }

            return true;
        }

        private bool TryFindRuntimeState(
            string stateName,
            string layerName,
            out RuntimeLayer layer,
            out RuntimeState state)
        {
            layer = FindRuntimeLayer(layerName);
            state = null;

            if (layer == null || string.IsNullOrWhiteSpace(stateName))
                return false;

            for (int i = 0; i < layer.States.Count; i++)
            {
                RuntimeState candidate = layer.States[i];
                if (string.Equals(
                        candidate.Path,
                        stateName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    state = candidate;
                    return true;
                }
            }

            for (int i = 0; i < layer.States.Count; i++)
            {
                RuntimeState candidate = layer.States[i];
                if (candidate.Definition == null ||
                    !string.Equals(
                        candidate.Definition.name,
                        stateName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                state = candidate;
                return true;
            }

            return false;
        }

        private bool IsStateComplete(RuntimeState state)
        {
            if (state == null || state.Definition == null || state.Definition.loop)
                return false;

            float duration = GetCurrentStateDuration(state);
            return duration > 0.0001f && state.ElapsedTime >= duration;
        }

        private static float GetStateDuration(PlayableState state)
        {
            if (state.clip != null)
                return state.clip.length / Mathf.Max(0.01f, state.speed);

            if (state.motions == null)
                return 0f;

            float duration = 0f;
            bool playlist = state.output == PlayableStateOutput.Playlist;
            for (int i = 0; i < state.motions.Count; i++)
            {
                PlayableMotion motion = state.motions[i];
                if (motion == null ||
                    !motion.enabled ||
                    motion.clip == null)
                    continue;

                float motionDuration = motion.clip.length /
                    (Mathf.Max(0.01f, state.speed) *
                     Mathf.Max(0.01f, motion.speed));
                duration = playlist
                    ? duration + motionDuration
                    : Mathf.Max(duration, motionDuration);
            }

            return duration;
        }

        private static void ResetPlayableTime(Playable playable)
        {
            if (!playable.IsValid())
                return;

            playable.SetTime(0d);
        }

        private static void ResetMotionPlayableTimes(RuntimeState state)
        {
            if (state == null || !state.HasMotionMixer)
                return;

            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                if (motion == null || !motion.Playable.IsValid())
                    continue;

                motion.Playable.SetTime(
                    state.Definition.output == PlayableStateOutput.Playlist
                        ? 0d
                        : PlayableAnimationTime.GetCycleOffsetTime(
                            motion.Definition.clip,
                            motion.Definition.cycleOffset));
            }

            state.PlaylistMotionIndex = -1;
            state.PlaylistCycle = -1;
        }

    }
}
