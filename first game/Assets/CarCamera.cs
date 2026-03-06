using UnityEngine;

public class CarCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 6.0f;
    public float height = 2.5f;
    public float smoothSpeed = 10.0f;
    public float rotationSmoothSpeed = 5.0f;

    [Header("Direction Corrections")]
    [Tooltip("If the camera is in front of the car, check this to flip it to the back.")]
    public bool invertDirection = false;

    private void FixedUpdate()
    {
        if (target == null) return;

        // If the car model is facing backwards, we use target.forward instead of -target.forward
        Vector3 direction = invertDirection ? target.forward : -target.forward;

        Vector3 desiredPosition = target.position + direction * distance + Vector3.up * height;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        Quaternion desiredRotation = Quaternion.LookRotation(target.position - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothSpeed * Time.deltaTime);
    }
}
