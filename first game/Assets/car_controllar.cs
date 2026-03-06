using UnityEngine;
using UnityEngine.InputSystem;

public class car_controllar : MonoBehaviour
{
    public WheelCollider frontLeftCollider, frontRightCollider, rearLeftCollider, rearRightCollider;
    public Transform frontLeftMesh, frontRightMesh, rearLeftMesh, rearRightMesh;
    
    public float motorForce = 50000f;
    public float brakeForce = 40000f;
    public float idleDrag = 2000f;
    [Header("Lateral Dynamics")]
    public float maxSteeringAngle = 30f;
    [Tooltip("The speed at which steering angle starts to noticeably decrease to prevent flipping")]
    public float speedForMinSteer = 50f; 
    [Tooltip("The minimum steering angle allowed when driving at high speeds")]
    public float minSteeringAngle = 10f;

    [Header("Direction Corrections")]
    public bool invertDrive = true;
    public bool invertSteering = false;
    
    [Header("Engine & RPM Settings")]
    public float minRPM = 1000f;
    public float maxRPM = 7000f;
    public float engineRPM;
    [Tooltip("X-axis: RPM. Y-axis: Torque Multiplier (0.0 to 1.0)")]
    public AnimationCurve torqueCurve = new AnimationCurve(
        new Keyframe(1000f, 0.2f), 
        new Keyframe(3000f, 0.5f), 
        new Keyframe(6000f, 1.0f), 
        new Keyframe(7500f, 0.8f) // Redline torque drop
    );

    [Header("Gearbox Settings")]
    public float[] gears = { 3.5f, 2.1f, 1.4f, 1.0f, 0.8f }; // 5 gears
    public float finalDriveRatio = 1.5f; // Extra multiplier to calculate realistic RPM
    public int currentGear = 0; // 0 = 1st Gear
    [Tooltip("RPM at which automatic transmission shifts up")]
    public float shiftUpRPM = 6500f;
    [Tooltip("RPM at which automatic transmission shifts down")]
    public float shiftDownRPM = 2500f;

    [Header("Analog Speedometer (Needle)")]
    [Tooltip("Drag the RectTransform of the Needle Image here")]
    public RectTransform speedNeedle;
    [Tooltip("Rotation Z angle of the needle when speed is 0")]
    public float minNeedleAngle = 135f;
    [Tooltip("Rotation Z angle of the needle when speed is maxed out")]
    public float maxNeedleAngle = -135f;
    [Tooltip("Max KM/H written on your UI gauge")]
    public float maxSpeedForNeedle = 260f;

    [Header("Physics Settings (Stability)")]
    [Tooltip("Rules:\nLower Y value -> More stable, sticks to ground.\nHigher Y value -> High rollover risk (flips easily)")]
    public Vector3 centerOfMassOffset = new Vector3(0, -0.5f, 0);
    public float antiRoll = 30000f;
    [Tooltip("Pushes the car into the track at high speeds (Aerodynamics)")]
    public float downforce = 100f;

    private float currentSteerAngle;
    private float currentMotorForce;
    private float currentBrakeForce;
    private bool isBraking;
    private Rigidbody rb;

    private void Start()
    {
        if (frontLeftCollider != null)
        {
            rb = frontLeftCollider.attachedRigidbody;
        }

        if (rb != null)
        {
            // CRITICAL: Force the Center of Mass X to be perfectly 0 so the car never pulls to one side
            Vector3 com = rb.centerOfMass;
            com.x = 0f; 
            rb.centerOfMass = com + centerOfMassOffset;
        }
        else
        {
            Debug.LogError("Could not find the car's Rigidbody! Make sure the WheelColliders are attached to a car with a Rigidbody.");
        }
    }

    private void FixedUpdate()
    {
        GetInput();
        Steer();
        CalculateRPM();
        HandleGearbox();
        Accelerate();
        ApplyAntiRoll();
        CalculateAndApplyWeightTransfer();
        UpdateTireFriction();
        ApplyDownforce();
    }

    private void CalculateRPM()
    {
        // Calculate the rotation speed of the rear driving wheels
        float wheelRPM = (Mathf.Abs(rearLeftCollider.rpm) + Mathf.Abs(rearRightCollider.rpm)) / 2f;
        
        // Convert to Engine Speed via Gear Ratio & Final Drive
        engineRPM = minRPM + (wheelRPM * gears[currentGear] * finalDriveRatio);

        // Clamp to engine redline
        engineRPM = Mathf.Clamp(engineRPM, minRPM, maxRPM + 500f); 
    }

