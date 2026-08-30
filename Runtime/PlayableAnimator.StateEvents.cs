using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
        private bool CanInterruptNow(
            RuntimeState incomingState,
            RuntimeLayer incomingLayer,
            RuntimeState currentState,
            RuntimeLayer currentLayer,
            out PlayableInterruption matchingRule)
        {
            matchingRule = null;
            if (!TryGetMatchingInterruption(
                    incomingState,
                    incomingLayer,
                    currentState,
                    currentLayer,
                    out matchingRule))
            {
                return false;
            }

            return matchingRule.timing == PlayableInterruptionTiming.Immediate ||
                   currentState.Weight >= 0.999f;
        }

        private static bool TryGetMatchingInterruption(
            RuntimeState incomingState,
            RuntimeLayer incomingLayer,
            RuntimeState currentState,
            RuntimeLayer currentLayer,
            out PlayableInterruption matchingRule)
        {
            matchingRule = null;
            if (incomingState == null ||
                incomingState.Definition == null ||
                incomingState.Definition.interruptions == null ||
                incomingLayer == null ||
                currentState == null ||
                currentState.Definition == null ||
                currentLayer == null)
            {
                return false;
            }

            if (incomingLayer != currentLayer &&
                !incomingState.Definition.interruptOtherLayers)
            {
                return false;
            }

            List<PlayableInterruption> interruptions =
                incomingState.Definition.interruptions;
            PlayableInterruption broadMatch = null;
            for (int i = 0; i < interruptions.Count; i++)
            {
                PlayableInterruption rule = interruptions[i];
                if (rule == null)
                    continue;

                if (!InterruptionRuleMatches(
                        rule,
                        incomingState,
                        incomingLayer,
                        currentState,
                        currentLayer))
                {
                    continue;
                }

                if (rule.target == PlayableInterruptionTarget.State ||
                    rule.target == PlayableInterruptionTarget.Self)
                {
                    matchingRule = rule;
                    return true;
                }

                if (broadMatch == null)
                    broadMatch = rule;
            }

            matchingRule = broadMatch;
            return matchingRule != null;
        }

        private static bool InterruptionRuleMatches(
            PlayableInterruption rule,
            RuntimeState incomingState,
            RuntimeLayer incomingLayer,
            RuntimeState currentState,
            RuntimeLayer currentLayer)
        {
            bool sameLayer = incomingLayer == currentLayer;
            bool sameState = incomingState == currentState;

            switch (rule.target)
            {
                case PlayableInterruptionTarget.Self:
                    return sameState;
                case PlayableInterruptionTarget.AllStates:
                    return sameLayer && !sameState;
                case PlayableInterruptionTarget.AllStatesFromOtherLayers:
                    return !sameLayer;
                case PlayableInterruptionTarget.State:
                    return MatchesSpecificInterruption(
                        rule,
                        currentState,
                        currentLayer);
                default:
                    return false;
            }
        }

        private static bool MatchesSpecificInterruption(
            PlayableInterruption rule,
            RuntimeState currentState,
            RuntimeLayer currentLayer)
        {
            return currentLayer.Definition != null &&
                   string.Equals(
                       rule.layerName,
                       currentLayer.Definition.name,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       rule.stateName,
                       currentState.Path,
                       StringComparison.OrdinalIgnoreCase);
        }

        private bool HasReachedExitTime(RuntimeState state)
        {
            if (state == null ||
                state.Definition == null ||
                !state.Definition.hasExitTime)
            {
                return true;
            }

            return GetNormalizedStateTime(state) >=
                   Mathf.Max(0f, state.Definition.exitTime);
        }

        private float GetNormalizedStateTime(RuntimeState state)
        {
            float duration = GetCurrentStateDuration(state);
            if (duration <= 0.0001f)
                return state != null ? state.ElapsedTime : 0f;

            return state.ElapsedTime / duration;
        }

        private void SetNormalizedStateTime(
            RuntimeState state,
            float normalizedTime)
        {
            if (state == null || state.Definition == null)
                return;

            float duration = GetCurrentStateDuration(state);
            state.ElapsedTime = duration > 0.0001f
                ? normalizedTime * duration
                : normalizedTime;
            state.PreviousNormalizedTime = normalizedTime;

            float playableNormalizedTime = state.Definition.loop
                ? Mathf.Repeat(normalizedTime, 1f)
                : Mathf.Clamp01(normalizedTime);

            if (state.Definition.output == PlayableStateOutput.Playlist)
            {
                motionEvaluator.ApplyPlaylist(state, true);
            }
            else if (state.HasMotionMixer)
            {
                for (int i = 0; i < state.Motions.Count; i++)
                {
                    RuntimeMotion motion = state.Motions[i];
                    if (motion == null ||
                        motion.Definition == null ||
                        motion.Definition.clip == null ||
                        !motion.Playable.IsValid())
                    {
                        continue;
                    }

                    double time = PlayableAnimationTime.GetCycleOffsetTime(
                                      motion.Definition.clip,
                                      motion.Definition.cycleOffset) +
                                  playableNormalizedTime *
                                  motion.Definition.clip.length;
                    motion.Playable.SetTime(time);
                }
            }
            else if (state.Playable.IsValid() && state.Definition.clip != null)
            {
                state.Playable.SetTime(
                    playableNormalizedTime * state.Definition.clip.length);
            }

            if (state.Definition.applyRootMotion)
                suppressNextRootMotionSample = true;
        }

        private float GetCurrentStateDuration(RuntimeState state)
        {
            if (state == null || state.Definition == null)
                return 0f;

            if (state.Definition.output == PlayableStateOutput.Playlist)
                return PlayableMotionEvaluator.GetPlaylistDuration(state);

            if (state.HasMotionMixer)
                return PlayableMotionEvaluator.GetWeightedBlendDuration(state);

            return GetStateDuration(state.Definition);
        }

        private void ResetEventState(RuntimeState state)
        {
            if (state == null)
                return;

            int eventCount = state.Definition != null &&
                             state.Definition.events != null
                ? state.Definition.events.Count
                : 0;
            if (state.EventFired == null || state.EventFired.Length != eventCount)
                state.EventFired = new bool[eventCount];
            else
                Array.Clear(state.EventFired, 0, state.EventFired.Length);
        }

        private static void EnsureEventState(RuntimeState state)
        {
            if (state == null)
                return;

            int eventCount = state.Definition != null &&
                             state.Definition.events != null
                ? state.Definition.events.Count
                : 0;
            if (state.EventFired == null || state.EventFired.Length != eventCount)
                state.EventFired = new bool[eventCount];
        }

        private void DispatchStateTick(RuntimeLayer layer, RuntimeState state)
        {
            float normalizedTime = GetNormalizedStateTime(state);
            NotifyStateUpdate(layer, state, normalizedTime);
            DispatchStateEvents(layer, state, normalizedTime);
            DispatchLoopBoundaryEvents(
                layer,
                state,
                state.PreviousNormalizedTime,
                normalizedTime);
            state.PreviousNormalizedTime = normalizedTime;
        }

        private void DispatchLoopBoundaryEvents(
            RuntimeLayer layer,
            RuntimeState state,
            float previousNormalizedTime,
            float normalizedTime)
        {
            if (state == null ||
                state.Definition == null ||
                normalizedTime <= previousNormalizedTime)
            {
                return;
            }

            float previousTime = Mathf.Max(0f, previousNormalizedTime);
            float currentTime = Mathf.Max(0f, normalizedTime);
            int completedLoops = Mathf.FloorToInt(currentTime) -
                                 Mathf.FloorToInt(previousTime);
            if (completedLoops <= 0)
                return;

            if (!state.Definition.loop)
                completedLoops = previousTime < 1f && currentTime >= 1f ? 1 : 0;

            for (int i = 0; i < completedLoops; i++)
            {
                DispatchStateBoundaryEvents(
                    layer,
                    state,
                    PlayableStateEventType.StateExit,
                    PlayableStateEventTrigger.OncePerLoop);

                if (state.Definition.loop)
                {
                    DispatchStateBoundaryEvents(
                        layer,
                        state,
                        PlayableStateEventType.StateEnter,
                        PlayableStateEventTrigger.OncePerLoop);
                }
            }
        }

        private void DispatchStateEvents(
            RuntimeLayer layer,
            RuntimeState state,
            float normalizedTime)
        {
            if (state.Definition.events == null || state.Definition.events.Count == 0)
                return;

            EnsureEventState(state);

            for (int i = 0; i < state.Definition.events.Count; i++)
            {
                PlayableStateEvent stateEvent = state.Definition.events[i];
                if (stateEvent == null || !stateEvent.enabled ||
                    stateEvent.type != PlayableStateEventType.NormalizedTime ||
                    IsBlendTreeState(state.Definition))
                    continue;

                float triggerTime = Mathf.Max(0f, stateEvent.normalizedTime);

                bool shouldFire = stateEvent.trigger ==
                                  PlayableStateEventTrigger.OncePerLoop &&
                                  state.Definition.loop
                    ? CrossedLoopingTime(
                        state.PreviousNormalizedTime,
                        normalizedTime,
                        triggerTime)
                    : !state.EventFired[i] &&
                      state.PreviousNormalizedTime < triggerTime &&
                      normalizedTime >= triggerTime;

                if (!shouldFire)
                    continue;

                state.EventFired[i] = true;
                NotifyStateEvent(layer, state, stateEvent);
            }
        }

        private static bool CrossedLoopingTime(
            float previousNormalizedTime,
            float normalizedTime,
            float triggerTime)
        {
            int previousLoop = Mathf.FloorToInt(previousNormalizedTime);
            int currentLoop = Mathf.FloorToInt(normalizedTime);
            float previousCycle = Mathf.Repeat(previousNormalizedTime, 1f);
            float currentCycle = Mathf.Repeat(normalizedTime, 1f);
            float triggerCycle = Mathf.Repeat(triggerTime, 1f);

            if (currentLoop > previousLoop)
            {
                return triggerCycle >= previousCycle ||
                       triggerCycle <= currentCycle ||
                       currentLoop - previousLoop > 1;
            }

            return triggerCycle > previousCycle && triggerCycle <= currentCycle;
        }

        private void NotifyStateEnter(RuntimeLayer layer, RuntimeState state)
        {
            if (state.Definition.behaviours != null)
            {
                for (int i = 0; i < state.Definition.behaviours.Count; i++)
                {
                    PlayableStateBehaviour behaviour =
                        state.Definition.behaviours[i];
                    if (behaviour == null)
                        continue;

                    try
                    {
                        behaviour.OnPlayableStateEnter(
                            this,
                            layer.Definition.name,
                            state.Path);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }
            }

            DispatchStateBoundaryEvents(
                layer,
                state,
                PlayableStateEventType.StateEnter,
                PlayableStateEventTrigger.OncePerState);
            DispatchStateBoundaryEvents(
                layer,
                state,
                PlayableStateEventType.StateEnter,
                PlayableStateEventTrigger.OncePerLoop);
        }

        private void NotifyStateUpdate(
            RuntimeLayer layer,
            RuntimeState state,
            float normalizedTime)
        {
            if (state.Definition.behaviours == null)
                return;

            for (int i = 0; i < state.Definition.behaviours.Count; i++)
            {
                PlayableStateBehaviour behaviour =
                    state.Definition.behaviours[i];
                if (behaviour == null)
                    continue;

                try
                {
                    behaviour.OnPlayableStateUpdate(
                        this,
                        layer.Definition.name,
                        state.Path,
                        normalizedTime);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private void NotifyStateExit(RuntimeLayer layer, RuntimeState state)
        {
            if (state.Definition.behaviours != null)
            {
                for (int i = 0; i < state.Definition.behaviours.Count; i++)
                {
                    PlayableStateBehaviour behaviour =
                        state.Definition.behaviours[i];
                    if (behaviour == null)
                        continue;

                    try
                    {
                        behaviour.OnPlayableStateExit(
                            this,
                            layer.Definition.name,
                            state.Path);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }
            }

            DispatchStateBoundaryEvents(
                layer,
                state,
                PlayableStateEventType.StateExit,
                PlayableStateEventTrigger.OncePerState);
        }

        private void DispatchStateBoundaryEvents(
            RuntimeLayer layer,
            RuntimeState state,
            PlayableStateEventType eventType,
            PlayableStateEventTrigger trigger)
        {
            if (state.Definition.events == null || state.Definition.events.Count == 0)
                return;

            EnsureEventState(state);
            for (int i = 0; i < state.Definition.events.Count; i++)
            {
                PlayableStateEvent stateEvent = state.Definition.events[i];
                if (stateEvent == null || !stateEvent.enabled ||
                    stateEvent.type != eventType ||
                    stateEvent.trigger != trigger ||
                    (trigger == PlayableStateEventTrigger.OncePerState &&
                     state.EventFired[i]))
                {
                    continue;
                }

                if (trigger == PlayableStateEventTrigger.OncePerState)
                    state.EventFired[i] = true;
                NotifyStateEvent(layer, state, stateEvent);
            }
        }

        private static bool IsBlendTreeState(PlayableState state)
        {
            if (state == null)
                return false;

            return state.output == PlayableStateOutput.BlendTree1D ||
                   state.output == PlayableStateOutput.BlendTree2D ||
                   state.output == PlayableStateOutput.DirectBlend;
        }

        private void NotifyStateEvent(
            RuntimeLayer layer,
            RuntimeState state,
            PlayableStateEvent stateEvent)
        {
            if (state.Definition.behaviours != null)
            {
                for (int i = 0; i < state.Definition.behaviours.Count; i++)
                {
                    PlayableStateBehaviour behaviour =
                        state.Definition.behaviours[i];
                    if (behaviour == null)
                        continue;

                    try
                    {
                        behaviour.OnPlayableStateEvent(
                            this,
                            layer.Definition.name,
                            state.Path,
                            stateEvent.name,
                            stateEvent.type,
                            stateEvent.trigger);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }
            }

            NotifyAnimatorStateEvent(layer, state, stateEvent);
        }

        private void NotifyAnimatorStateEvent(
            RuntimeLayer layer,
            RuntimeState state,
            PlayableStateEvent stateEvent)
        {
            string layerName = layer.Definition.name;
            string stateName = state.Path;
            Delegate[] handlers = StateEventRaised?.GetInvocationList();
            if (handlers != null)
            {
                for (int i = 0; i < handlers.Length; i++)
                {
                    try
                    {
                        ((Action<
                            string,
                            string,
                            string,
                            PlayableStateEventType,
                            PlayableStateEventTrigger>)handlers[i]).Invoke(
                            layerName,
                            stateName,
                            stateEvent.name,
                            stateEvent.type,
                            stateEvent.trigger);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }
            }

            if (eventBindings == null)
                return;

            for (int i = 0; i < eventBindings.Count; i++)
            {
                PlayableAnimatorEventBinding binding = eventBindings[i];
                if (binding == null ||
                    !string.Equals(
                        binding.EventName,
                        stateEvent.name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    binding.Response?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }
    }
}
