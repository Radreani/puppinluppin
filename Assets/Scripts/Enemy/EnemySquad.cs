using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a group of <see cref="EnemySoldier"/> units and keeps optional formation cohesion.
/// </summary>
public class EnemySquad : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] EnemySoldier soldierPrefab;
    [SerializeField, Min(1)] int unitCount = 4;

    [Header("Spawn")]
    [SerializeField] Vector2 spawnArea = new Vector2(3f, 3f);
    [SerializeField] bool spawnOnStart = true;

    [Header("Formation")]
    [Tooltip("0 = soldiers ignore formation; 1 = full pull toward squad slots.")]
    [SerializeField, Range(0f, 1f)] float formationInfluence = 0.55f;
    [SerializeField, Min(0f)] float formationRadius = 2.2f;
    [SerializeField, Min(0.05f)] float formationArrivalSlack = 0.75f;
    [Tooltip("When true, FormationInfluence is treated as zero for all members.")]
    [SerializeField] bool scattered;

    readonly List<EnemySoldier> _members = new List<EnemySoldier>();

    public float FormationInfluence => scattered ? 0f : formationInfluence;
    public float FormationArrivalSlack => formationArrivalSlack;

    public Vector3 FormationAnchor
    {
        get
        {
            int n = 0;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _members.Count; i++)
            {
                var m = _members[i];
                if (m == null || !m.IsAlive)
                    continue;
                sum += m.transform.position;
                n++;
            }

            return n > 0 ? sum / n : transform.position;
        }
    }

    void Start()
    {
        if (spawnOnStart && soldierPrefab != null)
            SpawnSquad();
    }

    public Quaternion GetFormationRotation() => Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

    /// <summary>Local XZ offset for slot index around a circle.</summary>
    public static Vector3 SlotOffsetCircle(int index, int total, float radius)
    {
        total = Mathf.Max(1, total);
        float ang = (Mathf.PI * 2f / total) * index;
        return new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
    }

    public void SpawnSquad()
    {
        ClearDeadRefs();
        if (soldierPrefab == null)
            return;

        for (int i = _members.Count; i < unitCount; i++)
        {
            Vector3 jitter = new Vector3(
                (Random.value - 0.5f) * spawnArea.x,
                0f,
                (Random.value - 0.5f) * spawnArea.y);
            Vector3 pos = transform.position + jitter;
            var inst = Instantiate(soldierPrefab, pos, transform.rotation);
            var es = inst.GetComponent<EnemySoldier>();
            if (es != null)
            {
                Vector3 local = SlotOffsetCircle(i, unitCount, formationRadius);
                es.BindSquad(this, local);
                _members.Add(es);
            }
        }
    }

    public void SetScattered(bool value)
    {
        scattered = value;
    }

    public void NotifyMemberDied(EnemySoldier s)
    {
        _members.Remove(s);
    }

    void ClearDeadRefs()
    {
        for (int i = _members.Count - 1; i >= 0; i--)
        {
            if (_members[i] == null)
                _members.RemoveAt(i);
        }
    }
}
