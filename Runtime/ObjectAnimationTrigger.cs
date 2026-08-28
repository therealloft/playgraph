using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Playgraph
{
    [AddComponentMenu("Play Graph/Object Animation Trigger")]
    [DisallowMultipleComponent]
    public sealed class ObjectAnimationTrigger : MonoBehaviour
    {
        [SerializeField] private ObjectAnimationProvider provider;
        [SerializeField] private string actionName = "Use";
        [SerializeField] private bool playOnTriggerEnter = true;
        [SerializeField] private bool endSessionAfterAction = true;

        private readonly Dictionary<ObjectAnimationPlayer, int> overlaps =
            new Dictionary<ObjectAnimationPlayer, int>();

        public ObjectAnimationProvider Provider => provider;
        public string ActionName => actionName;

        public event Action<ObjectAnimationPlayer> Played;

        private void Reset()
        {
            provider = GetComponent<ObjectAnimationProvider>();
        }

        private void Awake()
        {
            if (provider == null)
                provider = GetComponent<ObjectAnimationProvider>();
        }

        private void OnDisable()
        {
            overlaps.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!playOnTriggerEnter)
                return;

            ObjectAnimationPlayer player = FindPlayer(other);
            if (player == null)
                return;

            if (overlaps.TryGetValue(player, out int count))
            {
                overlaps[player] = count + 1;
                return;
            }

            overlaps.Add(player, 1);
            Play(player);
        }

        private void OnTriggerExit(Collider other)
        {
            ObjectAnimationPlayer player = FindPlayer(other);
            if (player == null || !overlaps.TryGetValue(player, out int count))
                return;

            if (count <= 1)
                overlaps.Remove(player);
            else
                overlaps[player] = count - 1;
        }

        public bool Play(Collider actorCollider)
        {
            return Play(FindPlayer(actorCollider));
        }

        public bool Play(GameObject actor)
        {
            return actor != null
                ? Play(actor.GetComponentInParent<ObjectAnimationPlayer>())
                : false;
        }

        public bool Play(ObjectAnimationPlayer player)
        {
            if (player == null || provider == null)
                return false;
            if (!player.Begin(provider))
                return false;
            if (!player.PlayAction(actionName))
            {
                player.Cancel();
                return false;
            }

            if (endSessionAfterAction)
                player.End();

            Played?.Invoke(player);
            return true;
        }

        public void Cancel(ObjectAnimationPlayer player)
        {
            if (player == null || player.ActiveProvider != provider)
                return;

            player.Cancel();
        }

        private static ObjectAnimationPlayer FindPlayer(Collider actorCollider)
        {
            if (actorCollider == null)
                return null;

            ObjectAnimationPlayer player =
                actorCollider.GetComponentInParent<ObjectAnimationPlayer>();
            if (player == null && actorCollider.attachedRigidbody != null)
            {
                player = actorCollider.attachedRigidbody
                    .GetComponentInParent<ObjectAnimationPlayer>();
            }

            return player;
        }
    }
}
