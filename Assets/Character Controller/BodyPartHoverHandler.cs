using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Click-to-lock body part inspection UI. Click a limb to show part name and statistical readout
/// (HP %, bleed value, severity 0–2, damage type code). Also drives overall and per-limb condition bars.
/// </summary>
[RequireComponent(typeof(BodyOutlineUI))]
public class BodyPartHoverHandler : MonoBehaviour
{
    [Header("Text Elements")]
    public TextMeshProUGUI partNameText;
    public TextMeshProUGUI descriptionText;

    [Header("Overall Condition Bar")]
    public Image conditionBarImage;
    public TextMeshProUGUI conditionBarText;

    [Header("Limb Condition Bar (per selected limb)")]
    public Image limbConditionBarImage;
    public TextMeshProUGUI limbConditionBarText;

    [Header("Blood Level Bar (total blood - death when empty)")]
    [Tooltip("Fill image for blood bar (assign BloodBarFill from BloodBarPanel in Inventory UI)")]
    public Image bloodBarImage;
    [Tooltip("Text for blood bar (assign BloodBarText from BloodBarPanel)")]
    public TextMeshProUGUI bloodBarText;

    [Header("Deselect")]
    [Tooltip("Optional: Graphic (e.g. transparent Image) that clears selection when clicked.")]
    public Graphic deselectPanel;

    [Header("Bar Smoothing")]
    [Tooltip("Smooth time for bar fill transitions (curve-style ease)")]
    [Range(0.05f, 0.5f)]
    public float barSmoothTime = 0.15f;

    [Header("Selection Highlight")]
    [Tooltip("Pulsating transparency: min alpha (0–1)")]
    [Range(0.3f, 0.9f)]
    public float pulseMinAlpha = 0.8f;
    [Tooltip("Pulsating transparency: max alpha (0–1)")]
    [Range(0.7f, 1f)]
    public float pulseMaxAlpha = 1f;
    [Tooltip("Pulsation speed (cycles per second) – lower = slower, calmer")]
    [Range(0.3f, 2f)]
    public float pulseSpeed = 0.4f;

    [Header("Condition Thresholds")]
    [Tooltip("Health above this = Excellent")]
    [Range(0f, 1f)]
    public float damagedThreshold = 0.99f;
    [Tooltip("Health above this = Fine (below = Bad)")]
    [Range(0f, 1f)]
    public float criticalThreshold = 0.2f;

    private BodyOutlineUI bodyOutlineUI;
    private ProceduralCharacterController characterController;
    private BleedingController bleedingController;
    private ProceduralCharacterController.LimbType? selectedLimb;
    private Dictionary<Image, LimbClickTarget> limbClickTargets = new Dictionary<Image, LimbClickTarget>();

    // Bar smoothing (curve-style)
    private float targetOverallFill;
    private float targetLimbFill;
    private float targetBloodFill;
    private float smoothOverallFill;
    private float smoothLimbFill;
    private float smoothBloodFill;
    private float overallFillVelocity;
    private float limbFillVelocity;
    private float bloodFillVelocity;

    private void Awake()
    {
        bodyOutlineUI = GetComponent<BodyOutlineUI>();
        if (bodyOutlineUI == null)
            bodyOutlineUI = GetComponentInParent<BodyOutlineUI>();
    }

    private void Start()
    {
        SetupLimbClickTargets();
        if (deselectPanel != null)
        {
            var et = deselectPanel.GetComponent<EventTrigger>() ?? deselectPanel.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(_ => ClearSelection());
            if (et.triggers == null) et.triggers = new List<EventTrigger.Entry>();
            et.triggers.Add(entry);
        }
    }

    private void OnEnable()
    {
        RefreshCharacterReferences();
    }

    private void Update()
    {
        RefreshCharacterReferences();
        UpdateBarSmoothing();
        UpdateLimbRaycastState();
    }

    private void LateUpdate()
    {
        UpdateSelectionPulsation();
    }

    private void RefreshCharacterReferences()
    {
        var controller = bodyOutlineUI != null ? bodyOutlineUI.characterController : null;
        if (controller == characterController) return;

        if (characterController != null)
        {
            characterController.OnHealthChanged -= OnHealthChanged;
            characterController.OnLimbHealthChanged -= OnLimbHealthChanged;
        }
        if (bleedingController != null)
            bleedingController.OnBloodLevelChanged -= OnBloodLevelChanged;

        characterController = controller;
        bleedingController = characterController != null ? characterController.GetComponent<BleedingController>() : null;

        if (characterController != null)
        {
            characterController.OnHealthChanged += OnHealthChanged;
            characterController.OnLimbHealthChanged += OnLimbHealthChanged;
            if (bleedingController != null)
                bleedingController.OnBloodLevelChanged += OnBloodLevelChanged;
            UpdateOverallConditionBar();
            UpdateBloodBar();
        }
        else
        {
            ClearAllDisplays();
        }
    }

