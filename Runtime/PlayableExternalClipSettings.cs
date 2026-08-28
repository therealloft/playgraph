using System;
using UnityEngine;

namespace Playgraph
{
    [Serializable]
    public sealed class PlayableExternalClipSettings
    {
        public string name = "Object Animation";
        public AnimationClip clip;
        [Min(0.01f)] public float speed = 1f;
        public bool applyFootIK = true;
        public bool applyRootMotion;
        public bool rootMotionPositionXZ = true;
        public bool rootMotionPositionY;
        public bool rootMotionRotation = true;
        public AvatarMask avatarMask;
        public bool additive;
        [Range(0f, 1f)] public float layerWeight = 1f;
        [Min(0f)] public float fadeIn = 0.1f;
        [Min(0f)] public float fadeOut = 0.1f;
    }
}
