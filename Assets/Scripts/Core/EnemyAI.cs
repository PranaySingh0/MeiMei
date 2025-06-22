using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
public sealed class EnemyAI : MonoBehaviour
{
    
    [Header("Patrol Points (optional)")]
    [Tooltip("Ordered waypoints for patrol. Leave empty for random roaming.")]
    [SerializeField] Transform[] patrolPoints;

    [Header("Patrol Zone(s)")]
    [Tooltip("One or more trigger colliders that together define the allowed area.")]
    [SerializeField] Collider[] zones;
    [Tooltip("If a straight line between way-points exits the zone, "
           + "insert a corner hop that hugs the bounds instead of skipping.")]
    public bool allowCornering = true;

    [Header("Vision")]
    [Range(1, 180)] public float fovAngle = 60f;
    public float viewRange = 8f;
    public LayerMask losBlockers = ~0;

    [Header("Speeds")]
    public float walkSpeed  = 2f;
    public float chaseSpeed = 3.5f;
    public float accelTime  = .4f;
    public float decelTime  = .5f;
    public float turnSpeed  = 720f;

    [Header("Path Shape")]
    [Range(0, 3)] public float curveOffset = .8f;
    public float arriveDist = .3f;

    [Header("Idle Durations (seconds)")]
    public Vector2 breathRange = new(1f, 1.5f);
    public Vector2 lookRange   = new(2f, 3f);

    [Header("Debug")]
    public UnityEvent<string> OnStateChanged;

    
    CharacterController cc;
    Transform player;
    readonly System.Random rng = new();

    int     wpIdx = -1;
    Vector3 p0, p1, p2;
    float   pathLen, param;
    Vector3 prevPos;

    Vector3 lagTarget;
    float   loseTimer;

    readonly Queue<Vector3> cornerQ = new();

    
    abstract class State { public virtual void Enter() { } public virtual void Tick(float dt) { } }
    State idle, patrolMove, chase, returnMove, cur;

    
    class IdleState : State
    {
        EnemyAI o; float t; bool look;
        public IdleState(EnemyAI o) { this.o = o; }
        public override void Enter()
        {
            look = Random.value < .35f;
            Vector2 r = look ? o.lookRange : o.breathRange;
            t = Random.Range(r.x, r.y);
            o.Signal(look ? "Idle-Look" : "Idle-Breath");
        }
        public override void Tick(float dt)
        {
            if (o.CanSeePlayer()) { o.Switch(o.chase); return; }
            if ((t -= dt) <= 0f)
            {
                if (!o.BuildNextPatrolPath()) { t = .8f; return; }
                o.Switch(o.patrolMove);
            }
        }
    }

    
    class PatrolMoveState : State
    {
        EnemyAI o; float v, acc, dec;
        public PatrolMoveState(EnemyAI o) { this.o = o; }
        public override void Enter()
        {
            o.Signal("Patrol");
            o.ResetPathParam();
            v = 0;
            acc = o.walkSpeed / o.accelTime;
            dec = o.walkSpeed / o.decelTime;
        }
        public override void Tick(float dt)
        {
            if (o.CanSeePlayer()) { o.Switch(o.chase); return; }
            o.FollowCurrentPath(dt, ref v, acc, dec, o.walkSpeed, o.idle);
        }
    }

    
    class ChaseState : State
    {
        EnemyAI o; float v, acc, dec;
        public ChaseState(EnemyAI o) { this.o = o; }
        public override void Enter()
        {
            o.Signal("Chase");
            v = 0;
            acc = o.chaseSpeed / o.accelTime;
            dec = o.chaseSpeed / o.decelTime;
            o.lagTarget = o.player.position;
            o.loseTimer = 0;
        }
        public override void Tick(float dt)
        {
            bool see = o.CanSeePlayer();
            o.loseTimer = see ? 0 : o.loseTimer + dt;
            bool giveUp = o.loseTimer > 3f || !o.IsInside(o.player.position);

            
            o.lagTarget = Vector3.Lerp(o.lagTarget, o.player.position, 0.6f * dt);
            Vector3 dest = o.ClampToZone(o.lagTarget.WithY(o.transform.position.y));

            
            o.p0 = o.transform.position; o.p1 = (o.p0 + dest) * .5f; o.p2 = dest;
            o.pathLen = Vector3.Distance(o.p0, o.p2);
            o.param += (v * dt) / Mathf.Max(.01f, o.pathLen);

            
            float stopD = v * v / (2 * dec);
            v = (stopD >= o.pathLen) ? Mathf.Max(0, v - dec * dt)
                                     : Mathf.Min(o.chaseSpeed, v + acc * dt);

            
            Vector3 next = Vector3.Lerp(o.p0, o.p2, o.param);
            o.cc.Move(next - o.prevPos); o.prevPos = next;

            Vector3 fwd = (dest - next).XZ();
            if (fwd.sqrMagnitude > 1e-4f)
            {
                Quaternion look = Quaternion.LookRotation(fwd);
                o.transform.rotation = Quaternion.RotateTowards(o.transform.rotation, look, o.turnSpeed * dt);
            }

            if (o.param >= 1f) o.param = 0f;

            if (giveUp) o.Switch(o.returnMove);
        }
    }

    
    class ReturnState : State
    {
        EnemyAI o; float wait = 3f; float v, acc, dec;
        public ReturnState(EnemyAI o) { this.o = o; }
        public override void Enter()
        {
            o.Signal("Return");
            wait = 3f; v = 0;
            acc = o.walkSpeed / o.accelTime;
            dec = o.walkSpeed / o.decelTime;
            o.GoToNearestWaypoint();
        }
        public override void Tick(float dt)
        {
            if ((wait -= dt) > 0f) return;
            if (o.CanSeePlayer()) { o.Switch(o.chase); return; }

            bool done = o.FollowCurrentPath(dt, ref v, acc, dec, o.walkSpeed, o.idle);
            if (done) o.Switch(o.idle);
        }
    }

    
    void Awake()
    {
        cc = GetComponent<CharacterController>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        ExpandRootZones();

        idle        = new IdleState(this);
        patrolMove  = new PatrolMoveState(this);
        chase       = new ChaseState(this);
        returnMove  = new ReturnState(this);
    }
    void Start()  => Switch(idle);
    void Update() => cur?.Tick(Time.deltaTime);

    
    void ResetPathParam() { param = 0; prevPos = transform.position; }