    private void SetupLimbClickTargets()
    {
        if (bodyOutlineUI == null) return;

        var limbMap = bodyOutlineUI.GetLimbImageMap();
        if (limbMap == null) return;

        foreach (var kvp in limbMap)
        {
            var limbType = kvp.Key;
            var img = kvp.Value;
            if (img == null) continue;

            img.raycastTarget = true;

            var target = img.GetComponent<LimbClickTarget>();
            if (target == null)
                target = img.gameObject.AddComponent<LimbClickTarget>();

            target.limbType = limbType;
            target.handler = this;
            limbClickTargets[img] = target;
        }
    }

    public void OnLimbClicked(ProceduralCharacterController.LimbType limbType)
    {
        if (!IsInventoryOpen()) return;
        var invUI = bodyOutlineUI != null ? bodyOutlineUI.inventoryUI : null;
        if (invUI == null) invUI = FindFirstObjectByType<InventoryUI>();
        if (invUI != null && invUI.HasPendingHealable())
        {
            if (invUI.TryApplyPendingHealableToLimb(limbType))
            {
                selectedLimb = limbType;
                UpdateLimbDisplay();
            }
            return;
        }
        selectedLimb = limbType;
        UpdateLimbDisplay();
    }

    private bool IsInventoryOpen()
    {
        var invUI = bodyOutlineUI != null ? bodyOutlineUI.inventoryUI : null;
        if (invUI == null) invUI = FindFirstObjectByType<InventoryUI>();
        if (invUI == null || invUI.inventoryPanel == null) return false;
        return invUI.inventoryPanel.activeSelf;
    }

    private void UpdateLimbRaycastState()
    {
        bool shouldReceiveClicks = IsInventoryOpen();
        foreach (var kvp in limbClickTargets)
        {
            if (kvp.Key != null)
                kvp.Key.raycastTarget = shouldReceiveClicks;
        }
        if (!shouldReceiveClicks && selectedLimb.HasValue)
            ClearSelection();
    }

    public void ClearSelection()
    {
        selectedLimb = null;
        UpdateLimbDisplay();
    }

    private void UpdateLimbDisplay()
    {
        if (!selectedLimb.HasValue)
        {
            if (partNameText != null) partNameText.text = "—";
            if (descriptionText != null) descriptionText.text = "";
            targetLimbFill = 0f;
            return;
        }

        var limbType = selectedLimb.Value;
        float healthPercent = GetLimbHealthPercent(limbType);
        targetLimbFill = healthPercent;

        if (partNameText != null)
            partNameText.text = BodyOutlineUI.GetLimbDisplayName(limbType);

        int hpPercent = Mathf.RoundToInt(healthPercent * 100f);
        string bleedCategory = GetBleedCategory(limbType);
        string conditionsLine = GetAllConditionWording(limbType, healthPercent);
        float painPercent = characterController != null ? characterController.GetPainPercentForLimb(limbType) : 0f;

        if (descriptionText != null)
            descriptionText.text = FormatStatisticalDescription(bleedCategory, conditionsLine, painPercent);

        if (limbConditionBarText != null)
            limbConditionBarText.text = $"{hpPercent}%";
    }

    private float GetLimbHealthPercent(ProceduralCharacterController.LimbType limbType)
    {
        if (characterController == null) return 1f;
        if (limbType == ProceduralCharacterController.LimbType.Torso)
        {
            float max = characterController.GetTorsoMaxHealth();
            if (max <= 0f) return 1f;
            return characterController.GetTorsoHealth() / max;
        }
        return characterController.GetLimbHealthPercentage(limbType);
    }

    private string GetBleedCategory(ProceduralCharacterController.LimbType limbType)
    {
        if (bleedingController == null) return "None";
        float intensity = bleedingController.GetBleedIntensityFor(limbType);
        if (intensity <= 0f) return "None";
        if (intensity >= bleedingController.heavyBleedThreshold) return "Heavy Bleed";
        if (intensity >= bleedingController.lightBleedThreshold) return "Light Bleed";
        return "None";
    }

    private int GetSeverityTier(float healthPercent)
    {
        if (healthPercent > damagedThreshold) return 0;
        if (healthPercent > criticalThreshold) return 1;
        return 2;
    }

    /// <summary>
    /// Wording for one damage type at current severity (tier 0 = minor, 1 = moderate, 2 = bad).
    /// </summary>
    private static string GetConditionWordingForDamageType(Weapon.DamageType type, int severityTier)
    {
        if (type == Weapon.DamageType.Generic)
        {
            return severityTier switch { 0 => "Excellent", 1 => "Fine", _ => "Bad" };
        }
        switch (type)
        {
            case Weapon.DamageType.Stab:
                return severityTier switch { 0 => "minor puncture", 1 => "punctured", _ => "badly punctured" };
            case Weapon.DamageType.Slash:
                return severityTier switch { 0 => "minor slicing", 1 => "sliced", _ => "badly slashed" };
            case Weapon.DamageType.Blunt:
                return severityTier switch { 0 => "minor bruising", 1 => "bruised", _ => "badly bruised" };
            default:
                return severityTier switch { 0 => "Excellent", 1 => "Fine", _ => "Bad" };
        }
    }

