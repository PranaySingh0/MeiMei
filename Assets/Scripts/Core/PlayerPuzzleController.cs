using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerPuzzleController : MonoBehaviour
{
    [Header("Core Speeds / Physics")]
    [SerializeField] float walkSpeed = 16.6359f / 2.16f;
    [SerializeField] float gravity   = 2f * 6.94986f / (0.14f*0.14f);
    [SerializeField] float terminalVelocity = 300f;

    [Header("Buffer & Grace")]
    [SerializeField] float inputDelay  = 0.04f;
    [SerializeField] float coyoteTime  = 0.04f;

    [Header("Exact Turn Targets")]
    [SerializeField] float yaw45Time  = 0.025f;
    [SerializeField] float yaw180Time = 0.04f;

    [Header("Move Inertia (seconds to 95 %)")]
    [Range(.01f,1f)] public float accelTime = .10f;
    [Range(.01f,1f)] public float decelTime = .12f;
    [Header("Turn Inertia (seconds)")]
    [Range(.01f,.3f)] public float turnAccel = .04f;
    [Range(.01f,.3f)] public float turnDecel = .05f;

    [Header("Idle Timers")]
    [SerializeField] float idle1 =  3.5f;
    [SerializeField] float idle2 = 17.0f;
    [SerializeField] float idle3 = 50.0f;
    [SerializeField] float idle4 = 75.0f;

    [Header("References")]
    [SerializeField] Animator anim;

    [Header("Pick-up")]
    [SerializeField] float pickupDuration = 0.50f;
    [SerializeField] string pickupTrigger = "Pickup";

    CharacterController cc;

    Vector2 rawInput, pendingDir, activeDir;
    float bufferT; bool dirLocked;

    Vector3 planarVel, planarVelVel;
    float   yawVel, yawVelRef, yaw45Rate, yaw180Rate;

    float verticalVel, airborneT;
    bool  straightFall, stunned;

    float idleT; int idleStage;
    float spinWin=.75f, spinT; int spinCnt; float lastYaw;

    bool  pickingUp;
    float pickupT;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!anim) anim = GetComponentInChildren<Animator>();

        yaw45Rate  = 45f  / yaw45Time;
        yaw180Rate = 180f / yaw180Time;
        lastYaw    = transform.eulerAngles.y;
    }

    void Update()
    {
        if (pickingUp) { HandlePickupTimer(); return; }

        ReadInput();
        HandleGround();
        HandleMotion();
        HandleIdleSpin();
        PushAnim();
        CheckPickupKey();
    }

    void CheckPickupKey()
    {
        if (Input.GetKeyDown(KeyCode.C) &&
            cc.isGrounded && !straightFall && !pickingUp)
        {
            pickingUp = true;
            pickupT   = pickupDuration;
            planarVel = Vector3.zero; activeDir = Vector2.zero;
            dirLocked = false; yawVel = 0f;
            anim?.SetTrigger(pickupTrigger);
        }
    }

    void HandlePickupTimer()
    {
        pickupT -= Time.deltaTime;
        if (pickupT <= 0f) pickingUp = false;

        if (cc.isGrounded && verticalVel < 0f) verticalVel = -2f;
        verticalVel = Mathf.Max(verticalVel - gravity * Time.deltaTime, -terminalVelocity);
        cc.Move(Vector3.up * verticalVel * Time.deltaTime);

        PushAnim();
    }

    void ReadInput()
    {
        rawInput = Vector2.zero;
        if (Input.GetKey(KeyCode.A)) rawInput.x -= 1;
        if (Input.GetKey(KeyCode.D)) rawInput.x += 1;
        if (Input.GetKey(KeyCode.S)) rawInput.y -= 1;
        if (Input.GetKey(KeyCode.W)) rawInput.y += 1;
        if (rawInput.sqrMagnitude > .01f) rawInput.Normalize();

        bool any = rawInput.sqrMagnitude > .01f;

        if (dirLocked && any && Vector2.Angle(activeDir, rawInput) > 1f)
        { dirLocked=false; bufferT=0f; }

        if (!dirLocked && any)
        {
            bufferT += Time.deltaTime; pendingDir=rawInput;
            if (bufferT >= inputDelay) { activeDir=pendingDir; dirLocked=true; }
        }

        if (!any)
        { activeDir=Vector2.zero; dirLocked=false; bufferT=0f; }
    }

    void HandleGround()
    {
        bool grounded=cc.isGrounded;
        airborneT = grounded?0:airborneT+Time.deltaTime;

        if (!grounded && airborneT>coyoteTime && !straightFall)
        { straightFall=true; activeDir=Vector2.zero; stunned=true; }

        if (grounded && straightFall)
        { straightFall=false; stunned=false; }
    }

    void HandleMotion()
    {
        Vector3 camFwd = Camera.main.transform.forward; camFwd.y=0; camFwd.Normalize();
        Vector3 camRight = new(camFwd.z,0,-camFwd.x);

        Vector3 targetVel = Vector3.zero;
        if (activeDir.sqrMagnitude>.01f && !stunned)
            targetVel = (camRight*activeDir.x + camFwd*activeDir.y).normalized * walkSpeed;

        float mvSmooth = (targetVel.sqrMagnitude>planarVel.sqrMagnitude) ? accelTime : decelTime;
        planarVel = Vector3.SmoothDamp(planarVel, targetVel, ref planarVelVel, mvSmooth);

        if (planarVel.sqrMagnitude>.01f)
        {
            Quaternion tgt = Quaternion.LookRotation(planarVel);
            float ang = Quaternion.Angle(transform.rotation, tgt);
            float tgtSpeed = (ang>90f?yaw180Rate:yaw45Rate) * Mathf.Sign(
                             Mathf.DeltaAngle(transform.eulerAngles.y, tgt.eulerAngles.y));
            float trnSmooth = (Mathf.Abs(tgtSpeed)>Mathf.Abs(yawVel)) ? turnAccel:turnDecel;
            yawVel = Mathf.SmoothDamp(yawVel, tgtSpeed, ref yawVelRef, trnSmooth);
        }
        else
            yawVel = Mathf.SmoothDamp(yawVel, 0f, ref yawVelRef, turnDecel);

        transform.Rotate(Vector3.up, yawVel*Time.deltaTime, Space.World);

        if (cc.isGrounded && verticalVel<0) verticalVel=-2f;
        verticalVel = Mathf.Max(verticalVel - gravity*Time.deltaTime, -terminalVelocity);

        cc.Move((planarVel + Vector3.up*verticalVel)*Time.deltaTime);
    }

    void HandleIdleSpin()
    {
        if (planarVel.sqrMagnitude<.01f && !stunned) idleT+=Time.deltaTime;
        else { idleT=0; idleStage=0; }

        if      (idleStage==0&&idleT>=idle1){anim?.SetTrigger("Idle1");idleStage=1;}
        else if (idleStage==1&&idleT>=idle2){anim?.SetTrigger("Idle2");idleStage=2;}
        else if (idleStage==2&&idleT>=idle3){anim?.SetTrigger("Idle3");idleStage=3;}
        else if (idleStage==3&&idleT>=idle4){anim?.SetTrigger("Idle4");idleStage=4;}

        float yawNow=transform.eulerAngles.y;
        float d=Mathf.DeltaAngle(lastYaw,yawNow); lastYaw=yawNow;

        if (Mathf.Abs(d)>300f)
        {
            spinT+=Time.deltaTime;
            if (spinT<=spinWin && ++spinCnt>=3)
            { anim?.SetTrigger("Spin"); spinT=0; spinCnt=0; }
        }
        else
        { spinT=Mathf.Max(0,spinT-Time.deltaTime); if(spinT==0)spinCnt=0; }
    }

    void PushAnim()
    {
        if (!anim) return;
        anim.SetFloat("Speed", planarVel.magnitude / walkSpeed);
        anim.SetBool ("Falling", straightFall || !cc.isGrounded);
        anim.SetBool ("Stunned", stunned);
    }
}