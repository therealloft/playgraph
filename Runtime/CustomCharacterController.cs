using UnityEngine;
using UnityEngine.Serialization;

namespace Playgraph
{
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class CustomCharacterController : MonoBehaviour
    {
        [Header("Collision")]
        [FormerlySerializedAs("playerLayer")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 55f;
        [SerializeField, Range(1, 10)] private int maxSlideIterations = 5;
        [SerializeField, Min(0.001f)] private float skinWidth = 0.02f;
        [SerializeField, Min(0f)] private float groundProbeDistance = 0.15f;

        [Header("Gravity")]
        [SerializeField] private float gravity = -25f;
        [SerializeField, Min(0f)] private float terminalVelocity = 40f;
        [SerializeField, Min(0f)] private float stoppingAcceleration = 45f;

        [Header("Steps")]
        [SerializeField, Min(0f)] private float stepHeight = 0.35f;
        [SerializeField, Min(0f)] private float minimumStepHeight = 0.02f;
        [SerializeField, Min(0f)] private float maxStepDownHeight = 0.35f;

        private const float MinMoveDistance = 0.0001f;
        private const int HitBufferSize = 16;

        private readonly RaycastHit[] castHits = new RaycastHit[HitBufferSize];

        private Rigidbody body;
        private CapsuleCollider capsule;
        private Vector3 velocity;
        private Vector3 groundNormal = Vector3.up;
        private Quaternion motorRotation = Quaternion.identity;
        private float yaw;

        private Vector3 desiredPlanarVelocity;
        private float desiredPlanarAcceleration;
        private bool hasPlanarCommand;
        private bool jumpRequested;
        private float requestedJumpSpeed;
        private bool hasExternalMovement;
        private Vector3 externalDisplacement;

        private Vector3 pendingRootMotionPosition;
        private Quaternion pendingRootMotionRotation = Quaternion.identity;
        private AnimationRootMotionMode pendingRootMotionMode =
            AnimationRootMotionMode.Ignore;

        public Rigidbody Body => body;
        public CapsuleCollider Capsule => capsule;
        public bool IsGrounded { get; private set; }
        public bool IsExternallyDriven => hasExternalMovement;
        public Vector3 Velocity => velocity;
        public Vector3 GroundNormal => groundNormal;
        public Quaternion Rotation => motorRotation;
        public float YawDegrees => yaw;
        public LayerMask CollisionMask => collisionMask;
        public LayerMask GroundMask => groundMask;
        public float SkinWidth => skinWidth;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();

            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;

            yaw = transform.eulerAngles.y;
            motorRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Clears commands submitted during the previous physics step.
        /// Call this before submitting movement for the next simulation.
        /// </summary>
        public void BeginSimulationStep()
        {
            desiredPlanarVelocity = Vector3.zero;
            desiredPlanarAcceleration = stoppingAcceleration;
            hasPlanarCommand = false;
            jumpRequested = false;
            requestedJumpSpeed = 0f;
            hasExternalMovement = false;
            externalDisplacement = Vector3.zero;
        }

        public void SetPlanarVelocityTarget(
            Vector3 worldVelocity,
            float acceleration)
        {
            desiredPlanarVelocity = Vector3.ProjectOnPlane(
                worldVelocity,
                Vector3.up);
            desiredPlanarAcceleration = Mathf.Max(0f, acceleration);
            hasPlanarCommand = true;
        }

        public void RequestJump(float verticalSpeed)
        {
            jumpRequested = true;
            requestedJumpSpeed = Mathf.Max(0f, verticalSpeed);
            ClearPendingRootMotion();
        }

        /// <summary>
        /// Supplies a complete displacement for modes such as ladder climbing.
        /// Gravity, grounding, steps, and planar movement are skipped for this step.
        /// Collision and slide response still apply.
        /// </summary>
        public void SetExternalMovement(Vector3 displacement)
        {
            hasExternalMovement = true;
            externalDisplacement = displacement;
        }

        public void SetYaw(float degrees)
        {
            yaw = degrees;
            motorRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void SetVelocity(Vector3 worldVelocity)
        {
            velocity = worldVelocity;
        }

        public void ClearVelocity()
        {
            velocity = Vector3.zero;
        }

        public void Simulate(float deltaTime)
        {
            if (body == null || capsule == null || deltaTime <= 0f)
                return;

            ConsumeRootMotion(
                out Vector3 rootMotionPosition,
                out Quaternion rootMotionRotation,
                out AnimationRootMotionMode rootMotionMode);

            yaw += NormalizeAngle(rootMotionRotation.eulerAngles.y);
            motorRotation = Quaternion.Euler(0f, yaw, 0f);
            body.MoveRotation(motorRotation);

            if (hasExternalMovement)
            {
                SimulateExternalMovement(
                    deltaTime,
                    rootMotionPosition,
                    rootMotionMode);
                return;
            }

            SimulateNormalMovement(
                deltaTime,
                rootMotionPosition,
                rootMotionMode);
        }

        private void SimulateNormalMovement(
            float deltaTime,
            Vector3 rootMotionPosition,
            AnimationRootMotionMode rootMotionMode)
        {
            Vector3 startPosition = body.position;
            bool wasGrounded = ProbeGround(
                startPosition,
                groundProbeDistance,
                out RaycastHit groundHit,
                out _);

            if (wasGrounded)
            {
                IsGrounded = true;
                groundNormal = groundHit.normal;
            }
            else
            {
                IsGrounded = false;
                groundNormal = Vector3.up;
            }

            Vector3 targetVelocity = hasPlanarCommand
                ? desiredPlanarVelocity
                : Vector3.zero;
            Vector3 controlledVelocity;

            if (wasGrounded && !jumpRequested)
            {
                targetVelocity = Vector3.ProjectOnPlane(
                    targetVelocity,
                    groundNormal);
                controlledVelocity = Vector3.ProjectOnPlane(
                    velocity,
                    groundNormal);
            }
            else
            {
                controlledVelocity = Vector3.ProjectOnPlane(
                    velocity,
                    Vector3.up);
            }

            controlledVelocity = Vector3.MoveTowards(
                controlledVelocity,
                targetVelocity,
                desiredPlanarAcceleration * deltaTime);

            if (jumpRequested)
            {
                velocity = new Vector3(
                    controlledVelocity.x,
                    requestedJumpSpeed,
                    controlledVelocity.z);
                IsGrounded = false;
            }
            else if (wasGrounded)
            {
                velocity = controlledVelocity;
            }
            else
            {
                float verticalSpeed = Mathf.Max(
                    velocity.y + gravity * deltaTime,
                    -terminalVelocity);
                velocity = new Vector3(
                    controlledVelocity.x,
                    verticalSpeed,
                    controlledVelocity.z);
            }

            Vector3 requestedDisplacement = velocity * deltaTime;
            ApplyRootMotion(
                ref requestedDisplacement,
                rootMotionPosition,
                rootMotionMode,
                !wasGrounded || jumpRequested);

            Vector3 displacement = CollideAndSlide(
                startPosition,
                requestedDisplacement);

            if (wasGrounded &&
                !jumpRequested &&
                TryStep(
                    startPosition,
                    requestedDisplacement,
                    displacement,
                    out Vector3 stepDisplacement))
            {
                displacement = stepDisplacement;
            }

            Vector3 endPosition = startPosition + displacement;
            bool canSnap = !jumpRequested && (wasGrounded || velocity.y <= 0f);
            float snapDistance = wasGrounded
                ? Mathf.Max(groundProbeDistance, maxStepDownHeight)
                : groundProbeDistance;

            if (canSnap && ProbeGround(
                    endPosition,
                    snapDistance,
                    out groundHit,
                    out float groundGap))
            {
                if (groundGap > MinMoveDistance)
                {
                    Vector3 snap = CollideAndSlide(
                        endPosition,
                        Vector3.down * groundGap);
                    displacement += snap;
                    endPosition += snap;
                }

                IsGrounded = true;
                groundNormal = groundHit.normal;
            }
            else
            {
                IsGrounded = false;
                groundNormal = Vector3.up;
            }

            body.MovePosition(endPosition);
            velocity = displacement / deltaTime;

            if (IsGrounded && Vector3.Dot(velocity, Vector3.up) < 0f)
                velocity = Vector3.ProjectOnPlane(velocity, groundNormal);
        }

        private void SimulateExternalMovement(
            float deltaTime,
            Vector3 rootMotionPosition,
            AnimationRootMotionMode rootMotionMode)
        {
            IsGrounded = false;
            groundNormal = Vector3.up;

            Vector3 requestedDisplacement = externalDisplacement;
            ApplyRootMotion(
                ref requestedDisplacement,
                rootMotionPosition,
                rootMotionMode,
                false);

            Vector3 startPosition = body.position;
            Vector3 displacement = CollideAndSlide(
                startPosition,
                requestedDisplacement);

            body.MovePosition(startPosition + displacement);
            velocity = displacement / deltaTime;
        }

        public void AccumulateRootMotion(
            Vector3 deltaPosition,
            Quaternion deltaRotation,
            AnimationRootMotionMode mode)
        {
            if (mode == AnimationRootMotionMode.Ignore ||
                (!IsGrounded && !hasExternalMovement))
            {
                return;
            }

            pendingRootMotionPosition += deltaPosition;
            pendingRootMotionRotation *= deltaRotation;
            pendingRootMotionMode = mode;
        }

        public void ClearPendingRootMotion()
        {
            pendingRootMotionPosition = Vector3.zero;
            pendingRootMotionRotation = Quaternion.identity;
            pendingRootMotionMode = AnimationRootMotionMode.Ignore;
        }

        public bool HasGroundBelow(
            float distance,
            out RaycastHit hit,
            out float groundGap)
        {
            Vector3 position = body != null ? body.position : transform.position;
            return ProbeGround(position, distance, out hit, out groundGap);
        }

        public Vector3 GetCapsuleBottom(Vector3 downAxis)
        {
            Vector3 position = body != null ? body.position : transform.position;
            return GetCapsuleBottom(position, downAxis);
        }

        public Vector3 GetCapsuleBottom(Vector3 position, Vector3 downAxis)
        {
            GetCapsuleGeometry(
                position,
                0f,
                out Vector3 point1,
                out Vector3 point2,
                out float radius);

            Vector3 axis = downAxis.normalized;
            Vector3 lowerPoint = Vector3.Dot(point1, axis) <
                                 Vector3.Dot(point2, axis)
                ? point1
                : point2;

            return lowerPoint - axis * radius;
        }

        public bool CanSetCapsule(float height, Vector3 center)
        {
            if (capsule == null)
                return false;

            Vector3 position = body != null ? body.position : transform.position;
            GetCapsuleGeometry(
                position,
                0f,
                out Vector3 currentPoint1,
                out Vector3 currentPoint2,
                out float currentRadius);
            GetCapsuleGeometry(
                position,
                height,
                center,
                0f,
                out Vector3 requestedPoint1,
                out Vector3 requestedPoint2,
                out float requestedRadius);

            float currentTop = Mathf.Max(
                Vector3.Dot(currentPoint1, Vector3.up),
                Vector3.Dot(currentPoint2, Vector3.up)) + currentRadius;
            float requestedTop = Mathf.Max(
                Vector3.Dot(requestedPoint1, Vector3.up),
                Vector3.Dot(requestedPoint2, Vector3.up)) + requestedRadius;
            float expansionDistance = requestedTop - currentTop;

            if (expansionDistance <= MinMoveDistance)
                return true;

            return !CastCapsule(
                position,
                Vector3.up,
                expansionDistance,
                collisionMask,
                false,
                out _);
        }

        public bool TrySetCapsule(float height, Vector3 center)
        {
            if (!CanSetCapsule(height, center))
                return false;

            capsule.height = Mathf.Max(height, capsule.radius * 2f);
            capsule.center = center;
            return true;
        }

        public void ForceSetCapsule(float height, Vector3 center)
        {
            if (capsule == null)
                return;

            capsule.height = Mathf.Max(height, capsule.radius * 2f);
            capsule.center = center;
        }

        private void ConsumeRootMotion(
            out Vector3 deltaPosition,
            out Quaternion deltaRotation,
            out AnimationRootMotionMode mode)
        {
            deltaPosition = pendingRootMotionPosition;
            deltaRotation = pendingRootMotionRotation;
            mode = pendingRootMotionMode;
            ClearPendingRootMotion();
        }

        private static void ApplyRootMotion(
            ref Vector3 requestedDisplacement,
            Vector3 rootMotionPosition,
            AnimationRootMotionMode mode,
            bool ignoreRootMotion)
        {
            if (ignoreRootMotion)
                return;

            switch (mode)
            {
                case AnimationRootMotionMode.Additive:
                    requestedDisplacement += rootMotionPosition;
                    break;

                case AnimationRootMotionMode.OverrideHorizontal:
                    requestedDisplacement.x = rootMotionPosition.x;
                    requestedDisplacement.z = rootMotionPosition.z;
                    requestedDisplacement.y += rootMotionPosition.y;
                    break;
            }
        }

        private Vector3 CollideAndSlide(
            Vector3 startPosition,
            Vector3 displacement)
        {
            Vector3 moved = Vector3.zero;
            Vector3 remaining = displacement;

            for (int iteration = 0; iteration < maxSlideIterations; iteration++)
            {
                float distance = remaining.magnitude;
                if (distance <= MinMoveDistance)
                    break;

                Vector3 direction = remaining / distance;
                Vector3 castPosition = startPosition + moved;

                if (!CastCapsule(
                        castPosition,
                        direction,
                        distance,
                        collisionMask,
                        false,
                        out RaycastHit hit))
                {
                    moved += remaining;
                    break;
                }

                float travelDistance = Mathf.Clamp(
                    hit.distance - skinWidth,
                    0f,
                    distance);
                moved += direction * travelDistance;

                float leftoverDistance = distance - travelDistance;
                if (leftoverDistance <= MinMoveDistance)
                    break;

                remaining = direction * leftoverDistance;

                if (IsWalkable(hit.normal))
                {
                    remaining = Vector3.ProjectOnPlane(remaining, hit.normal);
                }
                else if (hit.normal.y > 0f)
                {
                    Vector3 wallNormal = Vector3.ProjectOnPlane(
                        hit.normal,
                        Vector3.up);

                    if (wallNormal.sqrMagnitude > 0f)
                    {
                        Vector3 verticalPart = Vector3.Project(
                            remaining,
                            Vector3.up);
                        Vector3 horizontalPart = remaining - verticalPart;
                        horizontalPart = Vector3.ProjectOnPlane(
                            horizontalPart,
                            wallNormal.normalized);
                        verticalPart = Vector3.ProjectOnPlane(
                            verticalPart,
                            hit.normal);
                        remaining = horizontalPart + verticalPart;
                    }
                    else
                    {
                        remaining = Vector3.zero;
                    }
                }
                else
                {
                    remaining = Vector3.ProjectOnPlane(remaining, hit.normal);
                }
            }

            return moved;
        }

        private bool TryStep(
            Vector3 startPosition,
            Vector3 requestedDisplacement,
            Vector3 regularDisplacement,
            out Vector3 stepDisplacement)
        {
            stepDisplacement = regularDisplacement;

            if (stepHeight <= MinMoveDistance)
                return false;

            Vector3 horizontalRequest = Vector3.ProjectOnPlane(
                requestedDisplacement,
                Vector3.up);
            float requestedDistance = horizontalRequest.magnitude;

            if (requestedDistance <= MinMoveDistance)
                return false;

            Vector3 moveDirection = horizontalRequest / requestedDistance;
            float regularProgress = Vector3.Dot(
                Vector3.ProjectOnPlane(regularDisplacement, Vector3.up),
                moveDirection);
            float progressTolerance = Mathf.Max(
                MinMoveDistance,
                skinWidth * 0.25f);

            if (regularProgress >= requestedDistance - progressTolerance)
                return false;

            Vector3 up = CollideAndSlide(
                startPosition,
                Vector3.up * stepHeight);

            if (up.y < stepHeight - skinWidth)
                return false;

            Vector3 raisedPosition = startPosition + up;
            Vector3 forward = CollideAndSlide(
                raisedPosition,
                horizontalRequest);
            float stepProgress = Vector3.Dot(
                Vector3.ProjectOnPlane(forward, Vector3.up),
                moveDirection);

            if (stepProgress <= regularProgress + progressTolerance)
                return false;

            Vector3 landingProbePosition = raisedPosition + forward;
            if (!ProbeGround(
                    landingProbePosition,
                    stepHeight + groundProbeDistance,
                    out _,
                    out float landingGap))
            {
                return false;
            }

            Vector3 down = CollideAndSlide(
                landingProbePosition,
                Vector3.down * landingGap);

            if (-down.y < landingGap - skinWidth)
                return false;

            Vector3 candidate = up + forward + down;
            float verticalRise = Vector3.Dot(candidate, Vector3.up);

            if (verticalRise < minimumStepHeight ||
                verticalRise > stepHeight + skinWidth)
            {
                return false;
            }

            stepDisplacement = candidate;
            return true;
        }

        private bool ProbeGround(
            Vector3 position,
            float probeDistance,
            out RaycastHit hit,
            out float groundGap)
        {
            bool found = CastCapsule(
                position,
                Vector3.down,
                probeDistance,
                groundMask,
                true,
                out hit);

            groundGap = found
                ? Mathf.Max(0f, hit.distance - skinWidth)
                : 0f;
            return found;
        }

        private bool CastCapsule(
            Vector3 position,
            Vector3 direction,
            float distance,
            LayerMask mask,
            bool walkableOnly,
            out RaycastHit nearestHit)
        {
            GetCapsuleGeometry(
                position,
                skinWidth,
                out Vector3 point1,
                out Vector3 point2,
                out float radius);

            int hitCount = Physics.CapsuleCastNonAlloc(
                point1,
                point2,
                radius,
                direction,
                castHits,
                distance + skinWidth,
                mask,
                QueryTriggerInteraction.Ignore);

            nearestHit = default;
            float nearestDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = castHits[i];
                Collider other = candidate.collider;

                if (other == null ||
                    other.attachedRigidbody == body ||
                    other.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (walkableOnly && !IsWalkable(candidate.normal))
                    continue;

                if (candidate.distance >= nearestDistance)
                    continue;

                nearestDistance = candidate.distance;
                nearestHit = candidate;
            }

            return nearestDistance < float.PositiveInfinity;
        }

        private void GetCapsuleGeometry(
            Vector3 position,
            float shrink,
            out Vector3 point1,
            out Vector3 point2,
            out float radius)
        {
            GetCapsuleGeometry(
                position,
                capsule.height,
                capsule.center,
                shrink,
                out point1,
                out point2,
                out radius);
        }

        private void GetCapsuleGeometry(
            Vector3 position,
            float capsuleHeight,
            Vector3 capsuleCenter,
            float shrink,
            out Vector3 point1,
            out Vector3 point2,
            out float radius)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 absoluteScale = new Vector3(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z));

            Vector3 localAxis;
            float heightScale;
            float radiusScale;

            switch (capsule.direction)
            {
                case 0:
                    localAxis = Vector3.right;
                    heightScale = absoluteScale.x;
                    radiusScale = Mathf.Max(absoluteScale.y, absoluteScale.z);
                    break;

                case 2:
                    localAxis = Vector3.forward;
                    heightScale = absoluteScale.z;
                    radiusScale = Mathf.Max(absoluteScale.x, absoluteScale.y);
                    break;

                default:
                    localAxis = Vector3.up;
                    heightScale = absoluteScale.y;
                    radiusScale = Mathf.Max(absoluteScale.x, absoluteScale.z);
                    break;
            }

            float fullRadius = capsule.radius * radiusScale;
            float fullHeight = Mathf.Max(
                capsuleHeight * heightScale,
                fullRadius * 2f);
            float halfSegment = Mathf.Max(
                0f,
                fullHeight * 0.5f - fullRadius);
            Vector3 center = position +
                             motorRotation * Vector3.Scale(capsuleCenter, scale);
            Vector3 axis = motorRotation * localAxis;

            point1 = center + axis * halfSegment;
            point2 = center - axis * halfSegment;
            radius = Mathf.Max(0.001f, fullRadius - shrink);
        }

        private bool IsWalkable(Vector3 normal)
        {
            float minimumUpDot = Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);
            return Vector3.Dot(normal, Vector3.up) >= minimumUpDot;
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private void OnValidate()
        {
            maxSlopeAngle = Mathf.Clamp(maxSlopeAngle, 0f, 89f);
            maxSlideIterations = Mathf.Clamp(maxSlideIterations, 1, 10);
            skinWidth = Mathf.Max(0.001f, skinWidth);
            groundProbeDistance = Mathf.Max(0f, groundProbeDistance);
            gravity = Mathf.Min(0f, gravity);
            terminalVelocity = Mathf.Max(0f, terminalVelocity);
            stoppingAcceleration = Mathf.Max(0f, stoppingAcceleration);
            stepHeight = Mathf.Max(0f, stepHeight);
            minimumStepHeight = Mathf.Clamp(
                minimumStepHeight,
                0f,
                stepHeight);
            maxStepDownHeight = Mathf.Max(0f, maxStepDownHeight);
        }
    }
}
