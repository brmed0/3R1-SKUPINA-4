using UnityEngine;

public class WhellContoller1 : MonoBehaviour
{
  [SerializeField] WheelCollider FrontRight;
  [SerializeField] WheelCollider FrontLeft;
  [SerializeField] WheelCollider BackRight;
  [SerializeField] WheelCollider BackLeft;

  public float acceleration = 500f;
  public float breakingforce = 300f;
  public float maxSteerAngle = 15f;

  private float currentAcceleration = 0f;
  private float currentBreakingForce = 0f;
  private float currentSteerAngle = 0f; 


  private void FixedUpdate(){

    currentAcceleration = acceleration * Input.GetAxis("Vertical");
    currentSteerAngle = maxSteerAngle * Input.GetAxis("Horizontal");

    if(Input.GetKey(KeyCode.Space)){
      currentBreakingForce = breakingforce;
    }
      else{
        currentBreakingForce = 0f;
  }

  FrontRight.motorTorque = currentAcceleration;
  FrontLeft.motorTorque = currentAcceleration;

  FrontRight.brakeTorque = currentBreakingForce;
  FrontLeft.brakeTorque = currentBreakingForce;
  BackRight.brakeTorque = currentBreakingForce;
  BackLeft.brakeTorque = currentBreakingForce;

  currentSteerAngle = maxSteerAngle * Input.GetAxis("Horizontal");
  FrontRight.steerAngle = currentSteerAngle;
  FrontLeft.steerAngle = currentSteerAngle;

}

}
