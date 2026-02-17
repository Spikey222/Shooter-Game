using UnityEngine;

/// <summary>
/// Lightweight component that fakes blood falling onto the floor using position lerp and optional scale animation.
/// No Rigidbody2D or real physics—used for passive bleed drops and slash stain sprites.
/// </summary>
public class BloodDropFallAnimator : MonoBehaviour
{
    public enum ScaleMode
    {
        None,
        DripForm,   // Scale 0.3 -> 1 as it falls (drop forms)
        SplatOnLand, // Scale 0 -> 1 over last fraction when reaching floor (splat)
        ScaleFromTo  // Scale from custom start to end (droplet -> stain)
    }

    [Header("Fall Animation")]
    [Tooltip("Time to reach floor (seconds)")]
    [Range(0.1f, 1.5f)]
    public float fallDuration = 0.35f;

    [Tooltip("Y offset below spawn for floor (e.g. -0.2). Target Y = start Y + this.")]
    [Range(-1f, 0f)]
    public float floorOffset = -0.25f;

    [Header("Scale")]
    [Tooltip("How scale animates during fall")]
    public ScaleMode scaleMode = ScaleMode.SplatOnLand;

    [Tooltip("If SplatOnLand, scale-up time at end of fall (seconds)")]
    [Range(0.02f, 0.2f)]
    public float splatDuration = 0.08f;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float elapsed;
    private float startScale;
    private float scaleFromValue;
    private float scaleToValue;
    private bool hasBegun;

    /// <summary>
    /// Initialize and begin the fall animation. Call immediately after adding component.
    /// </summary>
    /// <param name="startWorldPos">World position to start from (e.g. limb or hit point)</param>
    /// <param name="targetWorldPos">Optional. If null, target = start + (0, floorOffset, 0)</param>
    public void Begin(Vector2 startWorldPos, Vector2? targetWorldPos = null)
    {
        BeginInternal(startWorldPos, targetWorldPos, null, null);
    }

    /// <summary>
    /// Begin with explicit scale range (e.g. droplet size -> stain size).
    /// </summary>
    public void Begin(Vector2 startWorldPos, Vector2? targetWorldPos, float scaleFrom, float scaleTo)
    {
        BeginInternal(startWorldPos, targetWorldPos, scaleFrom, scaleTo);
    }

    private void BeginInternal(Vector2 startWorldPos, Vector2? targetWorldPos, float? scaleFrom, float? scaleTo)
    {
        startPosition = new Vector3(startWorldPos.x, startWorldPos.y, 0f);
        targetPosition = targetWorldPos.HasValue
            ? new Vector3(targetWorldPos.Value.x, targetWorldPos.Value.y, 0f)
            : startPosition + new Vector3(0f, floorOffset, 0f);

        transform.position = startPosition;
        elapsed = 0f;
        hasBegun = true;

        if (scaleFrom.HasValue && scaleTo.HasValue)
        {
            scaleMode = ScaleMode.ScaleFromTo;
            scaleFromValue = scaleFrom.Value;
            scaleToValue = scaleTo.Value;
            transform.localScale = new Vector3(scaleFromValue, scaleFromValue, 1f);
        }
        else
        {
            startScale = transform.localScale.x;
            if (scaleMode == ScaleMode.DripForm)
                transform.localScale = new Vector3(startScale * 0.3f, startScale * 0.3f, 1f);
            else if (scaleMode == ScaleMode.SplatOnLand)
                transform.localScale = Vector3.zero;
        }
    }

    private void Update()
    {
        if (!hasBegun) return;
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / fallDuration);

        // Position lerp
        transform.position = Vector3.Lerp(startPosition, targetPosition, t);

        // Scale animation
        if (scaleMode == ScaleMode.DripForm)
        {
            float scaleT = Mathf.Lerp(0.3f, 1f, t);
            float s = startScale * scaleT;
            transform.localScale = new Vector3(s, s, 1f);
        }
        else if (scaleMode == ScaleMode.SplatOnLand)
        {
            float splatStart = 1f - (splatDuration / fallDuration);
            float scaleT = t <= splatStart ? 0f : (t - splatStart) / (1f - splatStart);
            float smoothT = Mathf.SmoothStep(0f, 1f, scaleT);
            float s = startScale * smoothT;
            transform.localScale = new Vector3(s, s, 1f);
        }
        else if (scaleMode == ScaleMode.ScaleFromTo)
        {
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            float s = Mathf.Lerp(scaleFromValue, scaleToValue, smoothT);
            transform.localScale = new Vector3(s, s, 1f);
        }

        if (elapsed >= fallDuration)
            Destroy(this);
    }
}
