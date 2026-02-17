using UnityEngine;

/// <summary>
/// Moves a sprite in a direction with linear deceleration until speed reaches zero.
/// When the droplet stops, spawns a stain at the exact landing position.
/// </summary>
public class FlyingBloodDrop : MonoBehaviour
{
    private Vector2 velocity;
    private float deceleration;
    private System.Action<Vector2> onLand;
    private const float StopThreshold = 0.05f;

    /// <param name="deceleration">Speed loss per second (units/sec²) – higher = shorter flight</param>
    /// <param name="onLandCallback">Called when droplet stops (speed ≈ 0), with exact landing position for stain</param>
    public void Begin(Vector2 vel, float deceleration, System.Action<Vector2> onLandCallback = null)
    {
        velocity = vel;
        this.deceleration = Mathf.Max(0.01f, deceleration);
        onLand = onLandCallback;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        float speed = velocity.magnitude;

        if (speed <= StopThreshold)
        {
            onLand?.Invoke(transform.position);
            Destroy(gameObject);
            return;
        }

        transform.position += (Vector3)(velocity * dt);

        float newSpeed = Mathf.Max(0f, speed - deceleration * dt);
        velocity = velocity.normalized * newSpeed;
    }
}
