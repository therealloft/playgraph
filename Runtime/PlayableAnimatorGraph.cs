using System;
using System.Collections.Generic;
using UnityEngine;

namespace Playgraph
{
    public enum PlayableParameterType
    {
        Float,
        Bool,
        Integer,
        Trigger,
        Enum
    }

    public enum PlayableConditionMode
    {
        None,
        Equals,
        NotEquals,
        Greater,
        Less,
        GreaterOrEqual,
        LessOrEqual
    }

    public enum PlayableStateOutput
    {
        Clip = 0,
        BlendTree1D = 1,
        BlendTree2D = 2,
        DirectBlend = 3,
        OneShot = 4,
        Playlist = 5
    }

    public enum PlayableStateKind
    {
        Motion,
        SubStateMachine
    }

    public enum PlayableBlendTree2DType
    {
        FreeformDirectional,
        FreeformCartesian
    }

    public enum PlayableInterruptionTarget
    {
        AllStates,
        Self,
        State,
        AllStatesFromOtherLayers
    }

    public enum PlayableInterruptionTiming
    {
        Immediate,
        WaitBlend
    }

    public enum PlayableStateEventType
    {
        NormalizedTime,
        StateEnter,
        StateExit
    }

    public enum PlayableStateEventTrigger
    {
        OncePerState,
        OncePerLoop
    }

    [Serializable]
    public sealed class PlayableParameter
    {
        public string name = "Speed";
        public PlayableParameterType type;
        public float floatValue;
        public bool boolValue;
        public int intValue;
        public string enumValue;
        public List<string> enumOptions = new List<string>
    {
        "Value"
    };
    }

    [Serializable]
    public sealed class PlayableCondition
    {
        public string parameter = "Speed";
        public PlayableConditionMode mode =
            PlayableConditionMode.Greater;
        public float floatValue;
        public bool boolValue;
        public int intValue;
        public string enumValue;
    }

    [Serializable]
    public sealed class PlayableInterruption
    {
        public PlayableInterruptionTarget target =
            PlayableInterruptionTarget.AllStates;
        public PlayableInterruptionTiming timing =
            PlayableInterruptionTiming.Immediate;
        public string layerName;
        public string stateName;
    }

    [Serializable]
    public sealed class PlayableStateEvent
    {
        public string name = "Event";
        public bool enabled = true;
        public PlayableStateEventType type =
            PlayableStateEventType.NormalizedTime;
        public PlayableStateEventTrigger trigger =
            PlayableStateEventTrigger.OncePerState;
        [Min(0f)] public float normalizedTime;
    }

    [Serializable]
    public sealed class PlayableMotion
    {
        public string name = "Motion";
        public AnimationClip clip;
        public float speed = 1f;
        public bool applyFootIK = true;
        public bool enabled = true;
        public float threshold;
        public Vector2 position;
        [Range(0f, 1f)] public float cycleOffset;
        public string directParameter;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(name) && name != "Motion")
                    return name;

