using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace Playgraph
{
    [Serializable]
    public sealed class PlayableAnimatorEventBinding
    {
        [SerializeField] private string eventName = "Event";
        [SerializeField] private UnityEvent response = new UnityEvent();

        public string EventName => eventName;
        public UnityEvent Response => response;

        internal void EnsureDefaults()
        {
            if (response == null)
                response = new UnityEvent();
        }
    }

    [DefaultExecutionOrder(56)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("Play Graph/Playable Animator")]
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

        [Header("Events")]
        [SerializeField] private List<PlayableAnimatorEventBinding> eventBindings =
            new List<PlayableAnimatorEventBinding>();

        private readonly PlayableParameterStore parameterStore =
            new PlayableParameterStore();
        private readonly PlayableMotionEvaluator motionEvaluator =
            new PlayableMotionEvaluator();
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
        private PlayableRuntimeGraphBuilder graphBuilder;
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

        public IReadOnlyList<PlayableAnimatorEventBinding> EventBindings =>
            eventBindings;

        public event Action<Vector3, Quaternion> RootMotionEvaluated;
        public event Action<
            string,
            string,
            string,
            PlayableStateEventType,
            PlayableStateEventTrigger> StateEventRaised;
        public static event Action<PlayableGraph>
            GraphVisualizationRequested;
        public static event Action<PlayableGraph>
            GraphVisualizationReleased;

        private void Reset()
        {
            animator = GetComponent<Animator>();
            EnsureEventBindings();
        }

        private void OnValidate()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            EnsureEventBindings();
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
            EnsureEventBindings();

            if (animator == null)
                animator = GetComponent<Animator>();

            if (animator == null || graphAsset == null)
                return;

            graphAsset.EnsureDefaults();
            parameterStore.Reset(graphAsset);

            if (clearAnimatorController)
                animator.runtimeAnimatorController = null;

            CaptureAnimatorRootMotionMode();

            playableGraph = PlayableGraph.Create(
                $"{name} - {graphAsset.name}");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            graphBuilder = new PlayableRuntimeGraphBuilder(playableGraph);

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

        private void EnsureEventBindings()
        {
            if (eventBindings == null)
                eventBindings = new List<PlayableAnimatorEventBinding>();

            for (int i = 0; i < eventBindings.Count; i++)
                eventBindings[i]?.EnsureDefaults();
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

            graphBuilder = null;
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
    }
}
