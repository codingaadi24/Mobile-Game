using UnityEngine;
using UnityEngine.InputSystem;

public class car_controllar : MonoBehaviour
{
    public WheelCollider frontLeftCollider, frontRightCollider, rearLeftCollider, rearRightCollider;
    public Transform frontLeftMesh, frontRightMesh, rearLeftMesh, rearRightMesh;
    
    public float motorForce = 1500f; // How fast the car goes
    public float maxSteeringAngle = 30f; // How far the wheels turn

    private float currentSteerAngle;
    private float currentMotorForce;

    private void FixedUpdate()
    {
        GetInput();
        Steer();
        Accelerate();
    }

    private void Update()
    {
        UpdateWheelPoses();
    }

    private void UpdateWheelPoses()
    {
        UpdateWheelPose(frontLeftCollider, frontLeftMesh);
        UpdateWheelPose(frontRightCollider, frontRightMesh);
        UpdateWheelPose(rearLeftCollider, rearLeftMesh);
        UpdateWheelPose(rearRightCollider, rearRightMesh);
    }

    private void UpdateWheelPose(WheelCollider collider, Transform mesh)
    {
        if (mesh == null) return;
        
        Vector3 position;
        Quaternion rotation;
        collider.GetWorldPose(out position, out rotation);
        
        mesh.position = position;
        mesh.rotation = rotation;
    }

    private void GetInput()
    {
        float verticalAxis = 0f;
        float horizontalAxis = 0f;

        // Keyboard input handling (W/S/A/D and Arrows)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) verticalAxis += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) verticalAxis -= 1f;
            
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontalAxis += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontalAxis -= 1f;
        }

        // Gamepad input handling (Left Stick)
        if (Gamepad.current != null)
        {
            Vector2 leftStick = Gamepad.current.leftStick.ReadValue();
            if (leftStick.magnitude > 0.1f)
            {
                horizontalAxis = leftStick.x;
                verticalAxis = leftStick.y;
            }
        }

        currentMotorForce = verticalAxis * motorForce;
        currentSteerAngle = horizontalAxis * maxSteeringAngle;
    }

    private void Steer()
    {
        frontLeftCollider.steerAngle = currentSteerAngle;
        frontRightCollider.steerAngle = currentSteerAngle;
    }

    
    private void Accelerate()
    {
        rearLeftCollider.motorTorque = currentMotorForce;
        rearRightCollider.motorTorque = currentMotorForce;
        Debug.Log("Current Motor Force: " + currentMotorForce); // Add this line!
    }

}
