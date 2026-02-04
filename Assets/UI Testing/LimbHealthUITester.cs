using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(ProceduralCharacterController))]
public class LimbHealthUITester : MonoBehaviour
{
    [Header("UI Testing")]
    [Tooltip("Enable UI testing mode")]
    public bool enableUITesting = false;
    
    [Range(0, 100)]
    [Tooltip("Test health value for head")]
    public float headHealth = 100f;
    
    [Range(0, 100)]
    [Tooltip("Test health value for neck")]
    public float neckHealth = 100f;
    
    [Range(0, 100)]
    [Tooltip("Test health value for torso")]
    public float torsoHealth = 100f;
    
    [Range(0, 100)]
    [Tooltip("Test health value for right arm")]
    public float rightArmHealth = 100f;
    
    [Range(0, 100)]
    [Tooltip("Test health value for right forearm")]
    public float rightForearmHealth = 100f;
    
    [Range(0, 100)]
    [Tooltip("Test health value for right hand")]
    public float rightHandHealth = 100f;
    
    [Range(0, 100)]
    [Tooltip("Test health value for left arm")]
    public float leftArmHealth = 100f;
    
    [Range(0, 100)]
    [Tooltip("Test health value for left forearm")]
    public float leftForearmHealth = 100f;
    
    [Range(0, 100)]
    [Tooltip("Test health value for left hand")]
    public float leftHandHealth = 100f;
    
    // Reference to the character controller
    private ProceduralCharacterController characterController;
    
    // Dictionary to store limb references
    private Dictionary<ProceduralCharacterController.LimbType, ProceduralLimb> limbMap;
    
    private void Awake()
    {
        // Get the character controller component
        characterController = GetComponent<ProceduralCharacterController>();
    }
    
    private void Start()
    {
        // Wait for the character controller to initialize its limbs
        Invoke("InitializeLimbMap", 0.1f);
    }
    
    private void InitializeLimbMap()
    {
        // Get the limb map from the character controller using reflection
        var limbMapField = typeof(ProceduralCharacterController).GetField("limbMap", 
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            
        if (limbMapField != null)
        {
            limbMap = limbMapField.GetValue(characterController) as Dictionary<ProceduralCharacterController.LimbType, ProceduralLimb>;
            Debug.Log("UI Testing: Successfully accessed limbMap from ProceduralCharacterController");
        }
        else
        {
            Debug.LogError("UI Testing: Failed to access limbMap from ProceduralCharacterController");
        }
    }
    
    private void Update()
    {
        // Apply test health values if UI testing is enabled
        if (enableUITesting && limbMap != null)
        {
            ApplyTestHealthValues();
        }
    }
    
    // Apply test health values to limbs for UI testing
    private void ApplyTestHealthValues()
    {
        // Log that UI testing is active
        Debug.Log("UI Testing: Applying health values to limbs from inspector sliders");
        
        // Apply health values to each limb based on slider values
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.Head, out ProceduralLimb headLimb))
        {
            headLimb.SetHealth(headHealth);
            Debug.Log($"UI Testing: Head health set to {headHealth} (Current: {headLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Head limb not found in limbMap!");
        }
        
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.Neck, out ProceduralLimb neckLimb))
        {
            neckLimb.SetHealth(neckHealth);
            Debug.Log($"UI Testing: Neck health set to {neckHealth} (Current: {neckLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Neck limb not found in limbMap!");
        }
        
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.Torso, out ProceduralLimb torsoLimb))
        {
            torsoLimb.SetHealth(torsoHealth);
            Debug.Log($"UI Testing: Torso health set to {torsoHealth} (Current: {torsoLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Torso limb not found in limbMap!");
        }
        
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.RightBicep, out ProceduralLimb rightArmLimb))
        {
            rightArmLimb.SetHealth(rightArmHealth);
            Debug.Log($"UI Testing: Right Bicep health set to {rightArmHealth} (Current: {rightArmLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Right Bicep limb not found in limbMap!");
        }
        
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.RightForearm, out ProceduralLimb rightForearmLimb))
        {
            rightForearmLimb.SetHealth(rightForearmHealth);
            Debug.Log($"UI Testing: Right Forearm health set to {rightForearmHealth} (Current: {rightForearmLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Right Forearm limb not found in limbMap!");
        }
        
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.RightHand, out ProceduralLimb rightHandLimb))
        {
            rightHandLimb.SetHealth(rightHandHealth);
            Debug.Log($"UI Testing: Right Hand health set to {rightHandHealth} (Current: {rightHandLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Right Hand limb not found in limbMap!");
        }
        
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.LeftBicep, out ProceduralLimb leftArmLimb))
        {
            leftArmLimb.SetHealth(leftArmHealth);
            Debug.Log($"UI Testing: Left Bicep health set to {leftArmHealth} (Current: {leftArmLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Left Bicep limb not found in limbMap!");
        }
        
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.LeftForearm, out ProceduralLimb leftForearmLimb))
        {
            leftForearmLimb.SetHealth(leftForearmHealth);
            Debug.Log($"UI Testing: Left Forearm health set to {leftForearmHealth} (Current: {leftForearmLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Left Forearm limb not found in limbMap!");
        }
        
        if (limbMap.TryGetValue(ProceduralCharacterController.LimbType.LeftHand, out ProceduralLimb leftHandLimb))
        {
            leftHandLimb.SetHealth(leftHandHealth);
            Debug.Log($"UI Testing: Left Hand health set to {leftHandHealth} (Current: {leftHandLimb.GetCurrentHealth()})");
        }
        else
        {
            Debug.LogWarning("UI Testing: Left Hand limb not found in limbMap!");
        }
    }
}
