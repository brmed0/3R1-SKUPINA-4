using UnityEngine;

public class WhellContoller1 : MonoBehaviour
{
    // The HUD reads these two
    public float ThrottleInput { get; private set; }
    public bool HandbrakeHeld { get; private set; }

    [SerializeField] WheelCollider FrontRight;
    [SerializeField] WheelCollider FrontLeft;
    [SerializeField] WheelCollider BackRight;
    [SerializeField] WheelCollider BackLeft;

    public float acceleration = 500f;
    public float breakingforce = 300f;
    public float maxSteerAngle = 15f;

    private void FixedUpdate()
    {
        ThrottleInput = Input.GetAxis("Vertical");     // -1 reverse, 0 nothing, 1 forward
        HandbrakeHeld = Input.GetKey(KeyCode.Space);

        float currentAcceleration = acceleration * ThrottleInput;
        float currentBreakingForce = HandbrakeHeld ? breakingforce : 0f;
        float currentSteerAngle = maxSteerAngle * Input.GetAxis("Horizontal");

        FrontRight.motorTorque = currentAcceleration;
        FrontLeft.motorTorque = currentAcceleration;

        FrontRight.brakeTorque = currentBreakingForce;
        FrontLeft.brakeTorque = currentBreakingForce;
        BackRight.brakeTorque = currentBreakingForce;
        BackLeft.brakeTorque = currentBreakingForce;

        FrontRight.steerAngle = currentSteerAngle;
        FrontLeft.steerAngle = currentSteerAngle;
    }
}