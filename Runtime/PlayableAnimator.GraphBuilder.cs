using UnityEngine;
using UnityEngine.Playables;

namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
        private void BuildLayer(PlayableLayer definition, int layerIndex)
        {
            RuntimeLayer runtimeLayer = graphBuilder?.BuildLayer(
                definition,
                graphAsset,
                0);
            if (runtimeLayer == null)
                return;

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
            {
                layerMixer.SetLayerMaskFromAvatarMask(
                    (uint)layerIndex,
                    definition.avatarMask);
            }

            runtimeLayers.Add(runtimeLayer);
            RuntimeState defaultState = FindDefaultState(runtimeLayer);
            if (defaultState != null)
                EnterState(runtimeLayer, defaultState, true);
        }
    }
}
