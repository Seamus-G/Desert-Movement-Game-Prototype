using UnityEngine;

public class MouseLook : MonoBehaviour
{
    public Transform cameraTransform;
    public float sensitivity = 2f;
    public float wallRunTilt;
    public float tiltTransitionSpeed = 8f;

    private float pitch;
    private float currentTilt;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

        transform.Rotate(Vector3.up * mouseX);
        pitch = Mathf.Clamp(pitch - mouseY, -80f, 80f);
        currentTilt = Mathf.Lerp(currentTilt, wallRunTilt, tiltTransitionSpeed * Time.deltaTime);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, currentTilt);
    }
}
