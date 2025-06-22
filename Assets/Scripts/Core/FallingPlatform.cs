

using System.Collections;
using UnityEngine;

[AddComponentMenu("Gameplay/Cloud Falling Platform w/ Shake & Jelly Pop")]
[RequireComponent(typeof(BoxCollider))]
public sealed class CloudFallingPlatformJelly : MonoBehaviour
{
    
    [Header("Press / Release")]
    public float PressDip = 0.25f;
    public float PressDuration = 0.35f;
    public float ReleaseDuration = 0.45f;
    [SerializeField] AnimationCurve pressCurve;
    [SerializeField] AnimationCurve releaseCurve;

    
    [Header("Strain Shake")]
    public float ShakeStartDelay = 0.15f;
    public float PreFallPause = 0.12f;
    public float ShakeMaxAmplitude = 0.04f;
    public float ShakeSpeed = 8f;
    public AnimationCurve ShakeRamp = AnimationCurve.EaseInOut(0, 0, 1, 1);

    
    [Header("Drop")]
    public float StandTime = 0.8f;
    public float FallDuration = 0.30f;
    public float FallDistance = 5f;
    [SerializeField] AnimationCurve fallCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float RespawnDelay = 4f;

    
    [Header("Respawn Jelly Pop")]
    [Range(0.05f, 0.5f)] public float PopStartScale = 0.12f;
    public float PopDuration = 0.60f;
    [Range(1.01f, 1.5f)] public float PopOvershoot = 1.30f;
    [SerializeField] AnimationCurve PopCurve;
    [Range(0, 1)] public float SquashIntensity = 0.6f;
    public float PopSettleTail = 0.08f;

    
    [Header("Sensor Slab")]
    public float SensorGap = 0.02f;
    public float SensorHeight = 0.15f;

    [Header("Visuals (auto-fill if empty)")]
    public Renderer[] Visuals;

    
    enum Phase { Ready, Held, Falling, Respawning }
    Phase phase;

