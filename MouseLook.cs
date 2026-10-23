using UnityEngine;

// Update aim before PlayerMovement reads it for this frame's glide or grapple.
[DefaultExecutionOrder(-100)]
public class MouseLook : MonoBehaviour
{
    public Transform cameraTransform;
    public float sensitivity = 2f;
    public float wallRunTilt;
    public float tiltTransitionSpeed = 8f;

    private float pitch;
    private float currentTilt;
    private bool cursorReleased;

    private void Awake()
    {
        if (cameraTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null)
                cameraTransform = childCamera.transform;
        }
        if (cameraTransform != null)
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, cameraTransform.localEulerAngles.x), -80f, 80f);
    }

    private void Start()
    {
        CaptureCursor();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            cursorReleased = true;
            ReleaseCursor();
            return;
        }
        if (!Application.isFocused)
            return;

        // Unity/the OS can release the cursor after a focus change or Escape.
        // A click back into the game must restore mouse-look without restarting play.
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (Input.GetMouseButtonDown(0))
                CaptureCursor();
            return; // Discard the cursor-warp delta on the capture frame.
        }
        ApplyLook(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")));
    }

    private void ApplyLook(Vector2 mouseDelta)
    {
        // Mouse input is independent of movement keys and all movement states.
        transform.Rotate(Vector3.up, mouseDelta.x * sensitivity, Space.World);
        pitch = Mathf.Clamp(pitch - mouseDelta.y * sensitivity, -80f, 80f);
        ApplyCameraRotation();
    }

    private void LateUpdate()
    {
        // Movement chooses the wall tilt in Update; finish the visual roll afterward.
        currentTilt = Mathf.Lerp(currentTilt, wallRunTilt,
            1f - Mathf.Exp(-tiltTransitionSpeed * Time.deltaTime));
        ApplyCameraRotation();
    }

    private void ApplyCameraRotation()
    {
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, currentTilt);
    }

    private void CaptureCursor()
    {
        cursorReleased = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            ReleaseCursor();
        else if (!cursorReleased && isActiveAndEnabled)
            CaptureCursor();
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }
}