    private string GetAllConditionWording(ProceduralCharacterController.LimbType limbType, float healthPercent)
    {
        if (characterController == null) return "—";
        var types = characterController.GetDamageTypesForLimb(limbType);
        if (types == null || types.Count == 0) return "—";
        int tier = GetSeverityTier(healthPercent);
        return string.Join(", ", types.Select(t => GetConditionWordingForDamageType(t, tier)));
    }

    private static string FormatStatisticalDescription(string bleedCategory, string conditionsLine, float painPercent)
    {
        int painPct = Mathf.RoundToInt(painPercent * 100f);
        return $"Bleed: {bleedCategory}\nConditions: {conditionsLine}\nPain: {painPct}%";
    }

    private void OnHealthChanged(float current, float max)
    {
        UpdateOverallConditionBar();
    }

    private void OnLimbHealthChanged(ProceduralCharacterController.LimbType limbType, float current, float max)
    {
        if (selectedLimb == limbType)
            UpdateLimbDisplay();
        UpdateOverallConditionBar();
    }

    private void OnBloodLevelChanged(float current, float max)
    {
        UpdateBloodBar();
    }

    private void UpdateOverallConditionBar()
    {
        if (characterController == null)
        {
            targetOverallFill = 0f;
            return;
        }

        float current = characterController.GetCurrentHealth();
        float max = characterController.GetMaxHealth();
        targetOverallFill = max > 0f ? Mathf.Clamp01(current / max) : 1f;

        if (conditionBarText != null)
            conditionBarText.text = $"{Mathf.RoundToInt(targetOverallFill * 100)}%";
    }

    private void UpdateBloodBar()
    {
        if (bleedingController == null)
        {
            targetBloodFill = 0f;
            return;
        }
        targetBloodFill = bleedingController.GetBloodLevelPercent();
        if (bloodBarText != null)
            bloodBarText.text = $"{Mathf.RoundToInt(targetBloodFill * 100)}%";
    }

    private void UpdateBarSmoothing()
    {
        smoothOverallFill = Mathf.SmoothDamp(smoothOverallFill, targetOverallFill, ref overallFillVelocity, barSmoothTime);
        smoothLimbFill = Mathf.SmoothDamp(smoothLimbFill, targetLimbFill, ref limbFillVelocity, barSmoothTime);
        smoothBloodFill = Mathf.SmoothDamp(smoothBloodFill, targetBloodFill, ref bloodFillVelocity, barSmoothTime);

        if (conditionBarImage != null)
            conditionBarImage.fillAmount = smoothOverallFill;
        if (limbConditionBarImage != null)
            limbConditionBarImage.fillAmount = smoothLimbFill;
        if (bloodBarImage != null)
            bloodBarImage.fillAmount = smoothBloodFill;

        if (characterController == null && conditionBarText != null)
            conditionBarText.text = "—";
        if (!selectedLimb.HasValue && limbConditionBarText != null)
            limbConditionBarText.text = "—";
        if (bleedingController == null && bloodBarText != null)
            bloodBarText.text = "—";
    }

    private void UpdateSelectionPulsation()
    {
        if (bodyOutlineUI == null) return;

        var limbMap = bodyOutlineUI.GetLimbImageMap();
        if (limbMap == null) return;

        float baseAlpha = bodyOutlineUI.GetCurrentFadeAlpha();
        float t = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f);
        float pulseAlpha = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, t);

        foreach (var kvp in limbMap)
        {
            var img = kvp.Value;
            if (img == null) continue;

            bool isSelected = selectedLimb.HasValue && kvp.Key == selectedLimb.Value;
            float alpha = isSelected ? baseAlpha * pulseAlpha : baseAlpha;
            Color c = img.color;
            img.color = new Color(c.r, c.g, c.b, alpha);
        }
    }

    private void ClearOverallBar()
    {
        targetOverallFill = 0f;
        if (conditionBarText != null) conditionBarText.text = "—";
    }

    private void ClearBloodBar()
    {
        targetBloodFill = 0f;
        if (bloodBarText != null) bloodBarText.text = "—";
    }

    private void ClearAllDisplays()
    {
        ClearSelection();
        ClearOverallBar();
        ClearBloodBar();
    }

    private void OnDestroy()
    {
        if (characterController != null)
        {
            characterController.OnHealthChanged -= OnHealthChanged;
            characterController.OnLimbHealthChanged -= OnLimbHealthChanged;
        }
        if (bleedingController != null)
            bleedingController.OnBloodLevelChanged -= OnBloodLevelChanged;
    }

    private class LimbClickTarget : MonoBehaviour, IPointerClickHandler
    {
        public ProceduralCharacterController.LimbType limbType;
        public BodyPartHoverHandler handler;

        public void OnPointerClick(PointerEventData eventData)
        {
            handler?.OnLimbClicked(limbType);
        }
    }
}
