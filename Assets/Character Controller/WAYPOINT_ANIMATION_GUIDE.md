# Waypoint Animation System - Quick Guide

## How to Access the Keyframe Animation System

### Step 1: Select Your Weapon GameObject
1. In the Unity Hierarchy, find your Knife (or any weapon with waypoint animation)
2. Select the GameObject

### Step 2: Find the Waypoint Animation Component
In the Inspector, you'll see:
- **EquippableItem** component (base class)
- **Weapon** component (if applicable)
- **Knife** component (or your weapon class)
- **WaypointAnimation** component (this is the "animator")

### Step 3: Configure Keyframes (Like Animation Keyframes!)
1. In the **WaypointAnimation** component, find the **"Animation Keyframes"** section
2. Click the **"+"** button to add a new keyframe
3. For each keyframe:
   - **Time**: Set when this keyframe should be reached (in seconds from animation start)
   - **Limb Targets**: Click "+" to add multiple limbs that animate simultaneously
   - **Default Easing Curve**: (Optional) Curve applied to all limbs in this keyframe
   - **Event Name**: (Optional) Name for event callbacks

4. For each **Limb Target** in a keyframe:
   - **Target Limb**: Drag the limb from your character's hierarchy (e.g., rightArm, rightForearm)
   - **Target Angle**: Enter the angle in degrees (e.g., -120 for pulled back, 30 for forward)
   - **Easing Curve**: (Optional) Override the keyframe's default curve for this specific limb

### Step 4: Keyframe System Features
- **Simultaneous Animation**: All limbs in a keyframe animate at the same time!
- **Time-Based**: Keyframes are organized by time, not sequence
- **Multiple Limbs**: Add as many limbs as you want to each keyframe
- **Smooth Transitions**: Duration is automatically calculated between keyframes

### Step 5: Example Stabbing Animation Setup
```
Keyframe 0 (Time: 0.0s):
  - rightArm → -120° (pull back)
  - rightForearm → -90° (follow arm back)

Keyframe 1 (Time: 0.15s):
  - rightArm → 30° (jolt forward)
  - rightForearm → 45° (follow arm forward)
  - Event: "Stab" (triggers damage)

Keyframe 2 (Time: 0.3s):
  - rightArm → 60° (return to idle)
  - rightForearm → 75° (return to idle)
```

### Step 6: Test Your Animation
1. Enter Play Mode
2. Equip the weapon
3. Press **Left Mouse Button** or **Space** to trigger the animation
4. Watch multiple limbs animate simultaneously through your keyframes!

## Tips
- Keyframes are automatically sorted by time
- Multiple limbs in the same keyframe animate simultaneously
- Duration between keyframes is calculated automatically
- You can override easing curves per limb or use the keyframe default
- Use Event Names to trigger actions at specific keyframes (like damage)

---

# Weapon Sprite Override System

## How to Set Up Character-Specific Weapon Sprites

### Step 1: Select Your Character
1. In the Unity Hierarchy, select your character GameObject (with ProceduralCharacterController)
2. In the Inspector, find the **"Weapon Sprite Overrides"** section

### Step 2: Add Sprite Override Sets
1. Click **"+"** to add a new sprite override set
2. For each override set:
   - **Override Name**: Give it a descriptive name (e.g., "White Character", "Black Character", "Red Character")
   - **Right Hand Sprite**: Drag the sprite for right hand holding weapon
   - **Left Hand Sprite**: Drag the sprite for left hand holding weapon

### Step 3: Select Which Override to Use
**Option A: Use Character's Default Selection**
- Set **"Selected Sprite Override Index"** on the character to choose which override set to use
- All weapons will use this selection

**Option B: Override Per Weapon**
- In the **Knife** component, set **"Sprite Override Index"** to a specific index
- Set to **-1** to use the character's selected index (default)
- Set to **0, 1, 2, etc.** to force a specific override

### Step 4: How It Works
- When a weapon (like Knife) is equipped, it automatically pulls sprites from the character's sprite overrides
- No need to configure sprites on each weapon - they all use the character's override system
- Weapons can override the character's selection if needed

## Example Setup
```
Character Controller:
  Weapon Sprite Overrides:
    [0] Override Name: "White Character"
        Right Hand Sprite: white_hand_knife
        Left Hand Sprite: white_hand_knife
    
    [1] Override Name: "Black Character"  
        Right Hand Sprite: black_hand_knife
        Left Hand Sprite: black_hand_knife

  Selected Sprite Override Index: 0  (uses "White Character")

Knife Component:
  Sprite Override Index: -1  (uses character's selection, which is 0)
```

When you equip the knife, it will automatically use the sprites from index 0 (White Character).

If you set Knife's Sprite Override Index to 1, it will use "Black Character" sprites regardless of the character's selection.

---

# Adjusting Knife Penetration Depth

## How to Make the Knife Stab Deeper or Shallower

The knife "impaling" visual is controlled by the animation keyframes. Since the knife overlay is attached to the hand, extending the arm further forward makes the knife appear to penetrate deeper into targets.

### Step 1: Open WaypointAnimation Component
1. Select your Knife GameObject
2. Find the **WaypointAnimation** component in the Inspector

### Step 2: Adjust the "Stab Forward" Keyframe
The "penetration depth" is determined by how far forward the arm/forearm extend during the stab animation.

**To increase penetration depth:**
- Increase the **Target Angle** for the arm in the "forward thrust" keyframe
- Example: Change `rightArm → 30°` to `rightArm → 50°` for deeper stab

**To decrease penetration depth:**
- Decrease the **Target Angle** for the arm in the "forward thrust" keyframe
- Example: Change `rightArm → 30°` to `rightArm → 10°` for shallower stab

### Step 3: Example Deep Stab Animation
```
Keyframe 0 (Time: 0.0s):
  - rightArm → -120° (pull back far)
  - rightForearm → -90°

Keyframe 1 (Time: 0.15s):
  - rightArm → 60° (thrust forward farther = deeper penetration)
  - rightForearm → 80° (extend forearm too)
  - Event: "Stab"

Keyframe 2 (Time: 0.4s):
  - rightArm → 60° (idle)
  - rightForearm → 75° (idle)
```

### Step 4: Additional Penetration Tips
- **Knife Overlay Position**: Adjust `Knife Sprite Offset` on the Knife component to position the blade further from the hand
- **Animation Speed**: Use `Animation Speed Multiplier` in WaypointAnimation to make the stab faster/more aggressive
- **Motor Torque**: Increase `Motor Torque` for snappier, more forceful arm movement

---

# Sound Effects

## How to Add Stab Sounds to the Knife

### Step 1: Select Your Knife GameObject
1. In the Unity Hierarchy, select your Knife weapon GameObject

### Step 2: Configure Sound Effects in Knife Component
In the Inspector, find the **"Sound Effects"** section:
- **Stab Sound**: Drag your AudioClip for the stab/hit sound here
- **Stab Volume**: Adjust volume (0-1)

### Step 3: How It Works
- The stab sound plays automatically when the knife hits a target during the damage window
- Sound only plays once per target hit (prevents duplicate sounds from multiple colliders)
- An AudioSource component is automatically added if not present

### Example Setup
```
Knife Component:
  Sound Effects:
    Stab Sound: knife_stab.wav
    Stab Volume: 0.8
```

The sound will play each time the knife successfully damages a target during an attack.
