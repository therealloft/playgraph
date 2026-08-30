using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
        private void EvaluateLayer(RuntimeLayer layer, float deltaTime)
        {
            if (layer == null || !layer.StateMixer.IsValid())
                return;

            if (layer.ActiveState != null)
                layer.ActiveState.ElapsedTime += deltaTime;

            if (layer.OneShotState != null &&
                IsStateComplete(layer.OneShotState))
            {
                layer.OneShotState = null;
            }

            RuntimeState desiredState = layer.OneShotState ?? layer.ManualState;
            if (desiredState == null)
            {
                desiredState = SelectWinningState(layer);
                desiredState = ResolveOutgoingExitState(layer, desiredState);
            }

            if (desiredState != null &&
                CanChangeState(layer, layer.ActiveState, desiredState))
            {
                EnterState(layer, desiredState);
            }
            else if (desiredState == layer.ActiveState)
            {
                parameterStore.ConsumeTriggers(
                    desiredState.Definition.conditions);
            }

            for (int i = 0; i < layer.States.Count; i++)
                ApplyStateWeight(layer, layer.States[i], deltaTime);

            for (int i = 0; i < layer.States.Count; i++)
            {
                RuntimeState state = layer.States[i];
                if (state == layer.ActiveState || state.Weight > 0.0001f)
                    motionEvaluator.ApplyWeights(state, parameterStore);
            }

            if (layer.ActiveState != null)
                DispatchStateTick(layer, layer.ActiveState);
        }

        private RuntimeState SelectWinningState(RuntimeLayer layer)
        {
            return SelectWinningState(
                layer,
                layer != null && layer.Definition != null
                    ? layer.Definition.states
                    : null);
        }

        private RuntimeState SelectWinningState(
            RuntimeLayer layer,
            List<PlayableState> definitions)
        {
            if (layer == null || definitions == null)
                return null;

            PlayableState defaultState = null;
            PlayableState bestState = null;
            bool bestUsesActiveTrigger = false;
            int bestPriority = int.MinValue;

            for (int i = 0; i < definitions.Count; i++)
            {
                PlayableState definition = definitions[i];
                if (definition == null || !definition.enabled)
                    continue;

                if (definition.manualOnly || definition.isExitState)
                    continue;

                if (definition.isDefault && defaultState == null)
                    defaultState = definition;

                if (!parameterStore.ConditionsPass(definition.conditions))
                    continue;

                bool usesActiveTrigger =
                    parameterStore.HasActiveTriggerCondition(
                        definition.conditions);

                if (bestState == null ||
                    (usesActiveTrigger && !bestUsesActiveTrigger) ||
                    (usesActiveTrigger == bestUsesActiveTrigger &&
                     definition.priority > bestPriority) ||
                    (usesActiveTrigger == bestUsesActiveTrigger &&
                     definition.priority == bestPriority &&
                     ContainsState(definition, layer.ActiveState)))
                {
                    bestState = definition;
                    bestUsesActiveTrigger = usesActiveTrigger;
                    bestPriority = definition.priority;
                }
            }

            PlayableState selected = bestState ??
                                            defaultState ??
                                            FirstEnabledDefinition(definitions);
            return ResolveSelectedState(layer, selected);
        }

        private RuntimeState ResolveOutgoingExitState(
            RuntimeLayer layer,
            RuntimeState desiredState)
        {
            if (layer == null ||
                layer.Definition == null ||
                layer.ActiveState == null)
            {
                return desiredState;
            }

            PlayableState stateMachine = FindContainingStateMachine(
                layer.Definition.states,
                layer.ActiveState);
            if (stateMachine == null ||
                ContainsState(stateMachine, desiredState))
            {
                return desiredState;
            }

            if (layer.ActiveState.Definition != null &&
                layer.ActiveState.Definition.isExitState)
            {
                return IsStateComplete(layer.ActiveState)
                    ? desiredState
                    : layer.ActiveState;
            }

            RuntimeState exitState = SelectMatchingExitState(layer, stateMachine);
            return exitState ?? desiredState;
        }

        private RuntimeState SelectMatchingExitState(
            RuntimeLayer layer,
            PlayableState stateMachine)
        {
            if (layer == null ||
                stateMachine == null ||
                stateMachine.subStates == null)
            {
                return null;
            }

            PlayableState best = null;
            int bestPriority = int.MinValue;
            for (int i = 0; i < stateMachine.subStates.Count; i++)
            {
                PlayableState candidate = stateMachine.subStates[i];
                if (candidate == null ||
                    candidate.IsSubStateMachine ||
                    !candidate.enabled ||
                    !candidate.isExitState ||
                    candidate.manualOnly ||
                    !parameterStore.ConditionsPass(candidate.conditions))
                {
                    continue;
                }

                if (best == null || candidate.priority > bestPriority)
                {
                    best = candidate;
                    bestPriority = candidate.priority;
                }
            }

            return best != null ? ResolveSelectedState(layer, best) : null;
        }

        private static PlayableState FindContainingStateMachine(
            List<PlayableState> definitions,
            RuntimeState runtimeState)
        {
            if (definitions == null || runtimeState == null)
                return null;

            for (int i = 0; i < definitions.Count; i++)
            {
                PlayableState definition = definitions[i];
                if (definition == null || !definition.IsSubStateMachine)
                    continue;

                PlayableState nested = FindContainingStateMachine(
                    definition.subStates,
                    runtimeState);
                if (nested != null)
                    return nested;

                if (ContainsState(definition, runtimeState))
                    return definition;
            }

            return null;
        }

        private RuntimeState ResolveSelectedState(
            RuntimeLayer layer,
            PlayableState definition)
        {
            if (layer == null || definition == null)
                return null;

            if (definition.IsSubStateMachine)
                return SelectWinningState(layer, definition.subStates);

            layer.StateLookup.TryGetValue(definition, out RuntimeState state);
            return state;
        }

        private static PlayableState FirstEnabledDefinition(
            List<PlayableState> definitions)
        {
            if (definitions == null)
                return null;

            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null &&
                    definitions[i].enabled &&
                    !definitions[i].manualOnly &&
                    !definitions[i].isExitState)
                {
                    return definitions[i];
                }
            }

            return null;
        }

        private static bool ContainsState(
            PlayableState definition,
            RuntimeState runtimeState)
        {
            if (definition == null || runtimeState == null)
                return false;

            if (!definition.IsSubStateMachine)
                return definition == runtimeState.Definition;

            if (definition.subStates == null)
                return false;

            for (int i = 0; i < definition.subStates.Count; i++)
            {
                if (ContainsState(definition.subStates[i], runtimeState))
                    return true;
            }

            return false;
        }

        private RuntimeState FindDefaultState(RuntimeLayer layer)
        {
            if (layer == null || layer.Definition == null)
                return null;

            return FindDefaultState(layer, layer.Definition.states);
        }

        private RuntimeState FindDefaultState(
            RuntimeLayer layer,
            List<PlayableState> definitions)
        {
            if (definitions == null)
                return null;

            PlayableState fallback = null;
            for (int i = 0; i < definitions.Count; i++)
            {
                PlayableState definition = definitions[i];
                if (definition == null ||
                    !definition.enabled ||
                    definition.manualOnly ||
                    definition.isExitState)
                {
                    continue;
                }

                if (fallback == null)
                    fallback = definition;

                if (!definition.isDefault)
                    continue;

                RuntimeState resolved = definition.IsSubStateMachine
                    ? FindDefaultState(layer, definition.subStates)
                    : ResolveSelectedState(layer, definition);
                if (resolved != null)
                    return resolved;
            }

            if (fallback == null)
                return null;

            return fallback.IsSubStateMachine
                ? FindDefaultState(layer, fallback.subStates)
                : ResolveSelectedState(layer, fallback);
        }

        private void EnterState(
            RuntimeLayer layer,
            RuntimeState state,
            bool immediate = false,
            bool allowCrossLayerInterruptions = true,
            float fadeDurationOverride = -1f)
        {
            if (layer == null || state == null)
                return;

            RuntimeState previousState = layer.ActiveState;
            if (previousState != null)
                NotifyStateExit(layer, previousState);

            if (previousState != state &&
                ((previousState != null &&
                  previousState.Definition != null &&
                  previousState.Definition.applyRootMotion) ||
                 (state.Definition != null &&
                  state.Definition.applyRootMotion)))
            {
                // Resetting a playable during a crossfade can produce one absolute
                // root-position jump. Discard that sample while timelines rebase.
                suppressNextRootMotionSample = true;
            }

            layer.ActiveState = state;
            layer.ActiveStateStartTime = Time.time;
            state.ElapsedTime = 0f;
            state.PreviousNormalizedTime = -0.0001f;
            state.FadeDurationOverride = fadeDurationOverride;
            ResetEventState(state);
            ResetPlayableTime(state.Playable);
            ResetMotionPlayableTimes(state);
            parameterStore.ConsumeTriggers(state.Definition.conditions);
            NotifyStateEnter(layer, state);

            if (allowCrossLayerInterruptions)
                ApplyCrossLayerInterruptions(layer, state);

            if (immediate)
            {
                for (int i = 0; i < layer.States.Count; i++)
                {
                    RuntimeState candidate = layer.States[i];
                    candidate.Weight = candidate == state ? 1f : 0f;
                    layer.StateMixer.SetInputWeight(
                        candidate.InputIndex,
                        candidate.Weight);
                }
            }
        }

        private bool CanChangeState(
            RuntimeLayer layer,
            RuntimeState currentState,
            RuntimeState desiredState)
        {
            if (desiredState == null)
                return false;

            if (currentState == null)
                return true;

            if (currentState == desiredState)
            {
                return parameterStore.HasActiveTriggerCondition(
                           desiredState.Definition != null
                               ? desiredState.Definition.conditions
                               : null) &&
                       CanInterruptNow(
                           desiredState,
                           layer,
                           currentState,
                           layer,
                           out _);
            }

            if (parameterStore.HasActiveTriggerCondition(
                    desiredState.Definition != null
                        ? desiredState.Definition.conditions
                        : null))
            {
                return true;
            }

            if (IsEnteringContainingExitState(
                    layer,
                    currentState,
                    desiredState))
            {
                return true;
            }

            if (HasReachedExitTime(currentState))
                return true;

            return CanInterruptNow(
                desiredState,
                layer,
                currentState,
                layer,
                out _);
        }

        private static bool IsEnteringContainingExitState(
            RuntimeLayer layer,
            RuntimeState currentState,
            RuntimeState desiredState)
        {
            if (layer == null ||
                layer.Definition == null ||
                currentState == null ||
                desiredState == null ||
                desiredState.Definition == null ||
                !desiredState.Definition.isExitState)
            {
                return false;
            }

            PlayableState stateMachine = FindContainingStateMachine(
                layer.Definition.states,
                currentState);
            return stateMachine != null &&
                   ContainsState(stateMachine, desiredState);
        }

        private void ApplyCrossLayerInterruptions(
            RuntimeLayer sourceLayer,
            RuntimeState incomingState)
        {
            if (sourceLayer == null ||
                incomingState == null ||
                incomingState.Definition == null ||
                !incomingState.Definition.interruptOtherLayers)
            {
                return;
            }

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                RuntimeLayer targetLayer = runtimeLayers[i];
                if (targetLayer == null ||
                    targetLayer == sourceLayer ||
                    targetLayer.ActiveState == null)
                {
                    continue;
                }

                if (!CanInterruptNow(
                        incomingState,
                        sourceLayer,
                        targetLayer.ActiveState,
                        targetLayer,
                        out _))
                {
                    continue;
                }

                RuntimeState defaultState = FindDefaultState(targetLayer);
                if (defaultState != null && defaultState != targetLayer.ActiveState)
                {
                    EnterState(
                        targetLayer,
                        defaultState,
                        false,
                        false);
                }
            }
        }

        private void ApplyStateWeight(
            RuntimeLayer layer,
            RuntimeState state,
            float deltaTime)
        {
            float targetWeight = state == layer.ActiveState ? 1f : 0f;
            float fadeDuration = ResolveFadeDuration(state);

            state.Weight = fadeDuration <= 0.0001f
                ? targetWeight
                : Mathf.MoveTowards(
                    state.Weight,
                    targetWeight,
                    deltaTime / fadeDuration);

            layer.StateMixer.SetInputWeight(state.InputIndex, state.Weight);

            if (state == layer.ActiveState && state.Weight >= 0.999f)
                state.FadeDurationOverride = -1f;
        }

        private float ResolveFadeDuration(RuntimeState state)
        {
            if (state != null && state.FadeDurationOverride >= 0f)
                return state.FadeDurationOverride;

            if (state != null &&
                state.Definition != null &&
                state.Definition.fadeDuration > 0f)
            {
                return state.Definition.fadeDuration;
            }

            return state != null ? state.DefaultFadeDuration : 0f;
        }
    }
}
