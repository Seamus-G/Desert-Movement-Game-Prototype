using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    public float sprintSpeed = 11f;
    public float jumpHeight = 1.5f;
    public int maxJumps = 2;
    public float gravity = -10f;
    public float glideGravityMultiplier = 0.2f;
    public float glideMaxFallSpeed = 5f;
    public float glideLookDownSpeedMultiplier = 1.5f;
    public float glideMaxHorizontalSpeed = 40f;
    public float glideDiveAcceleration = 12f;
    public float glidePullUpRate = 3f;
    public float glideLiftEfficiency = 10f;
    public float glideMinimumPullUpSpeed = 5f;
    public float wallCheckDistance = 0.6f;
    public float wallRunSpeed = 7f;
    public float wallRunGravityMultiplier = 0.25f;
    public float wallRunMaxFallSpeed = 10f;
    public float wallJumpUpForce = 7f;
    public float wallJumpSideForce = 5f;
    public float wallRunTilt = 12f;
    public float grappleHeight = 20.5f;
    public float grapplePullSpeed = 10f;
    public float grappleStopDistance = 0.5f;
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
    private bool wallIsOnRight;
    private Vector3 wallNormal;
    private Vector3 wallJumpDirection;

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
        if (Input.GetKeyDown(KeyCode.Q) && !isGrappling)
        {
            isGrappling = true;
            Transform aimTransform = playerCamera != null ? playerCamera.transform : transform;
            grapplePoint = aimTransform.position + aimTransform.forward * grappleHeight;
            grappleVelocity = Vector3.zero;
            glideVelocity = Vector3.zero;
            isWallRunning = false;
        }

        if (isGrappling)
        {
            Vector3 toGrapplePoint = grapplePoint - transform.position;
            float distance = toGrapplePoint.magnitude;

            if (distance <= grappleStopDistance)
            {
                isGrappling = false;
            }
            else
            {
                grappleVelocity = toGrapplePoint.normalized * grapplePullSpeed;
                float pullDistance = Mathf.Min(
                    grapplePullSpeed * Time.deltaTime,
                    distance - grappleStopDistance);
                CollisionFlags collision = controller.Move(
                    toGrapplePoint.normalized * pullDistance);
                verticalSpeed = grappleVelocity.y;

                if (collision != CollisionFlags.None)
                    isGrappling = false;
            }

            if (isGrappling)
            {
                if (mouseLook != null)
                    mouseLook.wallRunTilt = 0f;

                if (playerCamera != null)
                {
                    playerCamera.fieldOfView = Mathf.Lerp(
                        playerCamera.fieldOfView,
                        normalFov,
                        fovTransitionSpeed * Time.deltaTime);
                }

                return;
            }
        }

        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        Vector3 move = transform.right * x + transform.forward * z;
        move = Vector3.ClampMagnitude(move, 1f);
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) && move.sqrMagnitude > 0f;
        float currentSpeed = isSprinting ? sprintSpeed : speed;
        bool hasWall = TryGetWall(out RaycastHit wallHit, out bool wallOnRight);
        bool jumpPressed = Input.GetButtonDown("Jump");

        if (controller.isGrounded && verticalSpeed < 0)
        {
            verticalSpeed = -2f;
            isWallRunning = false;
            isGliding = false;
            jumpsRemaining = maxJumps;
            glideVelocity = Vector3.zero;
        }

        if (jumpPressed && isWallRunning)
        {
            verticalSpeed = wallJumpUpForce;
            wallJumpDirection = wallNormal * wallJumpSideForce;
            isWallRunning = false;
            jumpsRemaining = Mathf.Max(0, jumpsRemaining - 1);
        }
        else if (jumpPressed && !controller.isGrounded && hasWall)
        {
            isWallRunning = true;
            wallIsOnRight = wallOnRight;
            wallNormal = wallHit.normal;
        }
        else if (jumpPressed && controller.isGrounded)
        {
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpsRemaining = maxJumps - 1;
        }
        else if (jumpPressed && !controller.isGrounded && jumpsRemaining > 0)
        {
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpsRemaining--;
        }

        if (isWallRunning)
        {
            if (!hasWall)
            {
                isWallRunning = false;
            }
            else
            {
                wallIsOnRight = wallOnRight;
                wallNormal = wallHit.normal;
                Vector3 wallDirection = Vector3.Cross(Vector3.up, wallNormal).normalized;
                if (Vector3.Dot(wallDirection, transform.forward) < 0f)
                    wallDirection = -wallDirection;

                move = wallDirection * wallRunSpeed - wallNormal;
                verticalSpeed = Mathf.Max(
                    verticalSpeed + gravity * wallRunGravityMultiplier * Time.deltaTime,
                    -wallRunMaxFallSpeed);
            }
        } 
        if (isWallRunning)
            isGliding = false;

        if (Input.GetKeyDown(KeyCode.E) && !controller.isGrounded && !isWallRunning)
            isGliding = !isGliding;
        Vector3 glideDirection = transform.forward;
        float downwardLook = 0f;
        float upwardLook = 0f;
        if (isGliding && playerCamera != null)
        {
            Vector3 cameraForward = playerCamera.transform.forward;
            downwardLook = Mathf.Clamp01(-cameraForward.y);
            upwardLook = Mathf.Clamp01(cameraForward.y);
            glideDirection = cameraForward.normalized;
        }

        if (!isWallRunning)
        {
            float gravityMultiplier = isGliding ? glideGravityMultiplier : 1f;
            verticalSpeed += gravity * gravityMultiplier * Time.deltaTime;
            if (isGliding)
                verticalSpeed = Mathf.Max(verticalSpeed, -glideMaxFallSpeed);
        }

        if (isGliding)
        {
            if (downwardLook > 0f)
            {
                glideVelocity += glideDirection * glideDiveAcceleration *
                    Mathf.Lerp(1f, glideLookDownSpeedMultiplier, downwardLook) *
                    downwardLook * Time.deltaTime;
            }

            float glideSpeed = glideVelocity.magnitude;
            if (upwardLook > 0f && glideSpeed >= glideMinimumPullUpSpeed)
            {
                glideVelocity = Vector3.RotateTowards(
                    glideVelocity,
                    glideDirection * glideSpeed,
                    glidePullUpRate * upwardLook * Time.deltaTime,
                    0f);
            }

            glideVelocity = Vector3.ClampMagnitude(glideVelocity, glideMaxHorizontalSpeed);
        }
        else
        {
            glideVelocity = Vector3.MoveTowards(
                glideVelocity,
                Vector3.zero,
                glideDiveAcceleration * Time.deltaTime);
        }

        move *= isWallRunning ? 1f : currentSpeed;
        if (isGliding)
        {
            move += glideVelocity;
            Vector3 horizontalMove = new Vector3(move.x, 0f, move.z);
            horizontalMove = Vector3.ClampMagnitude(horizontalMove, glideMaxHorizontalSpeed);
            move.x = horizontalMove.x;
            move.z = horizontalMove.z;
        }
        move += wallJumpDirection;
        wallJumpDirection = Vector3.zero;
        move.y = isGliding
            ? Mathf.Max(verticalSpeed + glideVelocity.y * glideLiftEfficiency, -glideMaxFallSpeed)
            : verticalSpeed;

        controller.Move(move * Time.deltaTime);

        if (mouseLook != null)
            mouseLook.wallRunTilt = isWallRunning ? (wallIsOnRight ? wallRunTilt : -wallRunTilt) : 0f;

        if (playerCamera != null)
        {
            float targetFov = isSprinting || isWallRunning ? sprintFov : normalFov;
            playerCamera.fieldOfView = Mathf.Lerp(
                playerCamera.fieldOfView,
                targetFov,
                fovTransitionSpeed * Time.deltaTime);
        }
    }

    private bool TryGetWall(out RaycastHit wallHit, out bool wallOnRight)
    {
        float rayDistance = controller.radius + wallCheckDistance;

        for (int heightIndex = -1; heightIndex <= 1; heightIndex++)
        {
            Vector3 localOrigin = controller.center +
                Vector3.up * (heightIndex * controller.height * 0.25f);
            Vector3 origin = transform.TransformPoint(localOrigin);

            if (Physics.Raycast(origin, transform.right, out wallHit, rayDistance) &&
                Mathf.Abs(wallHit.normal.y) < 0.2f)
            {
                wallOnRight = true;
                return true;
            }

            if (Physics.Raycast(origin, -transform.right, out wallHit, rayDistance) &&
                Mathf.Abs(wallHit.normal.y) < 0.2f)
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