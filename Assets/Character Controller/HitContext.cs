using UnityEngine;

/// <summary>
/// Optional context when applying damage from a hit (e.g. melee contact).
/// Used to drive blood spray: position and direction come from the attacker's perspective.
/// </summary>
public struct HitContext
{
    /// <summary>World position of the hit (e.g. contact point on victim). Blood sprays from here.</summary>
    public Vector2 worldPosition;

    /// <summary>Normalized direction from attacker toward victim. Blood spray direction is the opposite.</summary>
    public Vector2 attackDirection;

    /// <summary>True for critical hits (e.g. head/neck); can increase spray intensity.</summary>
    public bool isCritical;

    /// <summary>Transform of the collider that was hit (for attaching stick blood to the exact part that made contact).</summary>
    public Transform contactTransform;

    public bool HasValidDirection => attackDirection.sqrMagnitude > 0.01f;
}
