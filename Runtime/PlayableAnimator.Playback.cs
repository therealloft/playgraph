using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
        public bool PlayState(string stateName, string layerName = null)
        {
            if (!TryFindRuntimeState(
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

        public bool ClearManualState(string layerName = null)
        {
            RuntimeLayer layer = FindRuntimeLayer(layerName);
            if (layer == null)
                return false;

            layer.ManualState = null;
            return true;
        }

        public bool TriggerOneShot(string stateName, string layerName = null)
        {
            if (!TryFindRuntimeState(
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

        public int RuntimeLayerCount => runtimeLayers.Count;

        public bool SetLayerWeight(string layerName, float weight)
        {
            return SetRuntimeLayerWeight(
                FindRuntimeLayer(layerName),
                weight);
        }

        public bool SetLayerWeight(int layerIndex, float weight)
        {
            if (layerIndex < 0 || layerIndex >= runtimeLayers.Count)
                return false;

            return SetRuntimeLayerWeight(
                runtimeLayers[layerIndex],
                weight);
        }

        public float GetLayerWeight(string layerName)
        {
            RuntimeLayer layer = FindRuntimeLayer(layerName);
            return layer != null ? layer.LocalWeight : 0f;
        }

        public float GetLayerWeight(int layerIndex)
        {
            return layerIndex >= 0 && layerIndex < runtimeLayers.Count
                ? runtimeLayers[layerIndex].LocalWeight
                : 0f;
        }

        public bool TryGetLayerWeight(
            int layerIndex,
            out string layerName,
            out float weight)
        {
            layerName = string.Empty;
            weight = 0f;
            if (layerIndex < 0 || layerIndex >= runtimeLayers.Count)
                return false;

            RuntimeLayer layer = runtimeLayers[layerIndex];
            if (layer == null || layer.Definition == null)
                return false;

            layerName = layer.Definition.name;
            weight = layer.LocalWeight;
            return true;
        }

        public bool TryGetLayerState(
            int layerIndex,
            out string layerName,
            out string statePath,
            out float normalizedTime)
        {
            layerName = string.Empty;
            statePath = string.Empty;
            normalizedTime = 0f;

            if (layerIndex < 0 || layerIndex >= runtimeLayers.Count)
                return false;

            RuntimeLayer layer = runtimeLayers[layerIndex];
            if (layer == null || layer.Definition == null)
                return false;

            layerName = layer.Definition.name;
            if (layer.ActiveState == null)
                return true;

            statePath = layer.ActiveState.Path;
            normalizedTime = GetNormalizedStateTime(layer.ActiveState);
            return true;
        }

        public bool SynchronizeState(
            string statePath,
            string layerName,
            float normalizedTime,
            float normalizedTimeThreshold = 0.1f)
        {
            if (!TryFindRuntimeState(
                    statePath,
                    layerName,
                    out RuntimeLayer layer,
                    out RuntimeState state))
            {
                return false;
            }

            if (float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime))
                normalizedTime = 0f;

            normalizedTime = Mathf.Max(0f, normalizedTime);
            bool changedState = layer.ActiveState != state;
            if (changedState)
                EnterState(layer, state, true, false);

            float currentTime = GetNormalizedStateTime(state);
            float threshold = Mathf.Max(0f, normalizedTimeThreshold);
            if (changedState || Mathf.Abs(currentTime - normalizedTime) > threshold)
                SetNormalizedStateTime(state, normalizedTime);

            return true;
        }
    }
}