                return clip != null ? clip.name : "(none)";
            }
        }
    }

    public static class PlayableBlendMath
    {
        private const float ExactPointEpsilon = 0.0001f;
        private const float InfluenceEpsilon = 0.00001f;
        private const float DirectionalAngleScale = 2f;

        public static bool Calculate2DWeights(
            IList<PlayableMotion> motions,
            Vector2 value,
            float[] weights,
            PlayableBlendTree2DType blendType =
                PlayableBlendTree2DType.FreeformDirectional)
        {
            if (weights == null)
                return false;

            Array.Clear(weights, 0, weights.Length);
            if (motions == null || motions.Count == 0)
                return false;

            int count = Mathf.Min(motions.Count, weights.Length);
            int nearest = -1;
            int validCount = 0;
            float nearestDistance = float.MaxValue;
            int exactCount = 0;

            for (int i = 0; i < count; i++)
            {
                if (!IsMotionBlendable(motions, i))
                    continue;

                validCount++;
                float distance = (motions[i].position - value).sqrMagnitude;
                if (distance <= ExactPointEpsilon)
                {
                    weights[i] = 1f;
                    exactCount++;
                }

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }

            if (validCount <= 0)
                return false;

            if (exactCount > 0)
            {
                float exactWeight = 1f / exactCount;
                for (int i = 0; i < count; i++)
                    weights[i] = weights[i] > 0f ? exactWeight : 0f;

                return true;
            }

            if (validCount == 1)
            {
                weights[nearest] = 1f;
                return true;
            }

            bool calculated = blendType == PlayableBlendTree2DType.FreeformCartesian
                ? TryCalculateCartesianWeights(motions, value, count, weights)
                : TryCalculateDirectionalWeights(motions, value, count, weights);

            if (calculated)
                return true;

            weights[nearest] = 1f;
            return true;
        }

        private static bool TryCalculateCartesianWeights(
            IList<PlayableMotion> motions,
            Vector2 value,
            int count,
            float[] weights)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsMotionBlendable(motions, i))
                    continue;

                Vector2 source = motions[i].position;
                float influence = 1f;
                bool hasComparison = false;

                for (int j = 0; j < count; j++)
                {
                    if (i == j || !IsMotionBlendable(motions, j))
                        continue;

                    Vector2 sourceToOther = motions[j].position - source;
                    float comparisonLength = sourceToOther.sqrMagnitude;
                    if (comparisonLength <= InfluenceEpsilon)
                        continue;

                    Vector2 sourceToValue = value - source;
                    float bandInfluence = 1f -
                        Vector2.Dot(sourceToValue, sourceToOther) /
                        comparisonLength;
                    influence = Mathf.Min(influence, bandInfluence);
                    hasComparison = true;
                }

                if (hasComparison)
                    weights[i] = Mathf.Clamp01(influence);
            }

            return NormalizeWeights(weights, count);
        }

        private static bool TryCalculateDirectionalWeights(
            IList<PlayableMotion> motions,
            Vector2 value,
            int count,
            float[] weights)
        {
            float valueMagnitude = value.magnitude;

            for (int i = 0; i < count; i++)
            {
                if (!IsMotionBlendable(motions, i))
                    continue;

                Vector2 source = motions[i].position;
                float sourceMagnitude = source.magnitude;
                float influence = 1f;
                bool hasComparison = false;

                for (int j = 0; j < count; j++)
                {
                    if (i == j || !IsMotionBlendable(motions, j))
                        continue;

                    Vector2 other = motions[j].position;
                    float otherMagnitude = other.magnitude;

                    if (sourceMagnitude <= InfluenceEpsilon &&
                        otherMagnitude <= InfluenceEpsilon)
                    {
                        continue;
                    }

                    float bandInfluence;
                    if (sourceMagnitude <= InfluenceEpsilon)
                    {
                        bandInfluence = 1f -
                            valueMagnitude /
                            Mathf.Max(InfluenceEpsilon, otherMagnitude);
                    }
                    else if (otherMagnitude <= InfluenceEpsilon)
                    {
                        bandInfluence = valueMagnitude /
                            Mathf.Max(InfluenceEpsilon, sourceMagnitude);
                    }
                    else
                    {
                        float averageMagnitude = Mathf.Max(
                            InfluenceEpsilon,
                            (sourceMagnitude + otherMagnitude) * 0.5f);
                        Vector2 sourceToOther = new Vector2(
                            (otherMagnitude - sourceMagnitude) / averageMagnitude,
                            SignedAngleRadians(source, other) *
                            DirectionalAngleScale);
                        Vector2 sourceToValue = new Vector2(
                            (valueMagnitude - sourceMagnitude) / averageMagnitude,
                            SignedAngleRadians(source, value) *
                            DirectionalAngleScale);
                        float comparisonLength = sourceToOther.sqrMagnitude;
                        if (comparisonLength <= InfluenceEpsilon)
                            continue;

                        bandInfluence = 1f -
                            Vector2.Dot(sourceToValue, sourceToOther) /
                            comparisonLength;
                    }

                    influence = Mathf.Min(influence, bandInfluence);
                    hasComparison = true;
                }

                if (hasComparison)
                    weights[i] = Mathf.Clamp01(influence);
            }

            return NormalizeWeights(weights, count);
        }

        private static bool NormalizeWeights(float[] weights, int count)
        {
            float total = 0f;
            for (int i = 0; i < count; i++)
                total += Mathf.Max(0f, weights[i]);

            if (total <= ExactPointEpsilon)
                return false;

            for (int i = 0; i < count; i++)
                weights[i] = Mathf.Max(0f, weights[i]) / total;

            return true;
        }

        private static float SignedAngleRadians(Vector2 from, Vector2 to)
        {
            if (from.sqrMagnitude <= InfluenceEpsilon ||
                to.sqrMagnitude <= InfluenceEpsilon)
            {
                return 0f;
            }

            float cross = from.x * to.y - from.y * to.x;
            float dot = Vector2.Dot(from, to);
            return Mathf.Atan2(cross, dot);
        }

        private static bool IsMotionBlendable(
            IList<PlayableMotion> motions,
            int index)
        {
            PlayableMotion motion = motions[index];
            return motion != null && motion.enabled && motion.clip != null;
        }
    }

    [Serializable]
    public sealed class PlayableState
    {
        public string name = "State";
        public PlayableStateKind kind = PlayableStateKind.Motion;
        public bool enabled = true;
        public bool isDefault;
        public bool manualOnly;
        public bool isExitState;
        public int priority;
        public PlayableStateOutput output = PlayableStateOutput.Clip;
        public AnimationClip clip;
        public string blendParameterX = "Speed";
        public string blendParameterY = "Direction";
        public PlayableBlendTree2DType blendTree2DType =
            PlayableBlendTree2DType.FreeformDirectional;
        public bool loop = true;
        public float speed = 1f;
        public bool applyFootIK = true;
        public bool applyRootMotion;
        public bool rootMotionPositionXZ = true;
        public bool rootMotionPositionY;
        public bool rootMotionRotation = true;
        public float fadeDuration = 0.15f;
        public bool hasExitTime;
        [Min(0f)] public float exitTime = 1f;
        public List<PlayableMotion> motions =
            new List<PlayableMotion>();
        public List<PlayableCondition> conditions =
            new List<PlayableCondition>();
        public List<PlayableInterruption> interruptions =
            new List<PlayableInterruption>();
        public bool interruptOtherLayers;
        public List<PlayableStateBehaviour> behaviours =
            new List<PlayableStateBehaviour>();
        public List<PlayableStateEvent> events =
            new List<PlayableStateEvent>();
        [SerializeReference]
        public List<PlayableState> subStates =
            new List<PlayableState>();

        public bool IsSubStateMachine =>
            kind == PlayableStateKind.SubStateMachine;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(name) && name != "State")
                    return name;

                if (IsSubStateMachine)
                    return "Sub-State Machine";

                return clip != null ? clip.name : output.ToString();
            }
        }
    }

    [Serializable]
    public sealed class PlayableLayer
    {
        public string name = "Base";
        public AvatarMask avatarMask;
        public bool additive;
        [Range(0f, 1f)] public float weight = 1f;
        public List<PlayableState> states =
            new List<PlayableState>();
    }

    [CreateAssetMenu(menuName = "Play Graph/Playable Animator Graph", fileName = "PlayableAnimatorGraph")]
    public sealed class PlayableAnimatorGraph : ScriptableObject
    {
        [Min(0f)] public float defaultFadeDuration = 0.15f;
        public bool showInPlayableGraphVisualizer = true;
        public List<PlayableParameter> parameters =
            new List<PlayableParameter>();
        public List<PlayableLayer> layers =
            new List<PlayableLayer>();

        public void EnsureDefaults()
        {
            if (parameters == null)
                parameters = new List<PlayableParameter>();

            if (layers == null)
                layers = new List<PlayableLayer>();

            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters[i] == null)
                    parameters[i] = new PlayableParameter();

                if (parameters[i].enumOptions == null)
                    parameters[i].enumOptions = new List<string>();

                if (parameters[i].type == PlayableParameterType.Trigger)
                    parameters[i].boolValue = false;

                if (parameters[i].type == PlayableParameterType.Enum)
                {
                    if (parameters[i].enumOptions.Count == 0)
                        parameters[i].enumOptions.Add("Value");

                    if (string.IsNullOrWhiteSpace(parameters[i].enumValue))
                        parameters[i].enumValue = parameters[i].enumOptions[0];
                }
            }

            if (layers.Count == 0)
            {
                PlayableLayer layer = new PlayableLayer();
                layer.states.Add(new PlayableState
                {
                    name = "Idle",
                    isDefault = true
                });
                layers.Add(layer);
            }

            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] == null)
                    layers[i] = new PlayableLayer();

                if (layers[i].states == null)
                    layers[i].states = new List<PlayableState>();

                EnsureStateDefaults(layers[i].states, true, "Idle");
            }
        }

        private static void EnsureStateDefaults(
            List<PlayableState> states,
            bool ensureState,
            string defaultName)
        {
            if (states == null)
                return;

            if (ensureState && states.Count == 0)
            {
                states.Add(new PlayableState
                {
                    name = defaultName,
                    isDefault = true
                });
            }

            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == null)
                    states[i] = new PlayableState();

                PlayableState state = states[i];
                if (state.motions == null)
                    state.motions = new List<PlayableMotion>();
                if (state.conditions == null)
                    state.conditions = new List<PlayableCondition>();
                if (state.interruptions == null)
                    state.interruptions = new List<PlayableInterruption>();
                if (state.behaviours == null)
                    state.behaviours = new List<PlayableStateBehaviour>();
                if (state.events == null)
                    state.events = new List<PlayableStateEvent>();
                if (state.subStates == null)
                    state.subStates = new List<PlayableState>();

                if (state.IsSubStateMachine)
                    EnsureStateDefaults(state.subStates, true, "State 1");
            }
        }

        public PlayableParameter FindParameter(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || parameters == null)
                return null;

            for (int i = 0; i < parameters.Count; i++)
            {
                PlayableParameter parameter = parameters[i];
                if (parameter != null &&
                    string.Equals(
                        parameter.name,
                        parameterName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }
    }

    public sealed class PlayableParameterDebugInfo
    {
        public string Name;
        public PlayableParameterType Type;
        public float FloatValue;
        public bool BoolValue;
        public int IntValue;
        public bool Triggered;
        public string EnumValue;
        public readonly List<string> EnumOptions = new List<string>();
    }

    public sealed class PlayableMotionDebugInfo
    {
        public string Name;
        public string ClipName;
        public float Weight;
        public float Threshold;
        public Vector2 Position;
    }

    public sealed class PlayableStateDebugInfo
    {
        public string Name;
        public PlayableStateOutput Output;
        public bool IsActive;
        public float Weight;
        public readonly List<PlayableMotionDebugInfo> Motions =
            new List<PlayableMotionDebugInfo>();
    }

    public sealed class PlayableLayerDebugInfo
    {
        public string Name;
        public float Weight;
        public string ActiveState;
        public readonly List<PlayableStateDebugInfo> States =
            new List<PlayableStateDebugInfo>();
    }
}
