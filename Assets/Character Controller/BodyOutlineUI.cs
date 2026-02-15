using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class BodyOutlineUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the character controller")]
    public ProceduralCharacterController characterController;

    [Tooltip("Parent canvas group to control visibility")]
    public CanvasGroup canvasGroup;

    [Tooltip("Canvas component to enable/disable based on attachment")]
    public Canvas canvas;

    [Header("UI Settings")]
    [Tooltip("Color for healthy limbs")]
    public Color healthyColor = Color.green;

    [Tooltip("Color for damaged limbs")]
    public Color damagedColor = Color.yellow;

    [Tooltip("Color for critically damaged limbs")]
    public Color criticalColor = Color.red;

    [Tooltip("Threshold for damaged state (percentage)")]
    [Range(0f, 1f)]
    public float damagedThreshold = 0.8f;

    [Tooltip("Threshold for critical state (percentage)")]
    [Range(0f, 1f)]
    public float criticalThreshold = 0.3f;

    [Tooltip("Fade in speed when activating")]
    public float fadeInSpeed = 5f;

    [Tooltip("Fade out speed when deactivating")]
    public float fadeOutSpeed = 3f;

    [Header("Damage-based visibility")]
    [Tooltip("Body outline fades in when you take damage and fades out after no damage for this many seconds.")]
    public float fadeOutAfterNoDamageSeconds = 3f;

    [Header("On take control")]
    [Tooltip("When you take control of a unit, outline fades in to show condition, then fades out after this many seconds.")]
    public float showOutlineAfterControlSeconds = 3f;

    [Tooltip("Optional: reference to InventoryUI. If set, outline is fully visible (no fade) while inventory is open.")]
    public InventoryUI inventoryUI;

    // Dictionary to map limb types to UI images
    private Dictionary<ProceduralCharacterController.LimbType, Image> limbImages = 
        new Dictionary<ProceduralCharacterController.LimbType, Image>();

    // Target alpha for the canvas group
    private float targetAlpha = 0f;
    
    // Track previous attachment state to avoid unnecessary updates
    private bool wasAttached = false;

    // Damage-based visibility: time when damage was last taken
    private float lastDamageTime = -999f;
    private Dictionary<ProceduralCharacterController.LimbType, float> previousLimbHealth = new Dictionary<ProceduralCharacterController.LimbType, float>();
    private float previousTorsoHealth = -1f;

    // When we took control of current unit (for fade-in then fade-out)
    private float controlTakenTime = -999f;

    // Current smoothed alpha used for fading (so we can apply to all parts even without CanvasGroup)
    private float currentFadeAlpha = 0f;

    private bool wasInventoryOpen;

    // Resolved at runtime: the "Outline" GameObject (found by name, like InventoryUI's panel)
    private GameObject outlineRoot;

    private void Awake()
    {
        // Initialize the limb images dictionary
        InitializeLimbImageMap();

        // Find Canvas component if not assigned
        if (canvas == null)
        {
            canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = GetComponent<Canvas>();
            }
        }
        
        // Try to find character controller if not assigned
        TryFindCharacterController();

        currentFadeAlpha = 0f;
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
        
        // Start with canvas disabled if not attached
        if (canvas != null)
        {
            canvas.enabled = false;
            wasAttached = false;
        }

        // Find "Outline" by name (same idea as InventoryUI: one known element). Disable until we possess a character.
        ResolveOutlineRoot();
        if (outlineRoot != null)
            outlineRoot.SetActive(false);
    }

    private void ResolveOutlineRoot()
    {
        if (outlineRoot != null) return;
        Transform direct = transform.Find("Outline");
        if (direct != null)
        {
            outlineRoot = direct.gameObject;
            return;
        }
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.gameObject.name == "Outline")
            {
                outlineRoot = t.gameObject;
                return;
            }
        }
        if (gameObject.name == "Outline")
            outlineRoot = gameObject;
    }

    private void Start()
    {
        // Try to find character controller again in Start (in case it's created after Awake)
        TryFindCharacterController();
        if (inventoryUI == null)
            inventoryUI = FindFirstObjectByType<InventoryUI>();
        // Subscribe to limb health change events and overall health changes (for Torso)
        if (characterController != null)
        {
            characterController.OnLimbHealthChanged += OnLimbHealthChanged;
            characterController.OnHealthChanged += OnCharacterHealthChanged;
            subscribedController = characterController;
            controlTakenTime = Time.time;
            previousTorsoHealth = characterController.GetTorsoHealth();
            foreach (ProceduralCharacterController.LimbType limbType in System.Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
            {
                ProceduralLimb limb = characterController.GetLimb(limbType);
                if (limb != null)
                    previousLimbHealth[limbType] = limb.GetCurrentHealth();
            }
            UpdateAllLimbUI();
        }
    }
    
    // Try to find the character controller if not assigned
    private void TryFindCharacterController()
    {
        // First, try to find in parent hierarchy
        ProceduralCharacterController foundController = GetComponentInParent<ProceduralCharacterController>();
        
        // If not found, try to find in children
        if (foundController == null)
        {
            foundController = GetComponentInChildren<ProceduralCharacterController>();
        }
        
        // If still not found, try to find in siblings
        if (foundController == null && transform.parent != null)
        {
            foundController = transform.parent.GetComponentInChildren<ProceduralCharacterController>();
        }
        
        // If still not found, try to find the currently controlled character (not in spectator mode)
        if (foundController == null)
        {
            ProceduralCharacterController[] allCharacters = GameObject.FindObjectsByType<ProceduralCharacterController>(FindObjectsSortMode.None);
            foreach (ProceduralCharacterController charController in allCharacters)
            {
                if (charController != null && !charController.spectatorMode)
                {
                    foundController = charController;
                    break;
                }
            }
        }
        
        // Only assign if we found one (don't clear existing if none found)
        if (foundController != null)
        {
            characterController = foundController;
        }
    }
    
    // Find the currently controlled character (not in spectator mode)
    private ProceduralCharacterController FindCurrentlyControlledCharacter()
    {
        ProceduralCharacterController[] allCharacters = GameObject.FindObjectsByType<ProceduralCharacterController>(FindObjectsSortMode.None);
        foreach (ProceduralCharacterController charController in allCharacters)
        {
            if (charController != null && !charController.spectatorMode)
            {
                return charController;
            }
        }
        return null;
    }
    
    // Public method to set the character controller (useful for dynamic assignment)
    public void SetCharacterController(ProceduralCharacterController controller)
    {
        // Unsubscribe from old controller
        if (subscribedController != null)
        {
            subscribedController.OnLimbHealthChanged -= OnLimbHealthChanged;
            subscribedController.OnHealthChanged -= OnCharacterHealthChanged;
        }
        
        // Set new controller
        characterController = controller;
        subscribedController = controller;
        
        // Subscribe to new controller
        if (characterController != null)
        {
            characterController.OnLimbHealthChanged += OnLimbHealthChanged;
            characterController.OnHealthChanged += OnCharacterHealthChanged;
            controlTakenTime = Time.time;
            previousTorsoHealth = characterController.GetTorsoHealth();
            previousLimbHealth.Clear();
            foreach (ProceduralCharacterController.LimbType limbType in System.Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
            {
                ProceduralLimb limb = characterController.GetLimb(limbType);
                if (limb != null)
                    previousLimbHealth[limbType] = limb.GetCurrentHealth();
            }
            UpdateAllLimbUI();
        }
        
        wasAttached = false;
    }

    private void OnDestroy()
    {
        // Unsubscribe from events when destroyed
        if (characterController != null)
        {
            characterController.OnLimbHealthChanged -= OnLimbHealthChanged;
            characterController.OnHealthChanged -= OnCharacterHealthChanged;
        }
        
        if (subscribedController != null && subscribedController != characterController)
        {
            subscribedController.OnLimbHealthChanged -= OnLimbHealthChanged;
            subscribedController.OnHealthChanged -= OnCharacterHealthChanged;
        }
    }

    private void Update()
    {
        // Check if UI should be visible based on player control
        UpdateUIVisibility();

        // Smoothly fade the canvas group
        UpdateCanvasFade();
    }

    // Initialize the mapping between limb types and UI images by auto-detecting based on naming
    private void InitializeLimbImageMap()
    {
        // #region agent log
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:202\",\"message\":\"InitializeLimbImageMap called\",\"data\":{{\"gameObjectName\":\"{gameObject.name}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"A\"}}\n"); } catch { }
        // #endregion
        limbImages.Clear();
        
        // Get all Image components in children of this GameObject (assuming images are child objects)
        Image[] allImages = GetComponentsInChildren<Image>(true);
        
        // #region agent log
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:207\",\"message\":\"Images found\",\"data\":{{\"imageCount\":{allImages.Length},\"imageNames\":[{string.Join(",", System.Array.ConvertAll(allImages, img => $"\\\"{img?.gameObject?.name ?? "null"}\\\""))}]}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"A\"}}\n"); } catch { }
        // #endregion
        
        // For each limb type, try to find a matching Image by name
        foreach (ProceduralCharacterController.LimbType limbType in System.Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
        {
            string limbTypeName = limbType.ToString();
            
            // Convert enum name to expected GameObject name format
            // RightBicep -> "Right Bicep" or "RightBicep"
            // RightForearm -> "Right Forearm" or "RightForearm"
            string searchName = ConvertLimbTypeToImageName(limbTypeName);
            
            // Special case: Torso matches "Chest" in UI
            if (limbType == ProceduralCharacterController.LimbType.Torso)
            {
                searchName = "Chest";
            }
            
            // #region agent log
            try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:217\",\"message\":\"Searching for limb\",\"data\":{{\"limbType\":\"{limbTypeName}\",\"searchName\":\"{searchName}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"B\"}}\n"); } catch { }
            // #endregion
            
            // Try to find Image with matching name (case-insensitive, with or without spaces)
            Image foundImage = null;
            foreach (Image img in allImages)
            {
                if (img != null && img.gameObject != null)
                {
                    string imageName = img.gameObject.name;
                    // Remove common suffixes like " (Image)" for matching
                    string cleanImageName = imageName.Replace(" (Image)", "").Trim();
                    
                    // Normalize both strings: remove spaces and compare (case-insensitive)
                    string normalizedImageName = cleanImageName.Replace(" ", "");
                    string normalizedSearchName = searchName.Replace(" ", "");
                    
                    // #region agent log
                    bool match1 = string.Equals(cleanImageName, searchName, System.StringComparison.OrdinalIgnoreCase);
                    bool match2 = string.Equals(normalizedImageName, normalizedSearchName, System.StringComparison.OrdinalIgnoreCase);
                    bool match3 = string.Equals(cleanImageName, limbTypeName, System.StringComparison.OrdinalIgnoreCase);
                    bool match4 = cleanImageName.IndexOf(searchName, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:240\",\"message\":\"Comparing image name\",\"data\":{{\"limbType\":\"{limbTypeName}\",\"imageName\":\"{imageName}\",\"cleanImageName\":\"{cleanImageName}\",\"searchName\":\"{searchName}\",\"normalizedImageName\":\"{normalizedImageName}\",\"normalizedSearchName\":\"{normalizedSearchName}\",\"match1\":{match1.ToString().ToLower()},\"match2\":{match2.ToString().ToLower()},\"match3\":{match3.ToString().ToLower()},\"match4\":{match4.ToString().ToLower()}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"C\"}}\n"); } catch { }
                    // #endregion
                    
                    // Compare: exact match (with/without spaces), or normalized match
                    // Also check if cleanImageName contains searchName (for more flexible matching)
                    if (match1 || match2 || match3 || match4)
                    {
                        foundImage = img;
                        // #region agent log
                        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:247\",\"message\":\"Match found\",\"data\":{{\"limbType\":\"{limbTypeName}\",\"imageName\":\"{imageName}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"C\"}}\n"); } catch { }
                        // #endregion
                        break;
                    }
                }
            }
            
            // If found, add to dictionary
            if (foundImage != null)
            {
                limbImages[limbType] = foundImage;
                // #region agent log
                try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:255\",\"message\":\"Image added to dictionary\",\"data\":{{\"limbType\":\"{limbTypeName}\",\"imageName\":\"{foundImage.gameObject.name}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"E\"}}\n"); } catch { }
                // #endregion
            }
            else
            {
                // #region agent log
                try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:257\",\"message\":\"No image found for limb\",\"data\":{{\"limbType\":\"{limbTypeName}\",\"searchName\":\"{searchName}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"B\"}}\n"); } catch { }
                // #endregion
            }
        }
        
        // #region agent log
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:258\",\"message\":\"InitializeLimbImageMap complete\",\"data\":{{\"mappedCount\":{limbImages.Count},\"mappedTypes\":[{string.Join(",", limbImages.Keys.Select(k => $"\\\"{k}\\\""))}]}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"E\"}}\n"); } catch { }
        // #endregion
    }
    
    /// <summary>
    /// Returns the limb-to-image mapping for use by BodyPartHoverHandler (click-to-select body parts).
    /// </summary>
    public Dictionary<ProceduralCharacterController.LimbType, Image> GetLimbImageMap()
    {
        return limbImages;
    }

    /// <summary>
    /// Returns display name for a limb type (e.g. "Left Forearm", "Chest" for Torso).
    /// </summary>
    public static string GetLimbDisplayName(ProceduralCharacterController.LimbType limbType)
    {
        if (limbType == ProceduralCharacterController.LimbType.Torso)
            return "Chest";
        return ConvertLimbTypeToImageNameStatic(limbType.ToString());
    }

    private static string ConvertLimbTypeToImageNameStatic(string enumName)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < enumName.Length; i++)
        {
            if (i > 0 && char.IsUpper(enumName[i]))
                sb.Append(' ');
            sb.Append(enumName[i]);
        }
        return sb.ToString();
    }

    // Convert LimbType enum name to expected Image GameObject name
    private string ConvertLimbTypeToImageName(string enumName)
    {
        return ConvertLimbTypeToImageNameStatic(enumName);
    }

    // Handle limb health change events
    private void OnLimbHealthChanged(ProceduralCharacterController.LimbType limbType, float current, float max)
    {
        // Detect damage: health decreased from previous value
        if (previousLimbHealth.TryGetValue(limbType, out float prev) && current < prev)
            lastDamageTime = Time.time;
        previousLimbHealth[limbType] = current;
        // Update the UI for the changed limb
        UpdateLimbUI(limbType, current, max);
        // Also update Torso UI since overall health changes when any limb health changes
        if (characterController != null && limbType != ProceduralCharacterController.LimbType.Torso)
        {
            float torsoCurrent = characterController.GetTorsoHealth();
            float torsoMax = characterController.GetTorsoMaxHealth();
            UpdateLimbUI(ProceduralCharacterController.LimbType.Torso, torsoCurrent, torsoMax);
        }
    }
    
    // Handle overall character health change events (for Torso)
    private void OnCharacterHealthChanged(float current, float max)
    {
        // #region agent log
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:325\",\"message\":\"OnCharacterHealthChanged called\",\"data\":{{\"current\":{current},\"max\":{max},\"characterController\":{(characterController != null ? "exists" : "null")}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"H\"}}\n"); } catch { }
        // #endregion
        if (characterController != null)
        {
            float torsoCurrent = characterController.GetTorsoHealth();
            float torsoMax = characterController.GetTorsoMaxHealth();
            if (previousTorsoHealth >= 0f && torsoCurrent < previousTorsoHealth)
                lastDamageTime = Time.time;
            previousTorsoHealth = torsoCurrent;
            UpdateLimbUI(ProceduralCharacterController.LimbType.Torso, torsoCurrent, torsoMax);
        }
    }

    // Update the UI for a specific limb
    private void UpdateLimbUI(ProceduralCharacterController.LimbType limbType, float current, float max)
    {
        // #region agent log
        bool hasImageInDict = limbImages.ContainsKey(limbType);
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:320\",\"message\":\"UpdateLimbUI called\",\"data\":{{\"limbType\":\"{limbType}\",\"current\":{current},\"max\":{max},\"hasImageInDict\":{hasImageInDict.ToString().ToLower()}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"G\"}}\n"); } catch { }
        // #endregion
        
        // Skip if we don't have an image for this limb
        if (!limbImages.TryGetValue(limbType, out Image limbImage) || limbImage == null)
        {
            // #region agent log
            try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:323\",\"message\":\"No image found for limb, skipping update\",\"data\":{{\"limbType\":\"{limbType}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"G\"}}\n"); } catch { }
            // #endregion
            return;
        }

        // Calculate health percentage
        float healthPercentage = max > 0 ? current / max : 1f;

        // Determine color based on health percentage
        Color healthColor;
        if (healthPercentage <= criticalThreshold)
        {
            healthColor = criticalColor;
        }
        else if (healthPercentage <= damagedThreshold)
        {
            healthColor = damagedColor;
        }
        else
        {
            healthColor = healthyColor;
        }

        // #region agent log
        Color oldColor = limbImage.color;
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:345\",\"message\":\"Setting limb color\",\"data\":{{\"limbType\":\"{limbType}\",\"healthPercentage\":{healthPercentage},\"oldColor\":\"({oldColor.r},{oldColor.g},{oldColor.b},{oldColor.a})\",\"newColor\":\"({healthColor.r},{healthColor.g},{healthColor.b},{healthColor.a})\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"G\"}}\n"); } catch { }
        // #endregion

        // Apply color to the limb image
        limbImage.color = healthColor;
    }

    // Update all limb UI elements based on current health
    public void UpdateAllLimbUI()
    {
        // #region agent log
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:349\",\"message\":\"UpdateAllLimbUI called\",\"data\":{{\"characterController\":{(characterController != null ? "exists" : "null")}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"F\"}}\n"); } catch { }
        // #endregion
        if (characterController == null)
            return;

        foreach (ProceduralCharacterController.LimbType limbType in System.Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
        {
            // Get the limb from the character controller
            ProceduralLimb limb = characterController.GetLimb(limbType);
            
            // #region agent log
            bool hasImage = limbImages.ContainsKey(limbType);
            bool hasLimb = limb != null;
            float currentHealth = limb != null ? limb.GetCurrentHealth() : -1f;
            float maxHealth = limb != null ? limb.maxHealth : -1f;
            try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:357\",\"message\":\"Checking limb for UI update\",\"data\":{{\"limbType\":\"{limbType}\",\"hasImage\":{hasImage.ToString().ToLower()},\"hasLimb\":{hasLimb.ToString().ToLower()},\"currentHealth\":{currentHealth},\"maxHealth\":{maxHealth}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"F\"}}\n"); } catch { }
            // #endregion
            
            // Special handling for Torso: use torso-specific health
            if (limbType == ProceduralCharacterController.LimbType.Torso)
            {
                // Update UI using torso-specific health (works like Head/Arms)
                float torsoCurrent = characterController.GetTorsoHealth();
                float torsoMax = characterController.GetTorsoMaxHealth();
                UpdateLimbUI(limbType, torsoCurrent, torsoMax);
                
                // #region agent log
                Image limbImg = null;
                limbImages.TryGetValue(limbType, out limbImg);
                Color imgColor = limbImg != null ? limbImg.color : Color.clear;
                try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:380\",\"message\":\"UI updated for Torso using torso health\",\"data\":{{\"limbType\":\"{limbType}\",\"currentHealth\":{torsoCurrent},\"maxHealth\":{torsoMax},\"imageColor\":\"({imgColor.r},{imgColor.g},{imgColor.b},{imgColor.a})\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"F\"}}\n"); } catch { }
                // #endregion
            }
            else if (limb != null)
            {
                // Update UI for this limb
                float healthPercentage = limb.GetHealthPercentage();
                UpdateLimbUI(limbType, limb.GetCurrentHealth(), limb.maxHealth);
                
                // #region agent log
                Image limbImg = null;
                limbImages.TryGetValue(limbType, out limbImg);
                Color imgColor = limbImg != null ? limbImg.color : Color.clear;
                try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", $"{{\"id\":\"log_{System.DateTime.Now.Ticks}\",\"timestamp\":{System.DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"location\":\"BodyOutlineUI.cs:392\",\"message\":\"UI updated for limb\",\"data\":{{\"limbType\":\"{limbType}\",\"healthPercentage\":{healthPercentage},\"imageColor\":\"({imgColor.r},{imgColor.g},{imgColor.b},{imgColor.a})\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"F\"}}\n"); } catch { }
                // #endregion
            }
        }
    }

    // Track if we're subscribed to events to prevent duplicate subscriptions
    private ProceduralCharacterController subscribedController = null;

    // Check if UI should be visible based on player control
    private void UpdateUIVisibility()
    {
        // Check if current character controller is still valid and controlled
        // If current controller is in spectator mode, clear it
        if (characterController != null && characterController.spectatorMode)
        {
            // Clear the reference when character enters spectator mode
            if (subscribedController == characterController)
            {
                subscribedController.OnLimbHealthChanged -= OnLimbHealthChanged;
                subscribedController.OnHealthChanged -= OnCharacterHealthChanged;
                subscribedController = null;
            }
            characterController = null;
            wasAttached = false;
        }
        
        // Find the currently controlled character
        ProceduralCharacterController currentlyControlled = FindCurrentlyControlledCharacter();
        
        // If we have a different controlled character, or no character at all, switch to it
        if (currentlyControlled != characterController)
        {
            // Unsubscribe from old controller if any
            if (subscribedController != null)
            {
                subscribedController.OnLimbHealthChanged -= OnLimbHealthChanged;
                subscribedController.OnHealthChanged -= OnCharacterHealthChanged;
                subscribedController = null;
            }
            
            // Set new controller
            characterController = currentlyControlled;
            
            // Subscribe to new controller
            if (characterController != null)
            {
                characterController.OnLimbHealthChanged += OnLimbHealthChanged;
                characterController.OnHealthChanged += OnCharacterHealthChanged;
                subscribedController = characterController;
                controlTakenTime = Time.time;
                previousTorsoHealth = characterController.GetTorsoHealth();
                previousLimbHealth.Clear();
                foreach (ProceduralCharacterController.LimbType limbType in System.Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
                {
                    ProceduralLimb limb = characterController.GetLimb(limbType);
                    if (limb != null)
                        previousLimbHealth[limbType] = limb.GetCurrentHealth();
                }
                UpdateAllLimbUI();
            }
        }
        
        // If still no controller, try the fallback method (check parent/children/siblings)
        if (characterController == null)
        {
            TryFindCharacterController();

            // Subscribe if we found one
            if (characterController != null && subscribedController != characterController)
            {
                if (subscribedController != null)
                {
                    subscribedController.OnLimbHealthChanged -= OnLimbHealthChanged;
                    subscribedController.OnHealthChanged -= OnCharacterHealthChanged;
                }
                characterController.OnLimbHealthChanged += OnLimbHealthChanged;
                characterController.OnHealthChanged += OnCharacterHealthChanged;
                subscribedController = characterController;
                controlTakenTime = Time.time;
                previousTorsoHealth = characterController.GetTorsoHealth();
                previousLimbHealth.Clear();
                foreach (ProceduralCharacterController.LimbType limbType in System.Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
                {
                    ProceduralLimb limb = characterController.GetLimb(limbType);
                    if (limb != null)
                        previousLimbHealth[limbType] = limb.GetCurrentHealth();
                }
                UpdateAllLimbUI();
            }
        }
        
        // Determine if canvas should be enabled based on attachment
        bool isAttached = characterController != null && !characterController.spectatorMode;
        if (canvas != null)
        {
            canvas.enabled = isAttached;
            wasAttached = isAttached;
        }

        // Enable/disable outline root with possession (same system as InventoryUI: active only while on a character)
        if (outlineRoot != null)
            outlineRoot.SetActive(isAttached);
        
        // Target alpha: inventory = full. Else when on a character, show for X seconds after control or after recent damage, then fade out.
        bool inventoryOpen = inventoryUI != null && inventoryUI.inventoryPanel != null && inventoryUI.inventoryPanel.activeSelf;
        if (inventoryOpen)
        {
            targetAlpha = 1f;
        }
        else if (characterController != null && !characterController.spectatorMode)
        {
            float timeSinceControl = Time.time - controlTakenTime;
            float timeSinceLastDamage = Time.time - lastDamageTime;
            bool showingAfterControl = timeSinceControl < showOutlineAfterControlSeconds;
            bool recentDamage = timeSinceLastDamage < fadeOutAfterNoDamageSeconds;
            targetAlpha = (showingAfterControl || recentDamage) ? 1f : 0f;
        }
        else
        {
            targetAlpha = 0f;
        }
    }

    // Smoothly fade all parts: canvas group (if set) and every Graphic under this object.
    // When inventory is open, show at full opacity; when closing inventory, snap to target (no fade out).
    private void UpdateCanvasFade()
    {
        bool inventoryOpen = inventoryUI != null && inventoryUI.inventoryPanel != null && inventoryUI.inventoryPanel.activeSelf;
        if (inventoryOpen)
        {
            currentFadeAlpha = 1f;
        }
        else if (wasInventoryOpen)
        {
            // Just closed inventory: snap to target so outline doesn't fade out
            currentFadeAlpha = targetAlpha;
        }
        else
        {
            float fadeSpeed = targetAlpha > currentFadeAlpha ? fadeInSpeed : fadeOutSpeed;
            currentFadeAlpha = Mathf.Lerp(currentFadeAlpha, targetAlpha, Time.deltaTime * fadeSpeed);
        }
        wasInventoryOpen = inventoryOpen;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = currentFadeAlpha;
            bool isVisible = currentFadeAlpha > 0.01f;
            canvasGroup.interactable = isVisible;
            canvasGroup.blocksRaycasts = isVisible;
        }

        // Apply the same fade alpha to every Graphic (Image, RawImage, Text, etc.) under this object
        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        foreach (Graphic g in graphics)
        {
            if (g == null) continue;
            Color c = g.color;
            g.color = new Color(c.r, c.g, c.b, currentFadeAlpha);
        }
    }

    /// <summary>
    /// Current fade alpha applied to limb graphics. Used by BodyPartHoverHandler for pulsating highlight.
    /// </summary>
    public float GetCurrentFadeAlpha()
    {
        return currentFadeAlpha;
    }
}
