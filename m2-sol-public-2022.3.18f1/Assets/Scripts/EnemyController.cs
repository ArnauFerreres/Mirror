using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.MemoryProfiler;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;


public class EnemyController : NetworkBehaviour
{
    public Transform[] patrolPoints; // Puntos de patrulla
    public float patrolSpeed = 2f; // Velocidad de patrulla
    public float chaseSpeed = 5f; // Velocidad de persecución
    public float detectionRange = 10f; // Rango de detección del jugador
    public float detectionAngle = 60f; // Ángulo de visión del jugador (en grados)
    public GameObject bulletPrefab; // Prefab de la bala
    public Transform firePoint; // Punto de disparo
    public float fireRate = 1f;
    public float attackRange = 3f;
    private NavMeshAgent agent;
    private int currentPatrolIndex = 0;
    [SyncVar]
    private List<Transform> players = new List<Transform>();
    [SyncVar]
    public bool playerDetected = false;
    private float nextFireTime = 0f;

    public bool turretMode = false;

    public GameObject attackMelee;

    private enum State
    {
        Patrolling,
        Chasing,
        Attacking,
        Shooting
    }
    [SyncVar]
    private State currentState;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        currentState = State.Patrolling;
        SetDestinationToNextPatrolPoint();

        if (isServer)
        {
            StartCoroutine(FindPlayers());
        }
    }

    private void Update()
    {
        if (isServer)
        {
            if (players.Count == 0)
            {
                return;
            }

            switch (currentState)
            {
                case State.Patrolling:
                    Patrol();
                    break;
                case State.Chasing:
                    ChasePlayer();
                    break;
                case State.Attacking:
                    AttackPlayer();
                    break;
                case State.Shooting:
                    Shoot();
                    break;
            }
        }
    }

    [Server]
    private IEnumerator FindPlayers()
    {
        while (players.Count == 0)
        {
            foreach (var identity in NetworkServer.spawned.Values)
            {
                if (identity != null && identity.GetComponent<TankController>() != null)
                {
                    players.Add(identity.transform);
                    Debug.Log($"Player found: {identity.gameObject.name}");
                }
            }

            if (players.Count == 0)
            {
                Debug.LogWarning("Players not found, retrying...");
            }

            yield return new WaitForSeconds(1f); // retry every second
        }
    }

    [Server]
    private void DetectPlayers()
    {
        foreach (var player in players)
        {
            if (player == null) continue;

            Vector3 dirToPlayer = player.position - transform.position;
            float angleToPlayer = Vector3.Angle(transform.forward, dirToPlayer);

            if (angleToPlayer < detectionAngle / 2f && dirToPlayer.magnitude < detectionRange)
            {
                if (turretMode)
                {
                    currentState = State.Shooting;
                }
                else
                {
                    currentState = State.Chasing;
                }
                playerDetected = true;
                RpcUpdateState(currentState, playerDetected);
                break;
            }
        }
    }

    [ClientRpc]
    private void RpcUpdateState(State newState, bool detected)
    {
        currentState = newState;
        playerDetected = detected;
    }

    private void Patrol()
    {
        agent.speed = patrolSpeed;
        if (agent.remainingDistance < 0.5f)
        {
            SetDestinationToNextPatrolPoint();
        }

        DetectPlayers();
    }

    private void ChasePlayer()
    {
        agent.speed = chaseSpeed;
        Transform closestPlayer = GetClosestPlayer();

        if (closestPlayer != null)
        {
            agent.SetDestination(closestPlayer.position);

            if (Vector3.Distance(transform.position, closestPlayer.position) < attackRange)
            {
                currentState = State.Attacking;
            }
            else if (Vector3.Distance(transform.position, closestPlayer.position) > detectionRange)
            {
                currentState = State.Patrolling;
                playerDetected = false;
            }
        }
    }

    private Transform GetClosestPlayer()
    {
        Transform closestPlayer = null;
        float closestDistance = Mathf.Infinity;

        foreach (var player in players)
        {
            if (player == null) continue;

            float distance = Vector3.Distance(transform.position, player.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPlayer = player;
            }
        }

        return closestPlayer;
    }

    private void Shoot()
    {
        agent.speed = 0f;
        Transform closestPlayer = GetClosestPlayer();

        if (closestPlayer != null)
        {
            Vector3 dirToPlayer = closestPlayer.position - transform.position;
            transform.rotation = Quaternion.LookRotation(dirToPlayer);

            if (playerDetected && Time.time >= nextFireTime)
            {
                ShootAtPlayer();
                nextFireTime = Time.time + 1f / fireRate;
            }

            if (Vector3.Distance(transform.position, closestPlayer.position) > detectionRange)
            {
                currentState = State.Patrolling;
                playerDetected = false;
            }
        }
    }

    private void AttackPlayer()
    {
        Transform closestPlayer = GetClosestPlayer();

        if (closestPlayer != null)
        {
            Vector3 dirToPlayer = closestPlayer.position - transform.position;
            transform.rotation = Quaternion.LookRotation(dirToPlayer);

            if (playerDetected && Time.time >= nextFireTime)
            {
                Instantiate(attackMelee, firePoint.position, firePoint.rotation);
                nextFireTime = Time.time + 1f / fireRate;
            }

            if (Vector3.Distance(transform.position, closestPlayer.position) > attackRange)
            {
                currentState = State.Chasing;
            }
        }
    }

    [Server]
    private void ShootAtPlayer()
    {
        var bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        bullet.GetComponent<Rigidbody>().velocity = bullet.transform.forward * 6;
        NetworkServer.Spawn(bullet);
        Destroy(bullet, 2.0f);
    }

    private void SetDestinationToNextPatrolPoint()
    {
        agent.speed = patrolSpeed;
        agent.SetDestination(patrolPoints[currentPatrolIndex].position);
        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.blue;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawFrustum(Vector3.zero, detectionAngle, detectionRange, 0f, 1f);
    }
}