    BoxCollider solidCol, sensorCol;
    Rigidbody rb;
    Vector3 baseScale, startPos, pressedPos;
    int feetInside;
    Coroutine springCo, waitCo, shakeCo, popCo;

    
    void Awake()
    {
        baseScale = transform.localScale;

        if (pressCurve == null || pressCurve.length == 0)
            pressCurve = new AnimationCurve(
                new Keyframe(0, 0, 0, 0.8f),
                new Keyframe(.75f, 1.06f, 0, -.75f),
                new Keyframe(1, 1, -.75f, 0));
        if (releaseCurve == null || releaseCurve.length == 0)
            releaseCurve = new AnimationCurve(
                new Keyframe(0, 0, 0, 2),
                new Keyframe(.55f, -.06f, 0, .4f),
                new Keyframe(1, 0, .4f, 0));
        if (PopCurve == null || PopCurve.length == 0)
            PopCurve = new AnimationCurve(
                new Keyframe(0, 0, 0, 3),
                new Keyframe(.30f, 1.25f, 0, -2.5f),
                new Keyframe(.55f, 0.85f, 0, 2),
                new Keyframe(.75f, 1.10f, 0, -1.2f),
                new Keyframe(.90f, 0.97f, 0, .6f),
                new Keyframe(1, 1, .4f, 0));

        solidCol = GetComponent<BoxCollider>();
        solidCol.isTrigger = false;

        rb = TryGetComponent(out Rigidbody r) ? r : gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        sensorCol = gameObject.AddComponent<BoxCollider>();
        sensorCol.isTrigger = true;
        Vector3 s = solidCol.size, c = solidCol.center;
        sensorCol.size = new Vector3(s.x, SensorHeight, s.z);
        sensorCol.center = new Vector3(c.x, c.y + s.y * 0.5f + SensorGap + SensorHeight * 0.5f, c.z);

        if (Visuals == null || Visuals.Length == 0)
            Visuals = GetComponentsInChildren<Renderer>(true);

        startPos = transform.position;
        pressedPos = startPos + Vector3.down * PressDip;
        phase = Phase.Ready;
    }

    
    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerFoot")) return;
        feetInside++;
        if (feetInside == 1 && phase == Phase.Ready)
        {
            StartSpring(true);
            waitCo = StartCoroutine(WaitShakeDrop());
            phase = Phase.Held;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("PlayerFoot")) return;
        feetInside = Mathf.Max(0, feetInside - 1);
        if (feetInside == 0 && phase == Phase.Held)
        {
            if (waitCo  != null) { StopCoroutine(waitCo);  waitCo  = null; }
            if (shakeCo != null) { StopCoroutine(shakeCo); shakeCo = null; }
            StartSpring(false);
            phase = Phase.Ready;
        }
    }

    
    void StartSpring(bool down)
    {
        if (springCo != null) StopCoroutine(springCo);
        Vector3 target = down ? pressedPos : startPos;
        float dur = down ? PressDuration : ReleaseDuration;
        AnimationCurve cv = down ? pressCurve : releaseCurve;
        springCo = StartCoroutine(SpringTween(target, dur, cv));
    }

    IEnumerator SpringTween(Vector3 target, float dur, AnimationCurve cv)
    {
        Vector3 from = transform.position;
        for (float t = 0; t < 1f; t += Time.fixedDeltaTime / dur)
        {
            rb.MovePosition(Vector3.LerpUnclamped(from, target, cv.Evaluate(t)));
            yield return new WaitForFixedUpdate();
        }
        SnapPos(target);
        springCo = null;
    }

    
    IEnumerator WaitShakeDrop()
    {
        float shakeWindow = Mathf.Max(0, StandTime - ShakeStartDelay - PreFallPause);
        while (springCo != null) yield return null;
        if (ShakeStartDelay > 0) yield return new WaitForSeconds(ShakeStartDelay);
        if (shakeWindow > 0) shakeCo = StartCoroutine(ShakeRoutine(shakeWindow));
        yield return new WaitForSeconds(shakeWindow);
        if (shakeCo != null) { StopCoroutine(shakeCo); shakeCo = null; }
        SnapPos(pressedPos);
        yield return new WaitForSeconds(PreFallPause);
        StartCoroutine(DropRoutine());
    }

    IEnumerator ShakeRoutine(float total)
    {
        float elapsed = 0;
        while (elapsed < total)
        {
            elapsed += Time.fixedDeltaTime;
            float pct = Mathf.Clamp01(elapsed / total);
            float amp = ShakeMaxAmplitude * ShakeRamp.Evaluate(pct);
            float f = ShakeSpeed;
            Vector3 offset = new Vector3(
                (Mathf.PerlinNoise(Time.time * f, 0) - .5f) * amp,
                0,
                (Mathf.PerlinNoise(0, Time.time * f) - .5f) * amp);
            rb.MovePosition(pressedPos + offset);
            yield return new WaitForFixedUpdate();
        }
        shakeCo = null;
    }

    
    IEnumerator DropRoutine()
    {
        phase = Phase.Falling;
        SnapPos(pressedPos);

        Vector3 from = pressedPos;
        Vector3 to = startPos + Vector3.down * FallDistance;
        for (float t = 0; t < 1f; t += Time.fixedDeltaTime / FallDuration)
        {
            rb.MovePosition(Vector3.LerpUnclamped(from, to, fallCurve.Evaluate(t)));
            yield return new WaitForFixedUpdate();
        }

        foreach (var r in Visuals) r.enabled = false;
        solidCol.enabled = sensorCol.enabled = false;
        yield return new WaitForSeconds(RespawnDelay);

        
        transform.localScale = baseScale * PopStartScale;
        SnapPos(startPos - Vector3.up * .1f);
        foreach (var r in Visuals) r.enabled = true;
        popCo = StartCoroutine(PopRoutine());
        yield return popCo;

        solidCol.enabled = sensorCol.enabled = true;
        feetInside = 0;
        phase = Phase.Ready;
    }

    
    IEnumerator PopRoutine()
    {
        Vector3 posA = startPos - Vector3.up * .1f;
        Vector3 posB = startPos;

        Vector3 tiny = baseScale * PopStartScale;
        Vector3 over = baseScale * PopOvershoot;

        for (float t = 0; t < 1f; t += Time.fixedDeltaTime / PopDuration)
        {
            float e = PopCurve.Evaluate(t);
            float uni = Mathf.LerpUnclamped(PopStartScale, PopOvershoot, e);

            
            float squashY = Mathf.Lerp(uni, 1f / uni, SquashIntensity);
            Vector3 scl = new Vector3(baseScale.x * uni, baseScale.y * squashY, baseScale.z * uni);
            transform.localScale = scl;

            rb.MovePosition(Vector3.LerpUnclamped(posA, posB, e));
            yield return new WaitForFixedUpdate();
        }

        
        Vector3 fromS = transform.localScale;
        Vector3 fromP = rb.position;
        for (float tt = 0; tt < 1f; tt += Time.fixedDeltaTime / PopSettleTail)
        {
            transform.localScale = Vector3.LerpUnclamped(fromS, baseScale, tt);
            rb.MovePosition(Vector3.LerpUnclamped(fromP, startPos, tt));
            yield return new WaitForFixedUpdate();
        }

        transform.localScale = baseScale;
        SnapPos(startPos);
        popCo = null;
    }

    
    void SnapPos(Vector3 p)
    {
        rb.MovePosition(p); rb.position = p; transform.position = p;
    }
}