    private void HandleGearbox()
    {
        // Simple Automatic Transmission
        // Shift up
        if (engineRPM >= shiftUpRPM && currentGear < gears.Length - 1)
        {
            currentGear++;
        }
        // Shift down
        else if (engineRPM <= shiftDownRPM && currentGear > 0)
        {
            currentGear--;
        }
    }

    private void ApplyDownforce()
    {
        if (rb != null)
        {
            // Aerodynamic downforce pushes the car into the track based on how fast it's moving
            rb.AddForce(-transform.up * downforce * rb.linearVelocity.magnitude);
        }
    }

    private void Update()
    {
        UpdateWheelPoses();
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (rb == null) return;

        float speedKPH = rb.linearVelocity.magnitude * 3.6f;

        // Rotate Analog Speedometer Needle
        if (speedNeedle != null)
        {
            float speedFactor = Mathf.Clamp01(speedKPH / maxSpeedForNeedle);
            float targetAngle = Mathf.Lerp(minNeedleAngle, maxNeedleAngle, speedFactor);
            // Notice: Z axis handles UI rotation 
            speedNeedle.localEulerAngles = new Vector3(0, 0, targetAngle);
        }
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

    private float targetSteerAngle;
    private float steerVelocity; 
    [Tooltip("Smoothing factor for how fast the steering wheel turns. Lower is faster.")]
    public float steeringSmoothTime = 0.1f;

    private void GetInput()
    {
        float verticalAxis = 0f;
        float horizontalAxis = 0f;
        isBraking = false;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) verticalAxis += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) verticalAxis -= 1f;
            
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontalAxis += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontalAxis -= 1f;
            
