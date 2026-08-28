using System;
using System.Collections.Generic;
using UnityEngine;

namespace Playgraph
{
    [DefaultExecutionOrder(57)]
    [AddComponentMenu("Play Graph/Object Animation Player")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayableAnimator))]
    public sealed class ObjectAnimationPlayer : MonoBehaviour
    {
        [SerializeField] private PlayableAnimator playableAnimator;
        [SerializeField] private ObjectAnimationProvider defaultProvider;

        private readonly List<ClipPlayback> clipPlaybacks =
            new List<ClipPlayback>();

        private ObjectAnimationProvider activeProvider;
        private int sessionHandle;
        private string pendingExitState;
        private string pendingExitLayer;
        private bool ending;

        public PlayableAnimator Animator => playableAnimator;
        public ObjectAnimationProvider ActiveProvider => activeProvider;
        public string ActiveProviderId => activeProvider != null
            ? activeProvider.AnimationId
            : string.Empty;
        public bool IsActive => activeProvider != null;
        public bool IsEnding => ending;

        public event Action<ObjectAnimationProvider> InteractionStarted;
        public event Action<ObjectAnimationProvider> InteractionEnded;
        public event Action<ObjectAnimationProvider, string> ActionPlayed;

        private void Reset()
        {
            playableAnimator = GetComponent<PlayableAnimator>();
        }

        private void Awake()
        {
            if (playableAnimator == null)
                playableAnimator = GetComponent<PlayableAnimator>();
        }

        private void Update()
        {
            UpdateSessionExit();
            UpdateClipPlaybacks();
        }

        private void OnDisable()
        {
            Cancel();
        }

        public bool Begin()
        {
            return Begin(defaultProvider);
        }

        public bool Begin(ObjectAnimationProvider provider)
        {
            if (provider == null || playableAnimator == null)
                return false;
            if (activeProvider == provider && !ending)
                return true;

            CancelSession();

            activeProvider = provider;
            ending = false;
            pendingExitState = string.Empty;
            pendingExitLayer = string.Empty;

            PlayableAnimatorGraph graph = provider.CharacterGraph;
            if (graph != null)
            {
                bool hasExplicitEntry =
                    !string.IsNullOrWhiteSpace(provider.EnterState) ||
                    !string.IsNullOrWhiteSpace(provider.LoopState);
                sessionHandle = playableAnimator.MountGraph(
                    graph,
                    provider.BlendIn,
                    !hasExplicitEntry);
                if (sessionHandle == 0)
                {
                    activeProvider = null;
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(provider.LoopState))
                {
                    playableAnimator.SetMountedReturnState(
                        sessionHandle,
                        provider.LoopState,
                        provider.LayerName);
                }

                if (!string.IsNullOrWhiteSpace(provider.EnterState))
                {
                    if (!playableAnimator.TriggerMountedOneShot(
                            sessionHandle,
                            provider.EnterState,
                            provider.LayerName))
                    {
                        Debug.LogWarning(
                            $"Object animation enter state '{provider.EnterState}' " +
                            $"was not found on {provider.name}.",
                            provider);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(provider.LoopState))
                {
                    playableAnimator.PlayMountedState(
                        sessionHandle,
                        provider.LoopState,
                        provider.LayerName);
                }
            }

            provider.NotifyInteractionStarted();
            InteractionStarted?.Invoke(provider);
            return true;
        }

        public bool PlayAction(string actionName)
        {
            if (activeProvider == null || ending)
                return false;

            ObjectAnimationAction action = activeProvider.FindAction(actionName);
            if (action == null)
                return false;

            bool played;
            switch (action.source)
            {
                case ObjectAnimationActionSource.AnimationClip:
                    played = PlayClipAction(action);
                    break;
                default:
                    played = PlayGraphAction(action);
                    break;
            }

            if (!played)
                return false;

            activeProvider.NotifyActionPlayed(action);
            ActionPlayed?.Invoke(activeProvider, action.name);
            return true;
        }

        public bool End()
        {
            if (activeProvider == null || ending)
                return false;

            ending = true;
            activeProvider.NotifyInteractionEnded();

            if (sessionHandle != 0 &&
                !string.IsNullOrWhiteSpace(activeProvider.ExitState))
            {
                pendingExitState = activeProvider.ExitState;
                pendingExitLayer = activeProvider.LayerName;
                playableAnimator.ClearMountedReturnState(
                    sessionHandle,
                    pendingExitLayer);
                if (playableAnimator.TriggerMountedOneShot(
                        sessionHandle,
                        pendingExitState,
                        pendingExitLayer))
                {
                    return true;
                }

                Debug.LogWarning(
                    $"Object animation exit state '{pendingExitState}' " +
                    $"was not found on {activeProvider.name}.",
                    activeProvider);
            }

            CompleteSession();
            return true;
        }

        public void Cancel()
        {
            CancelSession();

            for (int i = 0; i < clipPlaybacks.Count; i++)
            {
                if (playableAnimator != null)
                    playableAnimator.UnmountGraph(clipPlaybacks[i].Handle, 0f);
            }

            clipPlaybacks.Clear();
        }

        private bool PlayGraphAction(ObjectAnimationAction action)
        {
            if (sessionHandle == 0 || string.IsNullOrWhiteSpace(action.stateName))
                return false;

            string layer = string.IsNullOrWhiteSpace(action.layerName)
                ? activeProvider.LayerName
                : action.layerName;
            return action.oneShot
                ? playableAnimator.TriggerMountedOneShot(
                    sessionHandle,
                    action.stateName,
                    layer)
                : playableAnimator.PlayMountedState(
                    sessionHandle,
                    action.stateName,
                    layer);
        }

        private bool PlayClipAction(ObjectAnimationAction action)
        {
            if (action.clip == null || action.clip.clip == null)
                return false;

            int handle = playableAnimator.MountClip(action.clip);
            if (handle == 0)
                return false;

            clipPlaybacks.Add(new ClipPlayback
            {
                Handle = handle,
                FadeOut = Mathf.Max(0f, action.clip.fadeOut)
            });
            return true;
        }

        private void UpdateSessionExit()
        {
            if (!ending || activeProvider == null)
                return;

            if (sessionHandle == 0 ||
                string.IsNullOrWhiteSpace(pendingExitState) ||
                playableAnimator.IsMountedStateComplete(
                    sessionHandle,
                    pendingExitState,
                    pendingExitLayer))
            {
                CompleteSession();
            }
        }

        private void UpdateClipPlaybacks()
        {
            for (int i = clipPlaybacks.Count - 1; i >= 0; i--)
            {
                ClipPlayback playback = clipPlaybacks[i];
                if (!playableAnimator.IsGraphMounted(playback.Handle))
                {
                    clipPlaybacks.RemoveAt(i);
                    continue;
                }

                if (!playback.Releasing &&
                    playableAnimator.IsMountedGraphComplete(playback.Handle))
                {
                    playback.Releasing = true;
                    playableAnimator.UnmountGraph(
                        playback.Handle,
                        playback.FadeOut);
                }
            }
        }

        private void CompleteSession()
        {
            ObjectAnimationProvider completedProvider = activeProvider;
            if (sessionHandle != 0 && playableAnimator != null)
            {
                float fadeOut = completedProvider != null
                    ? completedProvider.BlendOut
                    : 0f;
                playableAnimator.UnmountGraph(sessionHandle, fadeOut);
            }

            activeProvider = null;
            sessionHandle = 0;
            pendingExitState = string.Empty;
            pendingExitLayer = string.Empty;
            ending = false;

            if (completedProvider != null)
                InteractionEnded?.Invoke(completedProvider);
        }

        private void CancelSession()
        {
            ObjectAnimationProvider cancelledProvider = activeProvider;
            if (sessionHandle != 0 && playableAnimator != null)
                playableAnimator.UnmountGraph(sessionHandle, 0f);

            activeProvider = null;
            sessionHandle = 0;
            pendingExitState = string.Empty;
            pendingExitLayer = string.Empty;
            ending = false;

            if (cancelledProvider != null)
            {
                cancelledProvider.NotifyInteractionEnded();
                InteractionEnded?.Invoke(cancelledProvider);
            }
        }

        private sealed class ClipPlayback
        {
            public int Handle;
            public float FadeOut;
            public bool Releasing;
        }
    }
}
