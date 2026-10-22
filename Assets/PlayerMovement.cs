using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    public float sprintSpeed = 20f;
    public float jumpHeight = 1.5f;
    public int maxJumps = 2;
    public float gravity = -10f;

    [Header("Gliding - E toggles, mouse steers, S brakes")]
    [Tooltip("How quickly momentum turns toward the camera. Turning does not add speed.")]
    [Min(0f)] public float glideSteering = 3f;
    [Tooltip("Below this airspeed, the wings progressively lose steering/lift authority.")]
    [Min(0.1f)] public float glideStallSpeed = 8f;
    [Min(0f)] public float glideAirDrag = 0.04f;
    [Min(0f)] public float glideSpeedDrag = 0.002f;
    [Min(1f)] public float glideSpeedLimit = 65f;
    [Min(0f)] public float glideBrakeDrag = 1.2f;
    [Min(0f)] public float airAcceleration = 3f;

    [Header("Wall running")]
    public float wallCheckDistance = 0.6f;
    public float wallRunSpeed = 7f;
    public float wallRunGravityMultiplier = 0.25f;
    public float wallRunMaxFallSpeed = 10f;
    public float wallJumpUpForce = 7f;
    public float wallJumpSideForce = 5f;
    public float wallRunTilt = 12f;

    [Header("Grapple - Q starts / cancels")]
    public float grappleHeight = 20.5f;
    public float grapplePullSpeed = 30f;
    public float grappleStopDistance = 0.5f;
    [Min(0.01f)] public float grappleArrivalTolerance = 0.02f;
    [Min(0.05f)] public float grappleStuckTimeout = 0.2f;
    [Min(0.1f)] public float grappleMaxDuration = 15f;

    public Camera playerCamera;
    public float sprintFov = 85f;
    public float fovTransitionSpeed = 8f;

    private CharacterController controller;
    private MouseLook mouseLook;
    private float verticalSpeed;
    private float normalFov;
    private int jumpsRemaining;
    private bool isWallRunning;
    private bool isGrappling;
    private bool isGliding;
    private Vector3 grapplePoint;
    private Vector3 grappleVelocity;
    private Vector3 glideVelocity;
    private Vector3 horizontalVelocity;
    private bool hasGrappleMomentum;
    private float grappleElapsed;
    private float grappleBlockedTime;
    private bool wallIsOnRight;
    private Vector3 wallNormal;
    private readonly RaycastHit[] groundHits = new RaycastHit[8];

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        mouseLook = GetComponent<MouseLook>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera != null)
            normalFov = playerCamera.fieldOfView;
        jumpsRemaining = maxJumps;
    }

    private void Update()
    {
        TickMovement(new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")),
            Input.GetKey(KeyCode.LeftShift), Input.GetButtonDown("Jump"),
            Input.GetKeyDown(KeyCode.E), Input.GetKeyDown(KeyCode.Q), Time.deltaTime);
    }

    private void TickMovement(Vector2 movementInput, bool sprintHeld, bool jumpPressed,
        bool glidePressed, bool grapplePressed, float deltaTime)
    {
        if (deltaTime <= 0f || !controller.enabled)
            return;

        if (grapplePressed)
        {
            if (isGrappling)
                StopGrapple();
            else
                StartGrapple();
        }

        // A grapple owns this frame's movement even if it ends during Move.
        // Do not move a second time on its completion/collision frame.
        if (isGrappling && TickGrapple(deltaTime))
        {
            UpdateCamera(false, deltaTime);
            return;
        }

        float x = movementInput.x;
        float z = movementInput.y;
        Vector3 inputDirection = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);
        bool isSprinting = sprintHeld && inputDirection.sqrMagnitude > 0f;
        float currentSpeed = isSprinting ? sprintSpeed : speed;
        Vector3 desiredHorizontal = inputDirection * currentSpeed;
        bool grounded = controller.isGrounded && verticalSpeed <= 0f;
        bool hasWall = TryGetWall(out RaycastHit wallHit, out bool wallOnRight);

        if (grounded)
        {
            hasGrappleMomentum = false;
            verticalSpeed = -2f;
            isWallRunning = false;
            isGliding = false;
            jumpsRemaining = maxJumps;
            glideVelocity = Vector3.zero;
            horizontalVelocity = desiredHorizontal;
        }

        if (jumpPressed && isWallRunning)
        {
            verticalSpeed = wallJumpUpForce;
            horizontalVelocity += wallNormal * wallJumpSideForce;
            isWallRunning = false;
            jumpsRemaining = Mathf.Max(0, jumpsRemaining - 1);
        }
        else if (jumpPressed && !grounded && hasWall)
        {
            StopGliding();
            hasGrappleMomentum = false;
            isWallRunning = true;
            wallIsOnRight = wallOnRight;
            wallNormal = wallHit.normal;
        }
        else if (jumpPressed && (grounded || jumpsRemaining > 0))
        {
            StopGliding();
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpsRemaining = grounded ? maxJumps - 1 : jumpsRemaining - 1;
        }

        if (isWallRunning && !hasWall)
            isWallRunning = false;

        if (glidePressed && !grounded && !isWallRunning)
        {
            if (isGliding)
                StopGliding();
            else
            {
                isGliding = true;
                // Deploying the wings never creates speed or discards a dive/launch.
                glideVelocity = horizontalVelocity + Vector3.up * verticalSpeed;
            }
        }

        if (isGliding)
        {
            Transform aim = playerCamera != null ? playerCamera.transform : transform;
            Vector3 lookDirection = (aim.forward + aim.right * x * 0.35f).normalized;
            float brake = Mathf.Clamp01(-z);
            Vector3 displacement = IntegrateGlide(lookDirection, brake, deltaTime);
            CollisionFlags glideCollision = controller.Move(displacement);
            // Contact callbacks remove velocity into walls, floors and ceilings.
            horizontalVelocity = Vector3.ProjectOnPlane(glideVelocity, Vector3.up);
            verticalSpeed = glideVelocity.y;
            if ((glideCollision & CollisionFlags.Below) != 0)
            {
                StopGliding();
                hasGrappleMomentum = false;
                verticalSpeed = -2f;
                jumpsRemaining = maxJumps;
            }
        }
        else
        {
            if (isWallRunning)
            {
                wallIsOnRight = wallOnRight;
                wallNormal = wallHit.normal;
                Vector3 wallDirection = Vector3.Cross(Vector3.up, wallNormal).normalized;
                if (Vector3.Dot(wallDirection, transform.forward) < 0f)
                    wallDirection = -wallDirection;
                horizontalVelocity = wallDirection * wallRunSpeed - wallNormal;
                verticalSpeed = Mathf.Max(verticalSpeed + gravity * wallRunGravityMultiplier * deltaTime,
                    -wallRunMaxFallSpeed);
            }
            else
            {
                if (!grounded)
                {
                    if (!hasGrappleMomentum)
                        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desiredHorizontal,
                            airAcceleration * deltaTime);
                    else if (inputDirection.sqrMagnitude > 0.001f)
                    {
                        // Air input steers the launch; releasing input does not brake it
                        // back to walking speed. Opposing input can still slow the player.
                        float targetSpeed = Mathf.Max(horizontalVelocity.magnitude, currentSpeed);
                        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity,
                            inputDirection.normalized * targetSpeed, airAcceleration * deltaTime);
                    }
                }
                verticalSpeed += gravity * deltaTime;
            }

            CollisionFlags collision = controller.Move((horizontalVelocity + Vector3.up * verticalSpeed) * deltaTime);
            // Follow walkable slopes/steps down without entering flight controls for
            // a frame. Never snap a jump, wall run, glide, or an already airborne fall.
            if (grounded && !isWallRunning && verticalSpeed <= 0f && !controller.isGrounded)
                collision |= SnapToGround(deltaTime);
            if ((collision & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
            if ((collision & CollisionFlags.Below) != 0 && verticalSpeed < 0f)
            {
                hasGrappleMomentum = false;
                verticalSpeed = -2f;
                jumpsRemaining = maxJumps;
            }
        }
        UpdateCamera(isSprinting, deltaTime);
    }

    private CollisionFlags SnapToGround(float deltaTime)
    {
        float scale = Mathf.Abs(transform.lossyScale.y);
        float skin = controller.skinWidth * scale;
        float slopeDrop = horizontalVelocity.magnitude * deltaTime *
            Mathf.Tan(Mathf.Min(controller.slopeLimit, 80f) * Mathf.Deg2Rad);
        float snapDistance = Mathf.Max(controller.stepOffset * scale, slopeDrop) + skin + 0.05f;
        Vector3 foot = controller.bounds.center - Vector3.up * controller.bounds.extents.y;
        float originOffset = skin + 0.05f;
        int count = Physics.RaycastNonAlloc(foot + Vector3.up * originOffset, Vector3.down,
            groundHits, snapDistance + originOffset, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        float minGroundNormal = Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.transform.IsChildOf(transform) || hit.normal.y < minGroundNormal)
                continue;
            nearest = Mathf.Min(nearest, hit.distance);
        }
        if (float.IsPositiveInfinity(nearest))
            return CollisionFlags.None;
        return controller.Move(Vector3.down * Mathf.Min(snapDistance, Mathf.Max(0f, nearest - originOffset) + skin));
    }

    private Vector3 IntegrateGlide(Vector3 lookDirection, float brake, float deltaTime)
    {
        Vector3 displacement = Vector3.zero;
        // Small integration steps keep dives/pull-ups consistent at different frame rates.
        int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime / (1f / 120f)));
        float step = deltaTime / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 previousVelocity = glideVelocity;
            float airspeed = glideVelocity.magnitude;
            float authority = Mathf.Clamp01(airspeed / Mathf.Max(0.1f, glideStallSpeed));
            authority *= authority;
            if (airspeed > 0.001f)
            {
                // A stalled wing cannot keep its nose high: it must descend to recover
                // airspeed before a pull-up can redirect momentum upward again.
                float recovery = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(glideStallSpeed * 0.5f, glideStallSpeed * 1.5f, airspeed));
                Vector3 flightDirection = lookDirection;
                flightDirection.y = Mathf.Min(flightDirection.y, Mathf.Lerp(-0.6f, 1f, recovery));
                flightDirection.Normalize();
                // Redirect momentum without increasing its magnitude. Gravity then trades
                // altitude for speed in a dive and removes speed when climbing.
                float steering = 1f - Mathf.Exp(-Mathf.Max(0f, glideSteering) * authority * step);
                Vector3 direction = Vector3.Slerp(glideVelocity / airspeed, flightDirection, steering).normalized;
                glideVelocity = direction * airspeed;
            }
            glideVelocity += Vector3.up * gravity * step;
            float drag = Mathf.Max(0f, glideAirDrag) + Mathf.Max(0f, glideSpeedDrag) * glideVelocity.magnitude
                + Mathf.Clamp01(brake) * Mathf.Max(0f, glideBrakeDrag);
            glideVelocity *= Mathf.Exp(-drag * step);
            glideVelocity = Vector3.ClampMagnitude(glideVelocity, Mathf.Max(1f, glideSpeedLimit));
            displacement += (previousVelocity + glideVelocity) * (0.5f * step);
        }
        return displacement;
    }

    private void StopGliding()
    {
        if (!isGliding)
            return;
        horizontalVelocity = Vector3.ProjectOnPlane(glideVelocity, Vector3.up);
        verticalSpeed = glideVelocity.y;
        isGliding = false;
        glideVelocity = Vector3.zero;
    }

    private void StartGrapple()
    {
        if (grapplePullSpeed <= 0f || grappleHeight <= 0f)
            return;
        StopGliding();
        hasGrappleMomentum = false;
        isGrappling = true;
        isWallRunning = false;
        Transform aim = playerCamera != null ? playerCamera.transform : transform;
        grapplePoint = aim.position + aim.forward * grappleHeight;
        grappleVelocity = horizontalVelocity + Vector3.up * verticalSpeed;
        grappleElapsed = 0f;
        grappleBlockedTime = 0f;
    }

    private bool TickGrapple(float deltaTime)
    {
        if (deltaTime <= 0f)
            return true;
        grappleElapsed += deltaTime;
        Vector3 toTarget = grapplePoint - transform.position;
        float distance = toTarget.magnitude;
        float stopDistance = Mathf.Max(0f, grappleStopDistance);
        float tolerance = Mathf.Max(0.01f, grappleArrivalTolerance, controller.minMoveDistance * 2f);
        if (distance <= stopDistance + tolerance || grapplePullSpeed <= 0f ||
            grappleElapsed >= Mathf.Max(0.1f, grappleMaxDuration))
        {
            StopGrapple();
            return false;
        }

        float travel = Mathf.Min(grapplePullSpeed * deltaTime, distance - stopDistance);
        Vector3 before = transform.position;
        // Keep the full pull speed even when the final step covers only the small
        // distance left to the target. Collision callbacks remove blocked velocity.
        grappleVelocity = toTarget / distance * grapplePullSpeed;
        CollisionFlags collision = controller.Move(toTarget / distance * travel);
        Vector3 actualDisplacement = transform.position - before;
        if ((collision & CollisionFlags.Above) != 0 && grappleVelocity.y > 0f)
            grappleVelocity.y = 0f;
        if ((collision & CollisionFlags.Below) != 0 && grappleVelocity.y < 0f)
            grappleVelocity.y = 0f;

        float remaining = Vector3.Distance(grapplePoint, transform.position);
        float progress = distance - remaining;
        grappleBlockedTime = progress < travel * 0.1f ? grappleBlockedTime + deltaTime : 0f;
        // If movement was suppressed without a contact (for example by minMoveDistance),
        // do not invent a launch from a stationary player.
        if (grappleBlockedTime > 0f && collision == CollisionFlags.None)
            grappleVelocity = actualDisplacement / deltaTime;
        if (remaining <= stopDistance + tolerance || collision != CollisionFlags.None ||
            grappleBlockedTime >= Mathf.Max(0.05f, grappleStuckTimeout))
            StopGrapple();
        return true;
    }

    private void StopGrapple()
    {
        isGrappling = false;
        horizontalVelocity = Vector3.ProjectOnPlane(grappleVelocity, Vector3.up);
        verticalSpeed = grappleVelocity.y;
        hasGrappleMomentum = grappleVelocity.sqrMagnitude > 0.001f;
        grappleVelocity = Vector3.zero;
        grappleElapsed = 0f;
        grappleBlockedTime = 0f;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (isGrappling)
        {
            // Capsule sweeps can report a slightly tilted normal on a vertical wall.
            // Remove the horizontal blocked component without converting it to lift.
            Vector3 contactNormal = Mathf.Abs(hit.normal.y) < 0.2f
                ? Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized : hit.normal;
            float intoSurface = Vector3.Dot(grappleVelocity, contactNormal);
            if (intoSurface < 0f)
                grappleVelocity -= contactNormal * intoSurface;
        }
        else if (isGliding)
        {
            float intoSurface = Vector3.Dot(glideVelocity, hit.normal);
            if (intoSurface < 0f)
                glideVelocity -= hit.normal * intoSurface;
        }
        else if (hasGrappleMomentum && Mathf.Abs(hit.normal.y) < 0.2f)
        {
            // A launched player loses momentum into a wall, while ordinary ground
            // movement continues to use the controller's slope/step handling.
            Vector3 wallDirection = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            float intoSurface = Vector3.Dot(horizontalVelocity, wallDirection);
            if (intoSurface < 0f)
                horizontalVelocity -= wallDirection * intoSurface;
        }
        // Walking uses CharacterController's slope/step resolution. Projecting its
        // velocity here turns uphill contact into a jump and leaks air steering
        // into ground movement. Only flight momentum needs contact projection.
    }

    private void UpdateCamera(bool isSprinting, float deltaTime)
    {
        if (mouseLook != null)
            mouseLook.wallRunTilt = isWallRunning ? (wallIsOnRight ? wallRunTilt : -wallRunTilt) : 0f;
        if (playerCamera != null)
        {
            float flightFov = Mathf.Lerp(normalFov, sprintFov, Mathf.InverseLerp(speed, 30f, glideVelocity.magnitude));
            float targetFov = isGliding ? flightFov : (isSprinting || isWallRunning ? sprintFov : normalFov);
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov,
                1f - Mathf.Exp(-fovTransitionSpeed * deltaTime));
        }
    }

    private void OnDisable()
    {
        isGrappling = false;
        isGliding = false;
        isWallRunning = false;
        grappleVelocity = Vector3.zero;
        glideVelocity = Vector3.zero;
        horizontalVelocity = Vector3.zero;
        hasGrappleMomentum = false;
        verticalSpeed = 0f;
        if (mouseLook != null)
            mouseLook.wallRunTilt = 0f;
    }

    private bool TryGetWall(out RaycastHit wallHit, out bool wallOnRight)
    {
        float rayDistance = controller.radius + wallCheckDistance;
        for (int heightIndex = -1; heightIndex <= 1; heightIndex++)
        {
            Vector3 localOrigin = controller.center + Vector3.up * (heightIndex * controller.height * 0.25f);
            Vector3 origin = transform.TransformPoint(localOrigin);
            if (Physics.Raycast(origin, transform.right, out wallHit, rayDistance) && Mathf.Abs(wallHit.normal.y) < 0.2f)
            {
                wallOnRight = true;
                return true;
            }
            if (Physics.Raycast(origin, -transform.right, out wallHit, rayDistance) && Mathf.Abs(wallHit.normal.y) < 0.2f)
            {
                wallOnRight = false;
                return true;
            }
        }
        wallHit = default;
        wallOnRight = false;
        return false;
    }
}
