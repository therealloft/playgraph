using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    internal sealed class PlayableMotionEvaluator
    {
        public void ApplyWeights(
            RuntimeState state,
            PlayableParameterStore parameters)
        {
            if (state == null || !state.HasMotionMixer)
                return;

            switch (state.Definition.output)
            {
                case PlayableStateOutput.Playlist:
                    ApplyPlaylist(state);
                    break;
                case PlayableStateOutput.BlendTree1D:
                    ApplyBlendTree1D(state, parameters);
                    break;
                case PlayableStateOutput.BlendTree2D:
                    ApplyBlendTree2D(state, parameters);
                    break;
                case PlayableStateOutput.DirectBlend:
                    ApplyDirectBlend(state, parameters);
                    break;
            }

            if (UsesSynchronizedBlendTime(state.Definition))
                UpdateSynchronizedBlendPlayback(state);
        }

        private void ApplyBlendTree1D(
            RuntimeState state,
            PlayableParameterStore parameters)
        {
            ClearMotionWeights(state);

            float value = parameters.GetFloat(state.Definition.blendParameterX);
            int lower = -1;
            int upper = -1;

            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                if (!motion.Connected)
                    continue;

                float threshold = motion.Definition.threshold;
                if (threshold <= value &&
                    (lower < 0 ||
                     threshold > state.Motions[lower].Definition.threshold))
                {
                    lower = i;
                }

                if (threshold >= value &&
                    (upper < 0 ||
                     threshold < state.Motions[upper].Definition.threshold))
                {
                    upper = i;
                }
            }

            if (lower < 0)
                lower = upper;
            if (upper < 0)
                upper = lower;

            if (lower < 0 || upper < 0)
            {
                ApplyFirstConnectedMotion(state);
                return;
            }

            if (lower == upper)
            {
                SetMotionWeight(state, lower, 1f);
                return;
            }

            float lowerThreshold = state.Motions[lower].Definition.threshold;
            float upperThreshold = state.Motions[upper].Definition.threshold;
            float t = Mathf.InverseLerp(lowerThreshold, upperThreshold, value);
            SetMotionWeight(state, lower, 1f - t);
            SetMotionWeight(state, upper, t);
        }

        private void ApplyBlendTree2D(
            RuntimeState state,
            PlayableParameterStore parameters)
        {
            ClearMotionWeights(state);

            Vector2 value = new Vector2(
                parameters.GetFloat(state.Definition.blendParameterX),
                parameters.GetFloat(state.Definition.blendParameterY));

            if (!PlayableBlendMath.Calculate2DWeights(
                    state.Definition.motions,
                    value,
                    state.BlendWeights,
                    state.Definition.blendTree2DType))
            {
                ApplyFirstConnectedMotion(state);
                return;
            }

            int count = Mathf.Min(state.Motions.Count, state.BlendWeights.Length);
            for (int i = 0; i < count; i++)
                SetMotionWeight(state, i, state.BlendWeights[i]);
        }

        private void ApplyDirectBlend(
            RuntimeState state,
            PlayableParameterStore parameters)
        {
            float total = 0f;
            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                motion.Weight = motion.Connected
                    ? Mathf.Clamp01(parameters.GetFloat(motion.Definition.directParameter))
                    : 0f;
                total += motion.Weight;
            }

            if (total <= 0.0001f)
            {
                ApplyFirstConnectedMotion(state);
                return;
            }

            float scale = total > 1f ? 1f / total : 1f;
            for (int i = 0; i < state.Motions.Count; i++)
                SetMotionWeight(state, i, state.Motions[i].Weight * scale);
        }

        public void ApplyPlaylist(RuntimeState state, bool forceSeek = false)
        {
            ClearMotionWeights(state);
            if (!TryGetPlaylistPosition(
                    state,
                    state.ElapsedTime,
                    out int motionIndex,
                    out int cycle,
                    out double clipTime))
            {
                return;
            }

            RuntimeMotion motion = state.Motions[motionIndex];
            SetMotionWeight(state, motionIndex, 1f);

            if (!forceSeek &&
                state.PlaylistMotionIndex == motionIndex &&
                state.PlaylistCycle == cycle)
            {
                return;
            }

            if (motion.Playable.IsValid())
                motion.Playable.SetTime(clipTime);

            state.PlaylistMotionIndex = motionIndex;
            state.PlaylistCycle = cycle;
        }

        private static bool TryGetPlaylistPosition(
            RuntimeState state,
            float elapsedTime,
            out int motionIndex,
            out int cycle,
            out double clipTime)
        {
            motionIndex = -1;
            cycle = 0;
            clipTime = 0d;
            float duration = GetPlaylistDuration(state);
            if (state == null || duration <= 0.0001f)
                return false;

            bool loop = state.Definition.loop;
            float timeline;
            if (loop)
            {
                float positiveTime = Mathf.Max(0f, elapsedTime);
                cycle = Mathf.FloorToInt(positiveTime / duration);
                timeline = Mathf.Repeat(positiveTime, duration);
            }
            else
            {
                timeline = Mathf.Clamp(elapsedTime, 0f, duration);
            }

            int lastConnected = -1;
            float cursor = 0f;
            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                if (!IsValidPlaylistMotion(motion))
                    continue;

                lastConnected = i;
                float speed = GetPlaylistMotionSpeed(state, motion);
                float motionDuration = motion.Definition.clip.length / speed;
                bool isSelected = timeline < cursor + motionDuration;
                if (!loop && Mathf.Approximately(timeline, duration))
                    isSelected = false;

                if (isSelected)
                {
                    motionIndex = i;
                    clipTime = Mathf.Clamp(
                        (timeline - cursor) * speed,
                        0f,
                        motion.Definition.clip.length);
                    return true;
                }

                cursor += motionDuration;
            }

            if (lastConnected < 0)
                return false;

            RuntimeMotion last = state.Motions[lastConnected];
            motionIndex = lastConnected;
            clipTime = last.Definition.clip.length;
            return true;
        }

        public static float GetPlaylistDuration(RuntimeState state)
        {
            if (state == null || state.Definition == null)
                return 0f;

            float duration = 0f;
            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                if (!IsValidPlaylistMotion(motion))
                    continue;

                duration += motion.Definition.clip.length /
                            GetPlaylistMotionSpeed(state, motion);
            }

            return duration;
        }

        private static bool IsValidPlaylistMotion(RuntimeMotion motion)
        {
            return motion != null &&
                   motion.Connected &&
                   motion.Definition != null &&
                   motion.Definition.clip != null;
        }

        private static float GetPlaylistMotionSpeed(
            RuntimeState state,
            RuntimeMotion motion)
        {
            return Mathf.Max(0.01f, state.Definition.speed) *
                   Mathf.Max(0.01f, motion.Definition.speed);
        }

        private void ApplyFirstConnectedMotion(RuntimeState state)
        {
            bool assigned = false;
            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                float weight = !assigned && motion.Connected ? 1f : 0f;
                if (motion.Connected)
                    assigned = true;

                SetMotionWeight(state, i, weight);
            }
        }

        private void ClearMotionWeights(RuntimeState state)
        {
            for (int i = 0; i < state.Motions.Count; i++)
                SetMotionWeight(state, i, 0f);
        }

        private void SetMotionWeight(
            RuntimeState state,
            int motionIndex,
            float weight)
        {
            RuntimeMotion motion = state.Motions[motionIndex];
            motion.Weight = Mathf.Clamp01(weight);
            state.MotionMixer.SetInputWeight(
                motionIndex,
                motion.Connected ? motion.Weight : 0f);
        }

        private static bool UsesSynchronizedBlendTime(PlayableState state)
        {
            return state != null &&
                   (state.output == PlayableStateOutput.BlendTree1D ||
                    state.output == PlayableStateOutput.BlendTree2D);
        }

        private static void UpdateSynchronizedBlendPlayback(RuntimeState state)
        {
            if (state == null || state.Motions.Count == 0)
                return;

            float duration = GetWeightedBlendDuration(state);
            if (duration <= 0.0001f)
                return;

            float normalizedSpeed =
                Mathf.Max(0.01f, state.Definition.speed) / duration;

            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                if (motion == null ||
                    !motion.Connected ||
                    !motion.Playable.IsValid() ||
                    motion.Definition.clip == null)
                {
                    continue;
                }

                float clipLength = Mathf.Max(0.01f, motion.Definition.clip.length);
                motion.Playable.SetSpeed(clipLength * normalizedSpeed);
            }
        }

        public static float GetWeightedBlendDuration(RuntimeState state)
        {
            float weightedDuration = 0f;
            float totalWeight = 0f;
            float fallbackDuration = 0f;

            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                if (motion == null ||
                    !motion.Connected ||
                    motion.Definition.clip == null)
                {
                    continue;
                }

                float duration = motion.Definition.clip.length /
                    Mathf.Max(0.01f, motion.Definition.speed);
                if (fallbackDuration <= 0f)
                    fallbackDuration = duration;

                float weight = Mathf.Clamp01(motion.Weight);
                if (weight <= 0.0001f)
                    continue;

                weightedDuration += duration * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0.0001f)
                return Mathf.Max(0.01f, weightedDuration / totalWeight);

            return Mathf.Max(0.01f, fallbackDuration);
        }
    }
}
