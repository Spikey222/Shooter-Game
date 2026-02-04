# Limb Health System Documentation

## Overview
The Limb Health System provides individual health tracking for each limb of a character in the game. It includes UI visualization that shows the health status of each limb through color-coded indicators.

## Components

### 1. ProceduralLimb
Each limb has its own health properties:
- `maxHealth`: Maximum health value for the limb
- `currentHealth`: Current health value (private, accessed via GetCurrentHealth())
- `damageMultiplier`: Multiplier for damage taken by this limb
- `affectsCharacterHealth`: Whether damage to this limb affects overall character health

Health-related methods:
- `TakeDamage(float amount)`: Apply damage to the limb
- `Heal(float amount)`: Heal the limb
- `GetHealthPercentage()`: Get current health as percentage (0-1)
- `GetCurrentHealth()`: Get current health value
- `SetHealth(float value)`: Set health to specific value
- `ResetHealth()`: Reset health to maximum
- `IsDead()`: Check if limb health is zero

### 2. ProceduralCharacterController
Manages all limbs and their health states:
- `LimbType` enum: Categorizes limbs (Head, Neck, Torso, Arms, Hands, and future Legs)
- `limbMap`: Maps LimbType to ProceduralLimb instances
- `OnLimbHealthChanged` event: Notifies when any limb's health changes
- `GetLimb(LimbType type)`: Returns the limb instance for a given type
- `limbHealthSettings`: Array of customizable health settings for each limb
- `useCustomHealthSettings`: Toggle to apply custom health settings on start
- `InitializeDefaultHealthSettings()`: Context menu function to create default settings for all limbs

### 3. BodyOutlineUI
Visualizes limb health through UI elements:
- Direct mapping between limb types and UI images
- Color-coded health status (green=healthy, yellow=damaged, red=critical)
- UI activation based on player control state
- Smooth fade in/out transitions

## Setup Instructions

### Setting Up the UI
1. Create a Canvas with a CanvasGroup component
2. Add UI Image elements for each limb (head, neck, torso, arms, forearms, hands)
3. Add UI Image elements for future leg implementation (thighs, calves, feet)
4. Add the BodyOutlineUI component to the Canvas
5. Assign the ProceduralCharacterController reference
6. Configure each LimbUIElement in the inspector:
   - Select the appropriate LimbType
   - Assign the corresponding UI Image

### Customizing Limb Health
1. Select your ProceduralCharacterController GameObject
2. Right-click on the component in the Inspector and select "Initialize Default Health Settings" from the context menu
3. This will create default health settings for all limb types
4. Customize each limb's settings in the Inspector:
   - `maxHealth`: Maximum health value for the limb
   - `affectsCharacterHealth`: Whether damage to this limb affects overall character health
   - `damageMultiplier`: Multiplier for damage taken by this limb (higher values mean more damage)
5. Enable or disable `useCustomHealthSettings` to control whether these settings are applied on start

### Health Visualization
- Healthy limbs (>60% health): Green
- Damaged limbs (30-60% health): Yellow
- Critical limbs (<30% health): Red

## Future Implementation
- Leg support has been added to the LimbType enum for future implementation
- UI elements for legs are included but will only be active when leg limbs are added to the character
- Potential for dismemberment or advanced damage effects

## Usage Example
```csharp
// Apply damage to a specific limb
ProceduralLimb headLimb = characterController.GetLimb(ProceduralCharacterController.LimbType.Head);
headLimb.TakeDamage(25f);

// Heal all limbs
characterController.HealAllLimbs(50f);
```
