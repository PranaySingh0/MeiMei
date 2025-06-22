using UnityEngine;

public sealed class OrbitPuzzleCamera : MonoBehaviour
{
    [Header("Scene references")]
    public Transform mapCentre;
    public Transform player;
    public Transform hPivot;
    public Transform vPivot;
    public Camera    cam;

    [Header("Constant speeds")]
    public float revolutionSec = 4.18f;
    public float sideToTopSec  = 1.10f;

    [Header("Feel / Inertia")]
    [Tooltip("Seconds to reach 95 % of target angular speed")]
    [Range(.01f, 1f)] public float accelTime = .15f;
    [Tooltip("Seconds for speed to decay to 5 % after input released")]
    [Range(.01f, 1f)] public float decelTime = .20f;
    [Tooltip("Extra damping when trailing the player (zoom 1-2)")]
    [Range(.05f, .6f)] public float followSmooth = .20f;

    [Header("Zoom stages (0 = map, 1-2 = player)")]
    public Vector3[] pivotOffset = { new(0,1.6f,0), new(0,1.3f,0), new(0,1.0f,0) };
    public float[]   distance    = { 10f, 6f, 3.5f };
    public float[]   fov         = { 60f, 52f, 40f };
    public float     zoomLerp    = .35f;

    int     zoom;
    Vector3 pivotOffsetCur, pivotOffsetVel;

    float   yawSpeedSS;
    float   pitchSpeedSS;

    float   yawVel,   yawVelRef;
    float   pitchVel, pitchVelRef;

    void Start()
    {
        if (!cam) cam = GetComponentInChildren<Camera>();
        pivotOffsetCur = pivotOffset[0];

        yawSpeedSS   = 360f / revolutionSec;
        pitchSpeedSS =  90f / sideToTopSec;
    }

    void LateUpdate()
    {
        if (!mapCentre || !player) return;

        SmoothInputToVelocity();
        ApplyRotation();
        HandleZoomCycle();
        UpdatePivotAndCamera();
        if (Input.GetKeyDown(KeyCode.R)) SnapReset();
    }

    void SmoothInputToVelocity()
    {
        float rawYawInput =
              (Input.GetKey(KeyCode.LeftArrow)  ? -1 : 0)
            + (Input.GetKey(KeyCode.Q)          ? -1 : 0)
            + (Input.GetKey(KeyCode.RightArrow) ?  1 : 0)
            + (Input.GetKey(KeyCode.E)          ?  1 : 0);

        float rawPitchInput =
              (Input.GetKey(KeyCode.UpArrow)   ?  1 : 0)
            + (Input.GetKey(KeyCode.DownArrow) ? -1 : 0);

        float yawTargetSpeed   = rawYawInput   * yawSpeedSS;
        float pitchTargetSpeed = rawPitchInput * pitchSpeedSS;

        float yawSmooth   = (Mathf.Abs(rawYawInput) > .01f) ? accelTime : decelTime;
        float pitchSmooth = (Mathf.Abs(rawPitchInput)> .01f) ? accelTime : decelTime;

        yawVel   = Mathf.SmoothDamp(yawVel,   yawTargetSpeed,   ref yawVelRef,   yawSmooth);
        pitchVel = Mathf.SmoothDamp(pitchVel, pitchTargetSpeed, ref pitchVelRef, pitchSmooth);
    }

    void ApplyRotation()
    {
        hPivot.Rotate(Vector3.up, yawVel * Time.deltaTime, Space.World);

        float curPitch = vPivot.localEulerAngles.x;
        if (curPitch > 180f) curPitch -= 360f;
        curPitch += pitchVel * Time.deltaTime;
        curPitch  = Mathf.Clamp(curPitch, 0f, 90f);
        vPivot.localEulerAngles = new Vector3(curPitch, 0, 0);
    }

    void HandleZoomCycle()
    {
        if (Input.GetKeyDown(KeyCode.V))
            zoom = (zoom + 1) % pivotOffset.Length;
    }

    void UpdatePivotAndCamera()
    {
        pivotOffsetCur = Vector3.SmoothDamp(
            pivotOffsetCur, pivotOffset[zoom], ref pivotOffsetVel, zoomLerp);

        Vector3 anchor = (zoom == 0) ? mapCentre.position : player.position;

        if (zoom == 0)
            hPivot.position = anchor + pivotOffsetCur;
        else
            hPivot.position = Vector3.Lerp(hPivot.position,
                                           anchor + pivotOffsetCur,
                                           Time.deltaTime / followSmooth);

        Vector3 wantLocal = new(0,0,-distance[zoom]);
        cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition,
                                                   wantLocal,
                                                   Time.deltaTime / zoomLerp);

        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov[zoom],
                                     Time.deltaTime / zoomLerp);
    }

    void SnapReset()
    {
        zoom = 0;
        yawVel = pitchVel = yawVelRef = pitchVelRef = 0f;

        pivotOffsetCur = pivotOffset[0];
        hPivot.position      = mapCentre.position + pivotOffsetCur;
        hPivot.rotation      = Quaternion.Euler(0,45,0);
        vPivot.localRotation = Quaternion.Euler(30,0,0);
        cam.transform.localPosition = new Vector3(0,0,-distance[0]);
        cam.fieldOfView = fov[0];
    }
}