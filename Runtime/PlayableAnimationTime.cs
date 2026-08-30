using UnityEngine;

namespace Playgraph
{
    internal static class PlayableAnimationTime
    {
        public static double GetCycleOffsetTime(
            AnimationClip clip,
            float cycleOffset)
        {
            if (clip == null)
                return 0d;

            return Mathf.Repeat(cycleOffset, 1f) *
                   Mathf.Max(0.01f, clip.length);
        }
    }
}
