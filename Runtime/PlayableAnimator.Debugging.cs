using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
        public void GetParameterSnapshot(
            List<PlayableParameterDebugInfo> results)
        {
            parameterStore.GetSnapshot(
                graphAsset,
                FindParameterDefinition,
                results);
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

    }
}