    bool FollowCurrentPath(float dt, ref float v, float acc, float dec,
                           float maxV, State whenFinished)
    {
        float remain  = pathLen * (1 - param);
        float stopDst = v * v / (2 * dec);
        v = (stopDst >= remain) ? Mathf.Max(0, v - dec * dt)
                                : Mathf.Min(maxV, v + acc * dt);

        param += (v * dt) / Mathf.Max(.01f, pathLen);
        param  = Mathf.Clamp01(param);

        Vector3 next = Bezier(p0, p1, p2, param);
        next = ClampToZone(next);
        cc.Move(next - prevPos); prevPos = next;

        Vector3 fwd = (Bezier(p0, p1, p2, Mathf.Min(param + .02f, 1f)) - next).XZ();
        if (fwd.sqrMagnitude > 1e-4f)
        {
            Quaternion look = Quaternion.LookRotation(fwd);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
        }

        if (param >= 1f || (next - p2).sqrMagnitude < arriveDist * arriveDist)
        { Switch(whenFinished); return true; }
        return false;
    }

    
    bool CanSeePlayer()
    {
        if (!player) return false;
        Vector3 to = player.position - transform.position;
        if (to.sqrMagnitude > viewRange * viewRange) return false;
        if (Vector3.Angle(transform.forward, to) > fovAngle * 0.5f) return false;

        if (Physics.Raycast(transform.position + Vector3.up * .1f, to.normalized,
                            out var hit, viewRange, losBlockers))
        {
            if (!hit.transform.CompareTag("Player")) return false;
        }
        return true;
    }

    
    bool BuildNextPatrolPath()
    {
        if (cornerQ.Count > 0) { MakeBezier(cornerQ.Dequeue()); return true; }

        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                wpIdx = (wpIdx + 1) % patrolPoints.Length;
                Vector3 dest = patrolPoints[wpIdx].position.WithY(transform.position.y);
                if (TryPathTo(dest)) return true;
            }
        }

        for (int t = 0; t < 30; t++)
            if (TryPathTo(RandomInside())) return true;
        return false;
    }

    void GoToNearestWaypoint()
    {
        if (patrolPoints is { Length: > 0 })
        {
            float best = float.MaxValue; int bestIdx = 0;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                float d = (patrolPoints[i].position - transform.position).sqrMagnitude;
                if (d < best) { best = d; bestIdx = i; }
            }
            wpIdx = bestIdx - 1;
            BuildNextPatrolPath();
            ResetPathParam();
        }
    }

    bool TryPathTo(Vector3 dest)
    {
        if (allowCornering && !StraightInside(transform.position, dest))
        {
            Vector3 mid = ClampToZone(dest);
            if (!StraightInside(transform.position, mid) ||
                !StraightInside(mid, dest)) return false;
            cornerQ.Enqueue(dest); dest = mid;
        }
        return MakeBezier(dest);
    }

    bool MakeBezier(Vector3 dest)
    {
        p0 = transform.position; p2 = ClampToZone(dest);
        Vector3 mid = (p0 + p2) * .5f;
        Vector3 dir = (p2 - p0).normalized;
        Vector3 perp = new(dir.z, 0, -dir.x);
        p1 = mid + perp * curveOffset * (rng.NextDouble() > .5 ? 1 : -1);

        if (!BezierInside(p0, p1, p2)) p1 = mid;
        pathLen = ArcLength(p0, p1, p2);
        return pathLen > .1f;
    }

    
    static Vector3 Bezier(in Vector3 a, in Vector3 b, in Vector3 c, float t)
    { float u = 1 - t; return u * u * a + 2 * u * t * b + t * t * c; }

    static float ArcLength(in Vector3 a, in Vector3 b, in Vector3 c)
    {
        float L = 0; Vector3 prev = a;
        for (int i = 1; i <= 10; i++)
        {
            float t = i / 10f; Vector3 p = Bezier(a, b, c, t);
            L += Vector3.Distance(prev, p); prev = p;
        }
        return L;
    }

    bool StraightInside(Vector3 a, Vector3 b)
    {
        for (int i = 0; i <= 10; i++)
            if (!IsInside(Vector3.Lerp(a, b, i / 10f))) return false;
        return true;
    }
    bool BezierInside(in Vector3 a, in Vector3 b, in Vector3 c)
    {
        for (int i = 1; i <= 10; i++)
            if (!IsInside(Bezier(a, b, c, i / 10f))) return false;
        return true;
    }

    bool IsInside(Vector3 p)
    {
        if (zones.Length == 0) return true;
        foreach (var z in zones)
        {
            Bounds b = z.bounds;
            if (p.x > b.min.x && p.x < b.max.x &&
                p.z > b.min.z && p.z < b.max.z) return true;
        }
        return false;
    }
    Vector3 ClampToZone(Vector3 p)
    {
        if (zones.Length == 0) return p;
        foreach (var z in zones)
        {
            Bounds b = z.bounds;
            p.x = Mathf.Clamp(p.x, b.min.x + .05f, b.max.x - .05f);
            p.z = Mathf.Clamp(p.z, b.min.z + .05f, b.max.z - .05f);
        }
        return p;
    }
    Vector3 RandomInside()
    {
        if (zones.Length == 0) return transform.position;
        for (int t = 0; t < 30; t++)
        {
            var z = zones[rng.Next(zones.Length)];
            Bounds b = z.bounds;
            Vector3 p = new(Random.Range(b.min.x, b.max.x),
                            transform.position.y,
                            Random.Range(b.min.z, b.max.z));
            if (IsInside(p)) return p;
        }
        return zones[0].bounds.center.WithY(transform.position.y);
    }
    void ExpandRootZones()
    {
        var list = new List<Collider>();
        foreach (var c in zones) if (c) list.AddRange(c.GetComponentsInChildren<Collider>());
        zones = list.ToArray();
    }

    
    void Switch(State s) { cur = s; s.Enter(); }
    void Signal(string msg) => OnStateChanged?.Invoke(msg);
}


static class VecExt
{
    public static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }
    public static Vector3 XZ(this Vector3 v) { v.y = 0; return v; }
}