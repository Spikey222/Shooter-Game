# Limb Health UI Testing Tool

This component provides a simple way to test the limb health UI visualization without having to trigger damage events in gameplay.

## Setup Instructions

1. Add the `LimbHealthUITester` component to your character GameObject (the same one that has the `ProceduralCharacterController` component)
2. The component will automatically access the limbMap from the ProceduralCharacterController

## How to Use

1. Select your character GameObject in the Unity Editor
2. In the Inspector, find the `LimbHealthUITester` component
3. Check the "Enable UI Testing" checkbox
4. Adjust the sliders for each body part to see immediate updates to the health UI
5. The console will display detailed logs about each limb's health changes

## Available Controls

The component provides sliders for the following body parts:
- Head
- Neck
- Torso
- Right Arm
- Right Forearm
- Right Hand
- Left Arm
- Left Forearm
- Left Hand

Each slider ranges from 0-100, representing the health percentage of that limb.

## Debugging

The component includes comprehensive logging:
- When UI testing is active, it will log all health changes to the console
- If any limb is not found in the limbMap, warning messages will be displayed
- Current health values are shown alongside the values you're setting

## Implementation Details

The `LimbHealthUITester` component uses reflection to access the private `limbMap` field in the `ProceduralCharacterController`. This allows it to directly modify limb health values without modifying the character controller itself.

The health values are applied every frame when UI testing is enabled, allowing for real-time adjustments in the editor.
