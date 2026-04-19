using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Place on the same GameObject as your Blender empty (<c>buildingRoot</c>). Child chunks use
/// <see cref="BuildingDestructibleChunk"/>; when one breaks, chunks above that overlap in XZ receive
/// structural damage so upper floors do not stay floating indefinitely.
/// </summary>
[DisallowMultipleComponent]
public class BuildingDestructibleRoot : MonoBehaviour
{
    [Header("Structural cascade")]
    [SerializeField, Min(0f)] float structuralDamageWhenSupportBreaks = 35f;
    [Tooltip("World-space slack: chunk bottoms slightly below the broken piece top still count as \"above\".")]
    [SerializeField, Min(0f)] float verticalSlop = 0.08f;
    [SerializeField, Min(0f)] float horizontalPadding;

    readonly List<BuildingDestructibleChunk> _chunks = new List<BuildingDestructibleChunk>(64);
    readonly Queue<Bounds> _supportLossRegions = new Queue<Bounds>(16);
    bool _drainingSupport;

    void Awake()
    {
        RebuildChunkList();
    }

    /// <summary>Call after instantiating or swapping chunk children at runtime.</summary>
    public void RebuildChunkList()
    {
        _chunks.Clear();
        GetComponentsInChildren(true, _chunks);
    }

    /// <summary>Invoked by a chunk right before it detaches; <paramref name="brokenWorldBounds"/> must be intact-space bounds.</summary>
    internal void NotifyChunkShattered(BuildingDestructibleChunk broken, Bounds brokenWorldBounds)
    {
        _chunks.Remove(broken);
        if (structuralDamageWhenSupportBreaks <= 0f)
            return;

        if (horizontalPadding > 0f)
        {
            var e = brokenWorldBounds.extents;
            e.x += horizontalPadding;
            e.z += horizontalPadding;
            brokenWorldBounds.extents = e;
        }

        _supportLossRegions.Enqueue(brokenWorldBounds);
        if (_drainingSupport)
            return;

        _drainingSupport = true;
        try
        {
            while (_supportLossRegions.Count > 0)
            {
                Bounds region = _supportLossRegions.Dequeue();
                BuildingDestructibleChunk[] snapshot = _chunks.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    BuildingDestructibleChunk c = snapshot[i];
                    if (c == null || !c.IsIntact)
                        continue;
                    if (!OverlapsXZ(c.WorldStructuralBounds, region))
                        continue;
                    if (c.WorldStructuralBounds.min.y < region.max.y - verticalSlop)
                        continue;

                    c.ApplyStructuralStress(structuralDamageWhenSupportBreaks);
                }
            }
        }
        finally
        {
            _drainingSupport = false;
        }
    }

    static bool OverlapsXZ(Bounds a, Bounds b)
    {
        return a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;
    }
}
