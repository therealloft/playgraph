using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
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
    }
}
