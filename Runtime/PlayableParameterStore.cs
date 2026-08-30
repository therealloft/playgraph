using System;
using System.Collections.Generic;
using UnityEngine;

namespace Playgraph
{
    /// <summary>
    /// Owns runtime parameter values and all parameter-specific evaluation.
    /// </summary>
    internal sealed class PlayableParameterStore
    {
        private readonly Dictionary<string, RuntimeParameter> parameters =
            new Dictionary<string, RuntimeParameter>(
                StringComparer.OrdinalIgnoreCase);

        public void Reset(PlayableAnimatorGraph graph)
        {
            parameters.Clear();
            AddDefinitions(graph);
        }

        public void AddDefinitions(PlayableAnimatorGraph graph)
        {
            if (graph == null || graph.parameters == null)
                return;

            for (int i = 0; i < graph.parameters.Count; i++)
            {
                PlayableParameter definition = graph.parameters[i];
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.name) ||
                    parameters.ContainsKey(definition.name))
                {
                    continue;
                }

                parameters.Add(
                    definition.name,
                    new RuntimeParameter(definition));
            }
        }

        public void SetFloat(string name, float value)
        {
            RuntimeParameter parameter = GetOrCreate(
                name,
                PlayableParameterType.Float);
            parameter.Type = PlayableParameterType.Float;
            parameter.FloatValue = value;
        }

        public float GetFloat(string name)
        {
            return parameters.TryGetValue(name, out RuntimeParameter parameter)
                ? parameter.FloatValue
                : 0f;
        }

        public void SetBool(string name, bool value)
        {
            RuntimeParameter parameter = GetOrCreate(
                name,
                PlayableParameterType.Bool);
            parameter.Type = PlayableParameterType.Bool;
            parameter.BoolValue = value;
        }

        public bool GetBool(string name)
        {
            return parameters.TryGetValue(name, out RuntimeParameter parameter) &&
                   parameter.BoolValue;
        }

        public void SetInteger(string name, int value)
        {
            RuntimeParameter parameter = GetOrCreate(
                name,
                PlayableParameterType.Integer);
            parameter.Type = PlayableParameterType.Integer;
            parameter.IntValue = value;
        }

        public int GetInteger(string name)
        {
            return parameters.TryGetValue(name, out RuntimeParameter parameter)
                ? parameter.IntValue
                : 0;
        }

        public void SetEnum(string name, string value)
        {
            RuntimeParameter parameter = GetOrCreate(
                name,
                PlayableParameterType.Enum);
            parameter.Type = PlayableParameterType.Enum;
            parameter.EnumValue = value;
        }

        public string GetEnum(string name)
        {
            return parameters.TryGetValue(name, out RuntimeParameter parameter)
                ? parameter.EnumValue
                : string.Empty;
        }

        public void SetTrigger(string name)
        {
            RuntimeParameter parameter = GetOrCreate(
                name,
                PlayableParameterType.Trigger);
            parameter.Type = PlayableParameterType.Trigger;
            parameter.Triggered = true;
        }

        public void ResetTrigger(string name)
        {
            if (!parameters.TryGetValue(name, out RuntimeParameter parameter))
                return;

            parameter.Triggered = false;
        }

        public bool GetTrigger(string name)
        {
            return parameters.TryGetValue(name, out RuntimeParameter parameter) &&
                   parameter.Triggered;
        }

        public bool ConditionsPass(List<PlayableCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0)
                return true;

            for (int i = 0; i < conditions.Count; i++)
            {
                PlayableCondition condition = conditions[i];
                if (condition == null)
                    continue;

                if (string.IsNullOrWhiteSpace(condition.parameter) ||
                    !parameters.TryGetValue(
                        condition.parameter,
                        out RuntimeParameter parameter))
                {
                    return false;
                }

                if (parameter.Type != PlayableParameterType.Trigger &&
                    condition.mode == PlayableConditionMode.None)
                {
                    continue;
                }

                if (!ConditionPasses(condition, parameter))
                    return false;
            }

            return true;
        }

        public bool HasActiveTriggerCondition(
            List<PlayableCondition> conditions)
        {
            if (conditions == null)
                return false;

            for (int i = 0; i < conditions.Count; i++)
            {
                PlayableCondition condition = conditions[i];
                if (condition == null ||
                    string.IsNullOrWhiteSpace(condition.parameter) ||
                    !parameters.TryGetValue(
                        condition.parameter,
                        out RuntimeParameter parameter) ||
                    parameter.Type != PlayableParameterType.Trigger ||
                    !parameter.Triggered)
                {
                    continue;
                }

                if (ConditionPasses(condition, parameter))
                    return true;
            }

            return false;
        }

        public void ConsumeTriggers(List<PlayableCondition> conditions)
        {
            if (conditions == null)
                return;

            for (int i = 0; i < conditions.Count; i++)
            {
                PlayableCondition condition = conditions[i];
                if (condition == null ||
                    string.IsNullOrWhiteSpace(condition.parameter) ||
                    !parameters.TryGetValue(
                        condition.parameter,
                        out RuntimeParameter parameter) ||
                    parameter.Type != PlayableParameterType.Trigger ||
                    !parameter.Triggered ||
                    !ConditionPasses(condition, parameter))
                {
                    continue;
                }

                parameter.Triggered = false;
            }
        }

        public void GetSnapshot(
            PlayableAnimatorGraph graph,
            Func<string, PlayableParameter> findDefinition,
            List<PlayableParameterDebugInfo> results)
        {
            results.Clear();

            if (graph != null && graph.parameters != null)
            {
                for (int i = 0; i < graph.parameters.Count; i++)
                {
                    PlayableParameter definition = graph.parameters[i];
                    if (definition == null ||
                        string.IsNullOrWhiteSpace(definition.name))
                    {
                        continue;
                    }

                    AddDebugInfo(
                        definition.name,
                        findDefinition,
                        results);
                }
            }

            foreach (string name in parameters.Keys)
            {
                bool alreadyAdded = false;
                for (int i = 0; i < results.Count; i++)
                {
                    if (string.Equals(
                            results[i].Name,
                            name,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                    AddDebugInfo(name, findDefinition, results);
            }
        }

        private void AddDebugInfo(
            string name,
            Func<string, PlayableParameter> findDefinition,
            List<PlayableParameterDebugInfo> results)
        {
            if (!parameters.TryGetValue(name, out RuntimeParameter parameter))
                return;

            PlayableParameterDebugInfo info = new PlayableParameterDebugInfo
            {
                Name = name,
                Type = parameter.Type,
                FloatValue = parameter.FloatValue,
                BoolValue = parameter.BoolValue,
                IntValue = parameter.IntValue,
                Triggered = parameter.Triggered,
                EnumValue = parameter.EnumValue
            };

            PlayableParameter definition = findDefinition?.Invoke(name);
            if (definition != null && definition.enumOptions != null)
                info.EnumOptions.AddRange(definition.enumOptions);

            results.Add(info);
        }

        private RuntimeParameter GetOrCreate(
            string name,
            PlayableParameterType type)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "Parameter";

            if (!parameters.TryGetValue(name, out RuntimeParameter parameter))
            {
                parameter = new RuntimeParameter
                {
                    Type = type
                };
                parameters.Add(name, parameter);
            }

            return parameter;
        }

        private static bool ConditionPasses(
            PlayableCondition condition,
            RuntimeParameter parameter)
        {
            switch (parameter.Type)
            {
                case PlayableParameterType.Bool:
                    return CompareBool(
                        parameter.BoolValue,
                        condition.mode,
                        condition.boolValue);
                case PlayableParameterType.Integer:
                    return CompareFloat(
                        parameter.IntValue,
                        condition.mode,
                        condition.intValue);
                case PlayableParameterType.Trigger:
                    return parameter.Triggered;
                case PlayableParameterType.Enum:
                    return CompareString(
                        parameter.EnumValue,
                        condition.mode,
                        condition.enumValue);
                default:
                    return CompareFloat(
                        parameter.FloatValue,
                        condition.mode,
                        condition.floatValue);
            }
        }

        private static bool CompareBool(
            bool left,
            PlayableConditionMode mode,
            bool right)
        {
            switch (mode)
            {
                case PlayableConditionMode.Equals:
                    return left == right;
                case PlayableConditionMode.NotEquals:
                    return left != right;
                default:
                    return left;
            }
        }

        private static bool CompareString(
            string left,
            PlayableConditionMode mode,
            string right)
        {
            bool equals = string.Equals(
                left,
                right,
                StringComparison.OrdinalIgnoreCase);

            switch (mode)
            {
                case PlayableConditionMode.Equals:
                    return equals;
                case PlayableConditionMode.NotEquals:
                    return !equals;
                default:
                    return equals;
            }
        }

        private static bool CompareFloat(
            float left,
            PlayableConditionMode mode,
            float right)
        {
            switch (mode)
            {
                case PlayableConditionMode.Equals:
                    return Mathf.Approximately(left, right);
                case PlayableConditionMode.NotEquals:
                    return !Mathf.Approximately(left, right);
                case PlayableConditionMode.Greater:
                    return left > right;
                case PlayableConditionMode.Less:
                    return left < right;
                case PlayableConditionMode.GreaterOrEqual:
                    return left >= right;
                case PlayableConditionMode.LessOrEqual:
                    return left <= right;
                default:
                    return true;
            }
        }

        private sealed class RuntimeParameter
        {
            public PlayableParameterType Type;
            public float FloatValue;
            public bool BoolValue;
            public int IntValue;
            public bool Triggered;
            public string EnumValue;

            public RuntimeParameter()
            {
            }

            public RuntimeParameter(PlayableParameter definition)
            {
                Type = definition.type;
                FloatValue = definition.floatValue;
                BoolValue = definition.type == PlayableParameterType.Trigger
                    ? false
                    : definition.boolValue;
                IntValue = definition.intValue;
                EnumValue = definition.enumValue;

                if (definition.type == PlayableParameterType.Enum &&
                    string.IsNullOrWhiteSpace(EnumValue) &&
                    definition.enumOptions != null &&
                    definition.enumOptions.Count > 0)
                {
                    EnumValue = definition.enumOptions[0];
                }
            }
        }
    }
}
