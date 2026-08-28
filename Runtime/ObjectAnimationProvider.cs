using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Playgraph
{
    public enum ObjectAnimationActionSource
    {
        GraphState,
        AnimationClip
    }

    [Serializable]
    public sealed class ObjectAnimationAction
    {
        public string name = "Action";
        public ObjectAnimationActionSource source =
            ObjectAnimationActionSource.GraphState;

        public string layerName;
        public string stateName;
        public bool oneShot = true;

        public PlayableExternalClipSettings clip =
            new PlayableExternalClipSettings();

        public string objectAnimatorTrigger;
    }

    [Serializable]
    public sealed class ObjectAnimationStringEvent : UnityEvent<string>
    {
    }

    [AddComponentMenu("Play Graph/Object Animation Provider")]
    [DisallowMultipleComponent]
    public sealed class ObjectAnimationProvider : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string animationId;

        [SerializeField] private PlayableAnimatorGraph characterGraph;
        [SerializeField] private string layerName;
        [SerializeField] private string enterState;
        [SerializeField] private string loopState;
        [SerializeField] private string exitState;
        [SerializeField, Min(0f)] private float blendIn = 0.15f;
        [SerializeField, Min(0f)] private float blendOut = 0.15f;

        [SerializeField]
        private List<ObjectAnimationAction> actions =
            new List<ObjectAnimationAction>();

        [SerializeField] private Animator objectAnimator;
        [SerializeField] private string enterObjectTrigger;
        [SerializeField] private string exitObjectTrigger;

        [SerializeField]
        private UnityEvent interactionStarted =
            new UnityEvent();
        [SerializeField]
        private UnityEvent interactionEnded =
            new UnityEvent();
        [SerializeField]
        private ObjectAnimationStringEvent actionPlayed =
            new ObjectAnimationStringEvent();

        public string AnimationId => animationId;
        public PlayableAnimatorGraph CharacterGraph => characterGraph;
        public string LayerName => layerName;
        public string EnterState => enterState;
        public string LoopState => loopState;
        public string ExitState => exitState;
        public float BlendIn => blendIn;
        public float BlendOut => blendOut;
        public IReadOnlyList<ObjectAnimationAction> Actions => actions;

        private void Reset()
        {
            objectAnimator = GetComponent<Animator>();
            EnsureAnimationId();
        }

        private void OnValidate()
        {
            blendIn = Mathf.Max(0f, blendIn);
            blendOut = Mathf.Max(0f, blendOut);
            EnsureAnimationId();

            if (actions == null)
                actions = new List<ObjectAnimationAction>();
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] == null)
                    actions[i] = new ObjectAnimationAction();
                if (actions[i].clip == null)
                    actions[i].clip = new PlayableExternalClipSettings();
            }
        }

        public ObjectAnimationAction FindAction(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName) || actions == null)
                return null;

            for (int i = 0; i < actions.Count; i++)
            {
                ObjectAnimationAction action = actions[i];
                if (action != null &&
                    string.Equals(
                        action.name,
                        actionName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return action;
                }
            }

            return null;
        }

        internal void NotifyInteractionStarted()
        {
            SetObjectTrigger(enterObjectTrigger);
            interactionStarted?.Invoke();
        }

        internal void NotifyInteractionEnded()
        {
            SetObjectTrigger(exitObjectTrigger);
            interactionEnded?.Invoke();
        }

        internal void NotifyActionPlayed(ObjectAnimationAction action)
        {
            if (action == null)
                return;

            SetObjectTrigger(action.objectAnimatorTrigger);
            actionPlayed?.Invoke(action.name);
        }

        private void SetObjectTrigger(string triggerName)
        {
            if (objectAnimator != null && !string.IsNullOrWhiteSpace(triggerName))
                objectAnimator.SetTrigger(triggerName);
        }

        private void EnsureAnimationId()
        {
            if (string.IsNullOrWhiteSpace(animationId))
                animationId = Guid.NewGuid().ToString("N");
        }
    }
}
