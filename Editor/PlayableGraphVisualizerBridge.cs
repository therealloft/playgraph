using System;
using System.Reflection;
using UnityEditor;
using UnityEngine.Playables;

namespace Playgraph
{
    [InitializeOnLoad]
    internal static class PlayableGraphVisualizerBridge
    {
        private const BindingFlags StaticPublic =
            BindingFlags.Public | BindingFlags.Static;

        private static MethodInfo showMethod;
        private static MethodInfo hideMethod;
        private static bool methodsResolved;

        static PlayableGraphVisualizerBridge()
        {
            PlayableAnimator.GraphVisualizationRequested += Show;
            PlayableAnimator.GraphVisualizationReleased += Hide;
            AssemblyReloadEvents.beforeAssemblyReload += Unregister;
        }

        private static void Unregister()
        {
            PlayableAnimator.GraphVisualizationRequested -= Show;
            PlayableAnimator.GraphVisualizationReleased -= Hide;
            AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
        }

        private static void Show(PlayableGraph graph)
        {
            ResolveMethods();
            showMethod?.Invoke(null, new object[] { graph });
        }

        private static void Hide(PlayableGraph graph)
        {
            ResolveMethods();
            hideMethod?.Invoke(null, new object[] { graph });
        }

        private static void ResolveMethods()
        {
            if (methodsResolved)
                return;

            methodsResolved = true;
            Type clientType = FindClientType();
            if (clientType == null)
                return;

            Type[] parameters = { typeof(PlayableGraph) };
            showMethod = clientType.GetMethod(
                "Show",
                StaticPublic,
                null,
                parameters,
                null);
            hideMethod = clientType.GetMethod(
                "Hide",
                StaticPublic,
                null,
                parameters,
                null);
        }

        private static Type FindClientType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(
                    "GraphVisualizerClient",
                    false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
