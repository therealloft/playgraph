using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    [DefaultExecutionOrder(56)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed partial class PlayableAnimator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private PlayableAnimatorGraph graphAsset;

        [Header("Runtime")]
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool clearAnimatorController = true;
        [SerializeField] private bool showInPlayableGraphVisualizer = true;
        [SerializeField] private bool applyRootMotionToTransform = true;

        private readonly Dictionary<string, RuntimeParameter> runtimeParameters =
            new Dictionary<string, RuntimeParameter>(
                StringComparer.OrdinalIgnoreCase);
        private readonly List<RuntimeLayer> runtimeLayers =
            new List<RuntimeLayer>();

        private readonly List<MountedGraph> mountedGraphs =
            new List<MountedGraph>();

        private AnimationPlayableOutput animationOutput;
        private AnimationLayerMixerPlayable mountedLayerMixer;
        private int nextMountHandle;

        public int MountedGraphCount => mountedGraphs.Count;

        private PlayableGraph playableGraph;
        private AnimationLayerMixerPlayable layerMixer;
        private bool originalAnimatorApplyRootMotion;
        private bool hasOriginalAnimatorApplyRootMotion;
        private bool suppressNextRootMotionSample;

        public PlayableAnimatorGraph GraphAsset => graphAsset;
        public Animator Animator => animator;
        public bool IsGraphValid => playableGraph.IsValid();
        public bool ApplyRootMotionToTransform
        {
            get => applyRootMotionToTransform;
            set => applyRootMotionToTransform = value;
        }

        public event Action<Vector3, Quaternion> RootMotionEvaluated;
        public static event Action<PlayableGraph>
            GraphVisualizationRequested;
        public static event Action<PlayableGraph>
            GraphVisualizationReleased;

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

        private void OnValidate()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            if (Application.isPlaying && playOnEnable)
                Initialize();
        }

        private void Update()
        {
            if (!Application.isPlaying || !playableGraph.IsValid())
                return;

            float deltaTime = Time.deltaTime;
            UpdateMountedGraphs(deltaTime);
            for (int i = 0; i < runtimeLayers.Count; i++)
                EvaluateLayer(runtimeLayers[i], deltaTime);
        }

        private void OnAnimatorMove()
        {
            if (animator == null)
                return;

            if (!Application.isPlaying || !playableGraph.IsValid())
            {
                if (animator.applyRootMotion)
                    animator.ApplyBuiltinRootMotion();
                return;
            }

            bool suppressRootMotion = suppressNextRootMotionSample;
            suppressNextRootMotionSample = false;

            if (!TryGetRootMotionChannels(
                    out bool applyPositionXZ,
                    out bool applyPositionY,
                    out bool applyRotation))
            {
                return;
            }

            Vector3 deltaPosition = suppressRootMotion
                ? Vector3.zero
                : animator.deltaPosition;
            if (!applyPositionXZ)
            {
                deltaPosition.x = 0f;
                deltaPosition.z = 0f;
            }

            if (!applyPositionY)
                deltaPosition.y = 0f;

            Quaternion deltaRotation = applyRotation && !suppressRootMotion
                ? animator.deltaRotation
                : Quaternion.identity;

            RootMotionEvaluated?.Invoke(deltaPosition, deltaRotation);

            if (!applyRootMotionToTransform)
                return;

            if (deltaPosition.sqrMagnitude > 0f)
                transform.position += deltaPosition;

            if (applyRotation)
                transform.rotation *= deltaRotation;
        }

        private void OnDisable()
        {
            DestroyGraph();
        }

        private void OnDestroy()
        {
            DestroyGraph();
        }

        public void Initialize()
        {
            DestroyGraph();

            if (animator == null)
                animator = GetComponent<Animator>();

            if (animator == null || graphAsset == null)
                return;

            graphAsset.EnsureDefaults();
            CopyDefaultParameters();

            if (clearAnimatorController)
                animator.runtimeAnimatorController = null;

            CaptureAnimatorRootMotionMode();

            playableGraph = PlayableGraph.Create(
                $"{name} - {graphAsset.name}");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            int layerCount = Mathf.Max(1, graphAsset.layers.Count);
            layerMixer = AnimationLayerMixerPlayable.Create(
                playableGraph,
                layerCount);

            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                playableGraph,
                "Playable Animator",
                animator);
            InitializeMountedGraphMixer(output);

            runtimeLayers.Clear();
            for (int i = 0; i < graphAsset.layers.Count; i++)
                BuildLayer(graphAsset.layers[i], i);

            animator.applyRootMotion = GraphUsesRootMotion();

            playableGraph.Play();

            if (showInPlayableGraphVisualizer &&
                graphAsset.showInPlayableGraphVisualizer)
            {
                NotifyGraphVisualization(
                    GraphVisualizationRequested,
                    playableGraph);
            }
        }

        public void RebuildGraph()
        {
            if (Application.isPlaying)
                Initialize();
        }

        public void DestroyGraph()
        {
            if (playableGraph.IsValid())
            {
                NotifyGraphVisualization(
                    GraphVisualizationReleased,
                    playableGraph);
            }

            if (playableGraph.IsValid())
                playableGraph.Destroy();

            DestroyMountedGraphAssets();
            runtimeLayers.Clear();
            RestoreAnimatorRootMotionMode();
        }

        private static void NotifyGraphVisualization(
            Action<PlayableGraph> notification,
            PlayableGraph graph)
        {
            if (notification == null)
                return;

            Delegate[] handlers = notification.GetInvocationList();
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    ((Action<PlayableGraph>)handlers[i]).Invoke(graph);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        public void SetFloat(string parameterName, float value)
        {
            RuntimeParameter parameter = GetOrCreateRuntimeParameter(
                parameterName,
                PlayableParameterType.Float);
            parameter.Type = PlayableParameterType.Float;
            parameter.FloatValue = value;
        }

        public float GetFloat(string parameterName)
        {
            return runtimeParameters.TryGetValue(
                parameterName,
                out RuntimeParameter parameter)
                ? parameter.FloatValue
                : 0f;
        }

        public void SetBool(string parameterName, bool value)
        {
            RuntimeParameter parameter = GetOrCreateRuntimeParameter(
                parameterName,
                PlayableParameterType.Bool);
            parameter.Type = PlayableParameterType.Bool;
            parameter.BoolValue = value;
        }

        public bool GetBool(string parameterName)
        {
            return runtimeParameters.TryGetValue(
                parameterName,
                out RuntimeParameter parameter) &&
                parameter.BoolValue;
        }

        public void SetInteger(string parameterName, int value)
        {
            RuntimeParameter parameter = GetOrCreateRuntimeParameter(
                parameterName,
                PlayableParameterType.Integer);
            parameter.Type = PlayableParameterType.Integer;
            parameter.IntValue = value;
        }

        public int GetInteger(string parameterName)
        {
            return runtimeParameters.TryGetValue(
                parameterName,
                out RuntimeParameter parameter)
                ? parameter.IntValue
                : 0;
        }

        public void SetEnum(string parameterName, string value)
        {
            RuntimeParameter parameter = GetOrCreateRuntimeParameter(
                parameterName,
                PlayableParameterType.Enum);
            parameter.Type = PlayableParameterType.Enum;
            parameter.EnumValue = value;
        }

        public string GetEnum(string parameterName)
        {
            return runtimeParameters.TryGetValue(
                parameterName,
                out RuntimeParameter parameter)
                ? parameter.EnumValue
                : string.Empty;
        }

        public void SetTrigger(string parameterName)
        {
            RuntimeParameter parameter = GetOrCreateRuntimeParameter(
                parameterName,
                PlayableParameterType.Trigger);
            parameter.Type = PlayableParameterType.Trigger;
            parameter.Triggered = true;
            parameter.BoolValue = true;
        }

        public void ResetTrigger(string parameterName)
        {
            if (!runtimeParameters.TryGetValue(
                    parameterName,
                    out RuntimeParameter parameter))
            {
                return;
            }

            parameter.Triggered = false;
            parameter.BoolValue = false;
        }

        public bool GetTrigger(string parameterName)
        {
            return runtimeParameters.TryGetValue(
                       parameterName,
                       out RuntimeParameter parameter) &&
                   parameter.Triggered;
        }

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

        public void GetParameterSnapshot(
            List<PlayableParameterDebugInfo> results)
        {
            results.Clear();

            if (graphAsset != null && graphAsset.parameters != null)
            {
                for (int i = 0; i < graphAsset.parameters.Count; i++)
                {
                    PlayableParameter parameter = graphAsset.parameters[i];
                    if (parameter == null ||
                        string.IsNullOrWhiteSpace(parameter.name))
                    {
                        continue;
                    }

                    AddParameterDebugInfo(parameter.name, results);
                }
            }

            foreach (string name in runtimeParameters.Keys)
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
                    AddParameterDebugInfo(name, results);
            }
        }

        public void GetLayerSnapshot(
            List<PlayableLayerDebugInfo> results)
        {
            results.Clear();

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                RuntimeLayer runtimeLayer = runtimeLayers[i];
                PlayableLayerDebugInfo layerInfo =
                    new PlayableLayerDebugInfo
                    {
                        Name = runtimeLayer.Definition.name,
                        Weight = runtimeLayer.Weight,
                        ActiveState = runtimeLayer.ActiveState != null
                            ? runtimeLayer.ActiveState.Path
                            : "(none)"
                    };

                for (int j = 0; j < runtimeLayer.States.Count; j++)
                {
                    RuntimeState runtimeState = runtimeLayer.States[j];
                    PlayableStateDebugInfo stateInfo =
                        new PlayableStateDebugInfo
                        {
                            Name = runtimeState.Path,
                            Output = runtimeState.Definition.output,
                            IsActive = runtimeState == runtimeLayer.ActiveState,
                            Weight = runtimeState.Weight
                        };

                    for (int k = 0; k < runtimeState.Motions.Count; k++)
                    {
                        RuntimeMotion runtimeMotion = runtimeState.Motions[k];
                        stateInfo.Motions.Add(
                            new PlayableMotionDebugInfo
                            {
                                Name = runtimeMotion.Definition.DisplayName,
                                ClipName = runtimeMotion.Definition.clip != null
                                    ? runtimeMotion.Definition.clip.name
                                    : "(none)",
                                Weight = runtimeMotion.Weight,
                                Threshold = runtimeMotion.Definition.threshold,
                                Position = runtimeMotion.Definition.position
                            });
                    }

                    layerInfo.States.Add(stateInfo);
                }

                results.Add(layerInfo);
            }
        }

        private void BuildLayer(PlayableLayer definition, int layerIndex)
        {
            if (definition == null)
                return;

            RuntimeLayer runtimeLayer = new RuntimeLayer(
                definition,
                graphAsset,
                0);
            int stateCount = Mathf.Max(1, CountLeafStates(definition.states));
            runtimeLayer.StateMixer = AnimationMixerPlayable.Create(
                playableGraph,
                stateCount);

            playableGraph.Connect(
                runtimeLayer.StateMixer,
                0,
                layerMixer,
                layerIndex);

            runtimeLayer.Weight = Mathf.Clamp01(definition.weight);
            runtimeLayer.LocalWeight = runtimeLayer.Weight;
            runtimeLayer.MixerInputIndex = layerIndex;
            layerMixer.SetInputWeight(layerIndex, runtimeLayer.Weight);
            layerMixer.SetLayerAdditive((uint)layerIndex, definition.additive);

            if (definition.avatarMask != null)
                layerMixer.SetLayerMaskFromAvatarMask(
                    (uint)layerIndex,
                    definition.avatarMask);

            BuildLeafStates(runtimeLayer, definition.states, string.Empty);

            runtimeLayers.Add(runtimeLayer);
            RuntimeState defaultState = FindDefaultState(runtimeLayer);
            if (defaultState != null)
                EnterState(runtimeLayer, defaultState, true);
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
                state.Playable = AnimationMixerPlayable.Create(playableGraph, 0);
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
                    playableGraph.Connect(
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
                state.Playable = AnimationMixerPlayable.Create(playableGraph, 0);
                return;
            }

            AnimationClipPlayable clipPlayable =
                AnimationClipPlayable.Create(playableGraph, definition.clip);
            clipPlayable.SetApplyFootIK(definition.applyFootIK);
            clipPlayable.SetSpeed(Mathf.Max(0.01f, definition.speed));
            state.Playable = clipPlayable;
        }

        private void BuildBlendState(RuntimeState state)
        {
            List<PlayableMotion> motions = state.Definition.motions;
            int inputCount = motions != null ? Mathf.Max(1, motions.Count) : 1;
            state.MotionMixer = AnimationMixerPlayable.Create(
                playableGraph,
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
                    AnimationClipPlayable.Create(playableGraph, motion.clip);
                clipPlayable.SetApplyFootIK(motion.applyFootIK);
                clipPlayable.SetSpeed(Mathf.Max(0.01f, motion.speed));
                clipPlayable.SetTime(
                    GetCycleOffsetTime(motion.clip, motion.cycleOffset));

                playableGraph.Connect(
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
                playableGraph,
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
                    AnimationClipPlayable.Create(playableGraph, motion.clip);
                clipPlayable.SetApplyFootIK(motion.applyFootIK);
                clipPlayable.SetSpeed(
                    stateSpeed * Mathf.Max(0.01f, motion.speed));

                playableGraph.Connect(
                    clipPlayable,
                    0,
                    state.MotionMixer,
                    i);
                state.MotionMixer.SetInputWeight(i, 0f);
                runtimeMotion.Connected = true;
                runtimeMotion.Playable = clipPlayable;
            }
        }

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
                desiredState != layer.ActiveState &&
                CanChangeState(layer, layer.ActiveState, desiredState))
            {
                EnterState(layer, desiredState);
            }
            else if (desiredState == layer.ActiveState)
            {
                ConsumeStateTriggers(desiredState);
            }

            for (int i = 0; i < layer.States.Count; i++)
                ApplyStateWeight(layer, layer.States[i], deltaTime);

            for (int i = 0; i < layer.States.Count; i++)
            {
                RuntimeState state = layer.States[i];
                if (state == layer.ActiveState || state.Weight > 0.0001f)
                    ApplyMotionWeights(state, deltaTime);
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

                if (!ConditionsPass(definition.conditions))
                    continue;

                bool usesActiveTrigger =
                    HasActiveTriggerCondition(definition.conditions);

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
                    !ConditionsPass(candidate.conditions))
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
            ConsumeStateTriggers(state);
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
            if (desiredState == null || currentState == desiredState)
                return false;

            if (currentState == null)
                return true;

            if (HasActiveTriggerCondition(
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
            if (sourceLayer == null || incomingState == null)
                return;

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
                        out PlayableInterruption rule))
                {
                    continue;
                }

                if (rule.scope != PlayableInterruptionScope.OtherLayers &&
                    rule.scope != PlayableInterruptionScope.AllLayers &&
                    rule.scope != PlayableInterruptionScope.SpecificState)
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
                        false,
                        rule.fadeDurationOverride);
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

        private void ApplyMotionWeights(RuntimeState state, float deltaTime)
        {
            if (state == null || !state.HasMotionMixer)
                return;

            switch (state.Definition.output)
            {
                case PlayableStateOutput.Playlist:
                    ApplyPlaylist(state);
                    break;
                case PlayableStateOutput.BlendTree1D:
                    ApplyBlendTree1D(state);
                    break;
                case PlayableStateOutput.BlendTree2D:
                    ApplyBlendTree2D(state);
                    break;
                case PlayableStateOutput.DirectBlend:
                    ApplyDirectBlend(state);
                    break;
            }

            if (UsesSynchronizedBlendTime(state.Definition))
                UpdateSynchronizedBlendPlayback(state);
        }

        private void ApplyBlendTree1D(RuntimeState state)
        {
            ClearMotionWeights(state);

            float value = GetFloat(state.Definition.blendParameterX);
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

        private void ApplyBlendTree2D(RuntimeState state)
        {
            ClearMotionWeights(state);

            Vector2 value = new Vector2(
                GetFloat(state.Definition.blendParameterX),
                GetFloat(state.Definition.blendParameterY));

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

        private void ApplyDirectBlend(RuntimeState state)
        {
            float total = 0f;
            for (int i = 0; i < state.Motions.Count; i++)
            {
                RuntimeMotion motion = state.Motions[i];
                motion.Weight = motion.Connected
                    ? Mathf.Clamp01(GetFloat(motion.Definition.directParameter))
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

        private void ApplyPlaylist(RuntimeState state, bool forceSeek = false)
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

        private static float GetPlaylistDuration(RuntimeState state)
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

        private static float GetWeightedBlendDuration(RuntimeState state)
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
                   HasReachedExitTime(currentState);
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

            List<PlayableInterruption> interruptions =
                incomingState.Definition.interruptions;
            for (int i = 0; i < interruptions.Count; i++)
            {
                PlayableInterruption rule = interruptions[i];
                if (rule == null || !rule.enabled)
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

                matchingRule = rule;
                return true;
            }

            return false;
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

            switch (rule.scope)
            {
                case PlayableInterruptionScope.Self:
                    return sameState;
                case PlayableInterruptionScope.SameLayer:
                    return sameLayer && !sameState;
                case PlayableInterruptionScope.OtherLayers:
                    return !sameLayer;
                case PlayableInterruptionScope.AllLayers:
                    return true;
                case PlayableInterruptionScope.SpecificState:
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
            bool layerMatches =
                string.IsNullOrWhiteSpace(rule.layerName) ||
                string.Equals(
                    rule.layerName,
                    currentLayer.Definition.name,
                    StringComparison.OrdinalIgnoreCase);
            bool stateMatches =
                string.IsNullOrWhiteSpace(rule.stateName) ||
                string.Equals(
                    rule.stateName,
                    currentState.Definition.name,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    rule.stateName,
                    currentState.Path,
                    StringComparison.OrdinalIgnoreCase);

            return layerMatches && stateMatches;
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
                ApplyPlaylist(state, true);
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

                    double time = GetCycleOffsetTime(
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
                return GetPlaylistDuration(state);

            if (state.HasMotionMixer)
                return GetWeightedBlendDuration(state);

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
            state.PreviousNormalizedTime = normalizedTime;
        }

        private void DispatchStateEvents(
            RuntimeLayer layer,
            RuntimeState state,
            float normalizedTime)
        {
            if (state.Definition.events == null || state.Definition.events.Count == 0)
                return;

            EnsureEventState(state);
            float duration = Mathf.Max(0.01f, GetCurrentStateDuration(state));

            for (int i = 0; i < state.Definition.events.Count; i++)
            {
                PlayableStateEvent stateEvent = state.Definition.events[i];
                if (stateEvent == null || !stateEvent.enabled)
                    continue;

                float triggerTime = stateEvent.timeMode ==
                                    PlayableEventTimeMode.Seconds
                    ? stateEvent.seconds / duration
                    : stateEvent.normalizedTime;
                triggerTime = Mathf.Max(0f, triggerTime);

                bool shouldFire = stateEvent.everyLoop && state.Definition.loop
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

        private void NotifyStateEvent(
            RuntimeLayer layer,
            RuntimeState state,
            PlayableStateEvent stateEvent)
        {
            if (stateEvent.callback != null)
            {
                try
                {
                    stateEvent.callback.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

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
                    behaviour.OnPlayableStateEvent(
                        this,
                        layer.Definition.name,
                        state.Path,
                        stateEvent.name);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private bool GraphUsesRootMotion()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                RuntimeLayer layer = runtimeLayers[i];
                if (layer == null)
                    continue;

                for (int j = 0; j < layer.States.Count; j++)
                {
                    RuntimeState state = layer.States[j];
                    if (state != null &&
                        state.Definition != null &&
                        state.Definition.applyRootMotion)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CaptureAnimatorRootMotionMode()
        {
            if (animator == null || hasOriginalAnimatorApplyRootMotion)
                return;

            originalAnimatorApplyRootMotion = animator.applyRootMotion;
            hasOriginalAnimatorApplyRootMotion = true;
        }

        private void RestoreAnimatorRootMotionMode()
        {
            if (animator == null || !hasOriginalAnimatorApplyRootMotion)
                return;

            animator.applyRootMotion = originalAnimatorApplyRootMotion;
            hasOriginalAnimatorApplyRootMotion = false;
        }

        private bool TryGetRootMotionChannels(
            out bool applyPositionXZ,
            out bool applyPositionY,
            out bool applyRotation)
        {
            applyPositionXZ = false;
            applyPositionY = false;
            applyRotation = false;
            bool hasRootMotionState = false;

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                RuntimeLayer layer = runtimeLayers[i];
                if (layer == null || layer.Weight <= 0.0001f)
                    continue;

                RuntimeState state = layer.ActiveState;
                if (state == null ||
                    state.Definition == null ||
                    !state.Definition.applyRootMotion)
                {
                    continue;
                }

                hasRootMotionState = true;
                applyPositionXZ |= state.Definition.rootMotionPositionXZ;
                applyPositionY |= state.Definition.rootMotionPositionY;
                applyRotation |= state.Definition.rootMotionRotation;
            }

            return hasRootMotionState;
        }

        private bool ConditionsPass(List<PlayableCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0)
                return true;

            for (int i = 0; i < conditions.Count; i++)
            {
                PlayableCondition condition = conditions[i];
                if (condition == null || condition.mode == PlayableConditionMode.None)
                    continue;

                if (string.IsNullOrWhiteSpace(condition.parameter) ||
                    !runtimeParameters.TryGetValue(
                        condition.parameter,
                        out RuntimeParameter parameter))
                {
                    return false;
                }

                if (!ConditionPasses(condition, parameter))
                    return false;
            }

            return true;
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
                    return CompareBool(
                        parameter.Triggered,
                        condition.mode,
                        condition.boolValue);
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

        private void CopyDefaultParameters()
        {
            runtimeParameters.Clear();

            if (graphAsset.parameters == null)
                return;

            for (int i = 0; i < graphAsset.parameters.Count; i++)
            {
                PlayableParameter definition = graphAsset.parameters[i];
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.name))
                {
                    continue;
                }

                runtimeParameters[definition.name] =
                    new RuntimeParameter(definition);
            }
        }

        private RuntimeParameter GetOrCreateRuntimeParameter(
            string parameterName,
            PlayableParameterType type)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                parameterName = "Parameter";

            if (!runtimeParameters.TryGetValue(
                    parameterName,
                    out RuntimeParameter parameter))
            {
                parameter = new RuntimeParameter
                {
                    Type = type
                };
                runtimeParameters.Add(parameterName, parameter);
            }

            return parameter;
        }

        private bool HasActiveTriggerCondition(
            List<PlayableCondition> conditions)
        {
            if (conditions == null)
                return false;

            for (int i = 0; i < conditions.Count; i++)
            {
                PlayableCondition condition = conditions[i];
                if (condition == null ||
                    string.IsNullOrWhiteSpace(condition.parameter) ||
                    !runtimeParameters.TryGetValue(
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

        private void ConsumeStateTriggers(RuntimeState state)
        {
            if (state == null || state.Definition == null)
                return;

            List<PlayableCondition> conditions = state.Definition.conditions;
            if (conditions == null)
                return;

            for (int i = 0; i < conditions.Count; i++)
            {
                PlayableCondition condition = conditions[i];
                if (condition == null ||
                    string.IsNullOrWhiteSpace(condition.parameter) ||
                    !runtimeParameters.TryGetValue(
                        condition.parameter,
                        out RuntimeParameter parameter) ||
                    parameter.Type != PlayableParameterType.Trigger ||
                    !parameter.Triggered ||
                    !ConditionPasses(condition, parameter))
                {
                    continue;
                }

                parameter.Triggered = false;
                parameter.BoolValue = false;
            }
        }

        private void AddParameterDebugInfo(
            string parameterName,
            List<PlayableParameterDebugInfo> results)
        {
            if (!runtimeParameters.TryGetValue(
                    parameterName,
                    out RuntimeParameter parameter))
            {
                return;
            }

            results.Add(
                new PlayableParameterDebugInfo
                {
                    Name = parameterName,
                    Type = parameter.Type,
                    FloatValue = parameter.FloatValue,
                    BoolValue = parameter.BoolValue,
                    IntValue = parameter.IntValue,
                    Triggered = parameter.Triggered,
                    EnumValue = parameter.EnumValue
                });

            PlayableParameter definition = FindParameterDefinition(parameterName);
            if (definition != null && definition.enumOptions != null)
                results[results.Count - 1].EnumOptions.AddRange(
                    definition.enumOptions);
        }

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
                        : GetCycleOffsetTime(
                            motion.Definition.clip,
                            motion.Definition.cycleOffset));
            }

            state.PlaylistMotionIndex = -1;
            state.PlaylistCycle = -1;
        }

        private static double GetCycleOffsetTime(
            AnimationClip clip,
            float cycleOffset)
        {
            if (clip == null)
                return 0d;

            return Mathf.Repeat(cycleOffset, 1f) *
                   Mathf.Max(0.01f, clip.length);
        }
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

            AddGraphParameters(graph);
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
            if (definition == null)
                return null;

            RuntimeLayer runtimeLayer = new RuntimeLayer(
                definition,
                sourceGraph,
                mountHandle);
            int stateCount = Mathf.Max(1, CountLeafStates(definition.states));
            runtimeLayer.StateMixer = AnimationMixerPlayable.Create(
                playableGraph,
                stateCount);

            BuildLeafStates(runtimeLayer, definition.states, string.Empty);
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

        private void AddGraphParameters(PlayableAnimatorGraph graph)
        {
            if (graph == null || graph.parameters == null)
                return;

            for (int i = 0; i < graph.parameters.Count; i++)
            {
                PlayableParameter definition = graph.parameters[i];
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.name) ||
                    runtimeParameters.ContainsKey(definition.name))
                {
                    continue;
                }

                runtimeParameters.Add(
                    definition.name,
                    new RuntimeParameter(definition));
            }
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

        private sealed class MountedGraph
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
                BoolValue = definition.boolValue;
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

        private sealed class RuntimeLayer
        {
            public readonly PlayableLayer Definition;
            public readonly PlayableAnimatorGraph SourceGraph;
            public readonly int MountHandle;
            public readonly List<RuntimeState> States = new List<RuntimeState>();
            public readonly Dictionary<PlayableState, RuntimeState>
                StateLookup =
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

        private sealed class RuntimeState
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

        private sealed class RuntimeMotion
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
}
