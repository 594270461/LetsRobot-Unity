﻿using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class roboSim : MonoBehaviour {

	//Robot Body & General Movement
	public GameObject robotBody; //Body of the robot.
	Rigidbody Body;
	public float turnSpeed = 0.2f; //How fast do i turn?
	public float moveSpeed = 0.2f; //How fast does it look like i move?

	float[] float4Imu = {0.0f, 0.0f, 0.0f, 0.0f};
	float[] float3Imu = {0.0f, 0.0f, 0.0f};

	//Body orientaiton in meat space
	Quaternion bodyRot;


	//Global Management
	public Material[] statusMaterials; //blue, green, yellow, red
	Robot robot; //Reference to data from Robot Class
	IDictionary<string, string> IMUData;
	IDictionary<string, string> panData;
	
	//Wheel Management
	public GameObject rightWheel;
	public GameObject leftWheel;

	//Renderer for the wheels
	Renderer leftRend;
	Renderer rightRend;

	bool moveLeftWheel = false;
	bool moveRightWheel = false;
	float moveTime = 1.0f;
	bool triggerMovement = false;
	

	//Gripper Management
	public GameObject gripperLeft;
	public GameObject gripperRight;

	/* Each gripper bit has an open and closed position stored, 
	   and a target position which switches between the two. */
	Vector3 leftGripperPos;
	Vector3 leftGripClosePos;
	Vector3 rightGripperPos;
	Vector3 rightGripClosePos;
	Vector3 leftGripTargetPos;
	Vector3 rightGripTargetPos;

	public bool closeGrip;
	bool triggerGrip = false;
	public float gripperMoveDist = 1.0f;
	public float gripSpeed = 1.0f;

	Renderer leftGripperRend;
	Renderer rightGripperRend;
	
	// Use this for initialization
	void Start () {

		//Find the robot object in order to get data to run the simulation
		if (GameObject.Find ("Robot")) {
			robot = GameObject.Find("Robot").GetComponent<Robot>();
		} else {
			//Debug.Log ("RoboSim not connecting to Robot");
		}

		//Robot will be moved by acting upon a rigid body... I think : D
		Body = robotBody.GetComponent<Rigidbody> ();
		bodyRot = robotBody.GetComponent<Transform> ().rotation;

		//Get Renderer for Left and Right Wheels
		leftRend = leftWheel.GetComponent<Renderer> ();
		rightRend = rightWheel.GetComponent<Renderer> ();


		//Managing gripper Setup
		//Get the gripper status from Constants, which is determined by the chat.
		closeGrip = Constants.gripperClose;
		leftGripperPos = gripperLeft.gameObject.GetComponent<Transform> ().localPosition;
		rightGripperPos = gripperRight.gameObject.GetComponent<Transform> ().localPosition;

		leftGripClosePos = new Vector3 (leftGripperPos.x + gripperMoveDist, 
		                                 leftGripperPos.y, 
		                                 leftGripperPos.z);

		rightGripClosePos = new Vector3 (rightGripperPos.x - gripperMoveDist,
		                                  rightGripperPos.y,
		                                  leftGripperPos.z);

		leftGripperRend = gripperLeft.GetComponent<Renderer> ();
		rightGripperRend = gripperRight.GetComponent<Renderer> ();

		rightGripTargetPos = rightGripperPos;
		leftGripTargetPos = leftGripperPos;
	}
	
	// Update is called once per frame
	float updateDir = 0.0f;
	void Update () {

		//Simulate input if the robot is not live to help with testing
		if (Constants.robotLive == false) {
			var inputSignal = simulateInput ();
			if (inputSignal.sqrMagnitude > 0.05f * 0.05f) {
				//Body.MovePosition(Body.position + (moveSpeed * Time.deltaTime) * inputSignal); //move in direction
				var targetRotation = Quaternion.LookRotation (inputSignal);
				Body.MoveRotation (Body.rotation.EaseTowards (targetRotation, turnSpeed));
			}
		} else {

			//Get variables from the robot / skynet if the robot is live.
			moveGripper ();
			fetchIMU ();

			//Trying to smooth out some botched IMU data. This is just a bandaid! 
			if (Constants.imuEuler.z >= 0.0f && Constants.imuEuler.z <= 360.0f) {
				updateDir = Constants.imuEuler.z;
			}

			bodyRot = Quaternion.Euler(0.0f, updateDir, 0.0f);
			Body.MoveRotation (Body.rotation.EaseTowards (bodyRot, turnSpeed));
			//robotBody.transform.localRotation = bodyRot;
		
		}

		moveHead();
	}

	//Logic for moving the gripper graphic on the robot sim.
	void moveGripper() {

		closeGrip = Constants.gripperClose;

		//Debug.Log ("Left Gripper Pos: " + leftGripperPos);
		//Debug.Log ("Right Gripper Pos: " + rightGripperPos);
		//Debug.Log ("Gripper Targets: " + leftGripTargetPos + " " + rightGripTargetPos);

			
			if (closeGrip == true) {
				leftGripTargetPos = leftGripClosePos;
				rightGripTargetPos = rightGripClosePos;
				//Debug.Log("Gripper Triggered");
			} else if (closeGrip == false) {
				leftGripTargetPos = leftGripperPos;
				rightGripTargetPos = rightGripperPos;
			}
		
		gripperLeft.transform.localPosition = 
			Vector3.Lerp (gripperLeft.transform.localPosition, 
			              leftGripTargetPos, 
			              gripSpeed * Time.deltaTime);
		gripperRight.transform.localPosition = 
			Vector3.Lerp (gripperRight.transform.localPosition, 
			              rightGripTargetPos, 
			              gripSpeed * Time.deltaTime);

		var leftGripCur = gripperLeft.transform.localPosition;
		var rightGripCur = gripperRight.transform.localPosition;
		
		if (Vector3.Distance (leftGripCur, leftGripTargetPos) > 0.05f || 
			Vector3.Distance (rightGripCur, rightGripTargetPos) > 0.05f) {
			leftGripperRend.sharedMaterial = statusMaterials [1];
			rightGripperRend.sharedMaterial = statusMaterials [1];
		} else {
			leftGripperRend.sharedMaterial = statusMaterials[0];
			rightGripperRend.sharedMaterial = statusMaterials[0];
		}
	}


	//MANAGE HEAD MOVEMENT ----------------------------------------------------------
	//values for pan and tilt, in degrees. 0 is center position by default.
	public float panHead = 0; 
	public float tiltHead = 0;
	
	public GameObject panner; //game object to apply pan transforms to
	public GameObject tilter; //game object to apply tilt transforms to

	public float panOffset = -90.0f; //Pan Calibration values
	public float tiltOffset = -120.0f; //Tilt Calibration values
	
	public float panTiltSpeed = 10.0f;

	void startHead() {

	}
	
	void moveHead () {


		if (Constants.robotLive == true && Constants.panTiltEnabled == true) {
			panHead = (float)Constants.headPan;
			tiltHead = (float)Constants.headTilt;
			panHead += panOffset;
			tiltHead += tiltOffset;
		}

		//Get current rotation, and create variable for target rotation.
		var panRotCur = panner.gameObject.transform.localRotation;
		Quaternion panTo = Quaternion.Euler(panRotCur.eulerAngles.x, panHead, panRotCur.eulerAngles.z);
		var tiltRotCur = tilter.gameObject.transform.localRotation;
		Quaternion tiltTo = Quaternion.Euler(tiltHead, tiltRotCur.y, tiltRotCur.z);


		//Pan head to target.
		if (panRotCur.y != panTo.y ) {

			panner.transform.localRotation = Quaternion.Slerp(panRotCur, panTo, panTiltSpeed *Time.deltaTime);
			//Debug.Log ("Pan thing is panning the thing");
		}

		//Tilt head to target.
		if (tiltRotCur.x != tiltTo.x) {

			tilter.transform.localRotation = Quaternion.Slerp (tiltRotCur, tiltTo, panTiltSpeed * Time.deltaTime);
			//Debug.Log ("Tilt thing is tilting the thing");
		}
	}

	//Simulate input when the robot is not live.-----------------------------------
	Vector3 simulateInput() {

		var simulateInput = new Vector3 (
				Input.GetAxisRaw("Horizontal"),
				Input.GetAxisRaw("Vertical"),
				Input.GetAxisRaw("Zed")
				);

		if (Input.GetKeyDown (KeyCode.F)) {

			Debug.Log("Key F is Down Yo");
			moveLeftWheel = true;
			moveRightWheel = true;
			leftRend.sharedMaterial = statusMaterials[1];
			rightRend.sharedMaterial = statusMaterials[1];

			triggerMovement = true;
			StartCoroutine("moveDuration");

		}

		if (Input.GetKeyDown (KeyCode.O)) {
			if (Constants.gripperClose == true) {
				Constants.gripperClose = false;


			} else if (Constants.gripperClose == false) {
			
				Constants.gripperClose = true;
			}

		}

		moveGripper();
		return simulateInput;
	}

	IEnumerator moveDuration () {

		if (triggerMovement == true && moveLeftWheel == true || moveRightWheel == true) {
			Debug.Log ("Movement Triggered");
			triggerMovement = false;
			yield return new WaitForSeconds (moveTime);
			moveLeftWheel = false;
			moveRightWheel = false;
			leftRend.sharedMaterial = statusMaterials [0];
			rightRend.sharedMaterial = statusMaterials [0];
			Debug.Log ("Movement Complete");

		} else {

			Debug.Log("We already movin yo!");
		}
	}


	float qx = 0.0f;
	float qy = 0.0f;
	float qz = 0.0f;
	float qw = 0.0f;

	float sqx = 0.0f;
	float sqy = 0.0f;
	float sqz = 0.0f;
	float sqw = 0.0f;

	void fetchIMU () {
		IMUData = robot.getIMUVariables();
		if (IMUData != null) {
			try {

				sqx = qx; sqy = qy; sqz = qz; sqw = qw;

				qx = (float)Convert.ToDouble(IMUData["quaternion_x"]);
				qy = (float)Convert.ToDouble(IMUData["quaternion_y"]);
				qz = (float)Convert.ToDouble(IMUData["quaternion_z"]);
				qw = (float)Convert.ToDouble(IMUData["quaternion_w"]);



				if (qx >= 0.0f && qx <= 1.0f && qx != sqx + sqx) {
					float4Imu[0] = qx;
				}
				
				if (qy >= 0.0f && qy <= 1.0f && qy != sqy + sqy) {
					float4Imu[1] = qy;
				}
				
				if (qz >= 0.0f && qz <= 1.0f && qz != sqz + sqz) {
					float4Imu[2] = qz;
				}
				
				if (qw >= 0.0f && qw <= 1.0f && qw != sqw + sqw) {
					float4Imu[3] = qw;
				}

				//Would prefer to use this method, but it needs some filtering.
				Constants.imuQuaternion = new Quaternion(qx, qy, qz, qw);

				//Constants.imuEuler = Constants.imuQuaternion.eulerAngles;

				float ex = (float)Convert.ToDouble(IMUData["euler_heading"]);
				float ey = (float)Convert.ToDouble (IMUData["euler_roll"]);
				float ez = (float)Convert.ToDouble (IMUData["euler_pitch"]);

				float[] efloat = float3Imu;

				if (ex >= 0.0f) {
					efloat[0] = ex;
				}

				if (ey >= 0.0f) {
					efloat[1] = ey;
				}

				if (ez >= 0.0f) {
					efloat[2] = ez;
				}

				Constants.imuEuler.x = ez;
				Constants.imuEuler.y = ey;
				Constants.imuEuler.z = ex; //cheap fix!
			
			/*Debug.Log ("Euler Rotation - Heading:" + efloat[0] + 
			           " Roll: " + efloat[1] + 
			           " Pitch: " + efloat[2]);*/

				float3Imu = efloat;
 
				} catch(KeyNotFoundException) {}

			//debugIMUData();
			} else {
				Debug.Log("No IMU Data found");
		}
	}

	void debugIMUData () {

		foreach (KeyValuePair<string, string> entry in IMUData) {
			Debug.Log ("Key");
			Debug.Log (entry.Key);
			Debug.Log ("Value");
			Debug.Log (entry.Value);
		}
	}
}