using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to blood instances. Allows nearby blood spawns to merge into this pool:
/// the dominant sprite stays and scales up as more blood is added.
/// </summary>
public class BloodPool : MonoBehaviour
{
    private static readonly List<BloodPool> AllPools = new List<BloodPool>();

    [Tooltip("Maximum scale (uniform X/Y) to prevent pools growing infinitely")]
    public float maxScale = 10f;

    [Tooltip("Minimum scale; pool will not shrink below this when merging logic adjusts")]
    public float minScale = 0.5f;

    private float _currentScale;

    /// <summary>
    /// Find the nearest blood pool within the given radius, or null if none.
    /// </summary>
    public static BloodPool FindNearestPool(Vector2 worldPosition, float baseRadius)
    {
        BloodPool nearest = null;
        float closestSq = float.MaxValue;

        for (int i = AllPools.Count - 1; i >= 0; i--)
        {
            BloodPool p = AllPools[i];
            if (p == null)
            {
                AllPools.RemoveAt(i);
                continue;
            }

            // Effective merge radius grows with pool size so big pools keep attracting new drops.
            float effectiveRadius = baseRadius * Mathf.Max(1f, p._currentScale);
            float maxSq = effectiveRadius * effectiveRadius;

            float sqDist = ((Vector2)p.transform.position - worldPosition).sqrMagnitude;
            if (sqDist <= maxSq && sqDist < closestSq)
            {
                closestSq = sqDist;
                nearest = p;
            }
        }

        return nearest;
    }

    private void OnEnable()
    {
        if (!AllPools.Contains(this))
            AllPools.Add(this);
    }

    private void OnDisable()
    {
        AllPools.Remove(this);
    }

    private void Awake()
    {
        float s = transform.localScale.x;
        _currentScale = s;
    }

    /// <summary>
    /// Add blood to this pool. Expands the sprite by the given scale amount (capped by maxScale).
    /// </summary>
    /// <param name="additionalScale">Amount to add to current scale (e.g. the scale a new drop would have had).</param>
    public void AddBlood(float additionalScale)
    {
        _currentScale = Mathf.Clamp(_currentScale + additionalScale, minScale, maxScale);
        transform.localScale = new Vector3(_currentScale, _currentScale, 1f);
    }

    /// <summary>
    /// Merge another pool into this one: add its effective scale and optionally destroy the other.
    /// </summary>
    public void MergeIn(BloodPool other)
    {
        if (other == null || other == this)
            return;
        float otherScale = other.GetCurrentScale();
        AddBlood(otherScale);
        if (other.gameObject != null)
            Destroy(other.gameObject);
    }

    public float GetCurrentScale()
    {
        return _currentScale;
    }

    public void SetScale(float scale)
    {
        _currentScale = Mathf.Clamp(scale, minScale, maxScale);
        transform.localScale = new Vector3(_currentScale, _currentScale, 1f);
    }
}
