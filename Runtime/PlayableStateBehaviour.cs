using UnityEngine;

namespace Playgraph
{
    public abstract class PlayableStateBehaviour : ScriptableObject
    {
        public virtual void OnPlayableStateEnter(
            PlayableAnimator animator,
            string layerName,
            string stateName)
        {
        }

        public virtual void OnPlayableStateUpdate(
            PlayableAnimator animator,
            string layerName,
            string stateName,
            float normalizedTime)
        {
        }

        public virtual void OnPlayableStateExit(
            PlayableAnimator animator,
            string layerName,
            string stateName)
        {
        }

        public virtual void OnPlayableStateEvent(
            PlayableAnimator animator,
            string layerName,
            string stateName,
            string eventName)
        {
        }
    }
}