            if (Keyboard.current.spaceKey.isPressed) isBraking = true;
        }

        if (Gamepad.current != null)
        {
            Vector2 leftStick = Gamepad.current.leftStick.ReadValue();
            // Added strict 0.15f Deadzone to prevent controller stick drift from constantly steering the car
            if (leftStick.magnitude > 0.15f)
            {
                horizontalAxis = leftStick.x;
                verticalAxis = leftStick.y;
            }
            
            if (Gamepad.current.buttonSouth.isPressed) isBraking = true;
        }

        currentMotorForce = verticalAxis * motorForce;
        if (invertDrive) currentMotorForce *= -1f;
        
        currentBrakeForce = isBraking ? brakeForce : 0f;

        // Determine current max steering angle based on speed (simulated rack limitations)
        float currentSpeed = rb.linearVelocity.magnitude;
        float dynamicMaxSteer = Mathf.Lerp(maxSteeringAngle, minSteeringAngle, currentSpeed / speedForMinSteer);

        targetSteerAngle = horizontalAxis * dynamicMaxSteer;
        
        // Remove manual invertSteering from here, WheelColliders handle their own inverse
        if (invertSteering) targetSteerAngle *= -1f;

        // Smoothly interpolate current steering angle to simulate physical tire turning speed
        currentSteerAngle = Mathf.SmoothDamp(currentSteerAngle, targetSteerAngle, ref steerVelocity, steeringSmoothTime);
    }

    private void Steer()
    {
        frontLeftCollider.steerAngle = currentSteerAngle;
        frontRightCollider.steerAngle = currentSteerAngle;
    }

    private void Accelerate()
    {
        // Simple logic without rigidBody velocity checks for now
        // W/S directly control motor vs brake
        bool pressingForward = currentMotorForce > 0.1f;
        bool pressingBackward = currentMotorForce < -0.1f;

        // Formula: wheelTorque = engineTorque * gearRatio
        float torqueMultiplier = torqueCurve.Evaluate(engineRPM);
        float realMotorForce = currentMotorForce * torqueMultiplier * gears[currentGear];
        
        // Traction Control System (TCS)
        // If the rear wheels receive 50000+ motor torque, they spin so fast they lose all lateral friction, causing severe fishtailing.
        WheelHit hit;
        if (rearLeftCollider.GetGroundHit(out hit) && Mathf.Abs(hit.forwardSlip) > 0.8f)
        {
            realMotorForce *= 0.5f; // Cut power if wheels are spinning dangerously fast
        }
        else if (rearRightCollider.GetGroundHit(out hit) && Mathf.Abs(hit.forwardSlip) > 0.8f)
        {
            realMotorForce *= 0.5f;
        }

        if (isBraking)
        {
            ApplyBraking(brakeForce);
            rearLeftCollider.motorTorque = 0f;
            rearRightCollider.motorTorque = 0f;
        }
        else if (pressingForward)
        {
            // Moving FORWARD
            ApplyBraking(0f);
            rearLeftCollider.motorTorque = realMotorForce;
            rearRightCollider.motorTorque = realMotorForce;
        }
        else if (pressingBackward)
        {
            // Moving BACKWARD
            ApplyBraking(0f);
            rearLeftCollider.motorTorque = realMotorForce;
            rearRightCollider.motorTorque = realMotorForce;
        }
        else
        {
            // COASTING
            ApplyBraking(idleDrag);
            rearLeftCollider.motorTorque = 0f;
            rearRightCollider.motorTorque = 0f;
        }
    }

    private void ApplyBraking(float force)
    {
        frontLeftCollider.brakeTorque = force;
        frontRightCollider.brakeTorque = force;
        rearLeftCollider.brakeTorque = force;
        rearRightCollider.brakeTorque = force;
    }

    private void ApplyAntiRoll()
    {
        if (rb == null) return;
        
        ApplyAntiRollAxle(frontLeftCollider, frontRightCollider);
        ApplyAntiRollAxle(rearLeftCollider, rearRightCollider);
    }

    private void ApplyAntiRollAxle(WheelCollider leftWheel, WheelCollider rightWheel)
    {
        WheelHit hit;
        float travelL = 1.0f;
        float travelR = 1.0f;
        
        bool groundedL = leftWheel.GetGroundHit(out hit);
        if (groundedL) travelL = (-leftWheel.transform.InverseTransformPoint(hit.point).y - leftWheel.radius) / leftWheel.suspensionDistance;

        bool groundedR = rightWheel.GetGroundHit(out hit);
        if (groundedR) travelR = (-rightWheel.transform.InverseTransformPoint(hit.point).y - rightWheel.radius) / rightWheel.suspensionDistance;
        
        // Calculate the difference in compression between the two wheels
        float antiRollForce = (travelL - travelR) * antiRoll;

        // CRITICAL STABILITY FIX: Cap the maximum force to prevent the car from violently vibrating ("Dancing")
        antiRollForce = Mathf.Clamp(antiRollForce, -antiRoll, antiRoll);

        // STABILITY FIX 2: We must apply the force EVEN IF the tire is lifted in the air. 
        // This physically yanks the lifted side of the car back down onto the road.
        rb.AddForceAtPosition(leftWheel.transform.up * -antiRollForce, leftWheel.transform.position);
        rb.AddForceAtPosition(rightWheel.transform.up * antiRollForce, rightWheel.transform.position);
    }

    private Vector3 lastVelocity;
    [Header("Weight Transfer Settings")]
    public float weightTransferIntensity = 250f; // Lowered severely so corner weight-shift doesn't flip the car

    private void CalculateAndApplyWeightTransfer()
    {
        if (rb == null) return;

        // Calculate G-Force (Acceleration) based on how velocity changes each frame
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 acceleration = (currentVelocity - lastVelocity) / Time.fixedDeltaTime;
        lastVelocity = currentVelocity;

        // Convert acceleration out of world space and into local car direction space
        Vector3 localAcceleration = transform.InverseTransformDirection(acceleration);

        if (invertDrive) localAcceleration.z = -localAcceleration.z; // Invert Z-axis if model is backwards

        // Shift weight longitudinally (Front/Rear)
        float longTransferForce = localAcceleration.z * weightTransferIntensity;
        
        // Shift weight laterally (Left/Right)
        float latTransferForce = localAcceleration.x * weightTransferIntensity;

        ApplyDownforceToWheel(frontLeftCollider,  -longTransferForce + latTransferForce);
        ApplyDownforceToWheel(frontRightCollider, -longTransferForce - latTransferForce);
        ApplyDownforceToWheel(rearLeftCollider,    longTransferForce + latTransferForce);
        ApplyDownforceToWheel(rearRightCollider,   longTransferForce - latTransferForce);
    }

    private void ApplyDownforceToWheel(WheelCollider wheel, float extraForce)
    {
        if (!wheel.isGrounded) return;

        // Press down directly on the wheel to increase its compression and grip dynamically
        if (extraForce > 0)
        {
            rb.AddForceAtPosition(-wheel.transform.up * extraForce, wheel.transform.position);
        }
    }

    [Header("Tire Slip Settings")]
    public float baseGrip = 1.0f;
    public float slipGripMultiplier = 0.5f; // How much grip is lost when slipping (drifting)
    public float slipThreshold = 0.5f;      // How much slip ratio triggers a drift

    private void UpdateTireFriction()
    {
        AdjustFriction(frontLeftCollider);
        AdjustFriction(frontRightCollider);
        AdjustFriction(rearLeftCollider);
        AdjustFriction(rearRightCollider);
    }

    private void AdjustFriction(WheelCollider wheel)
    {

    }
}
