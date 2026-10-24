# Skill input bindings

Item Skill assets and Item Graph skill nodes expose Input Binding:
- SlotNumber (default): existing HUD slot keys 1–5, preserving old assets.
- Keyboard: choose Key from the Unity Input System enum.
- LeftMouse / MiddleMouse / RightMouse: HUD labels LMB / CMB / RMB.
- None: no hotkey and an empty label.

Bindings belong to the skill definition and follow it between slots/items.
Only visible skills from the active hand are evaluated. At most one skill runs per frame;
conflicts resolve left-to-right in HUD order. Explicit bindings replace slot-number input.
Pause blocks skill input; mouse clicks over UI are ignored. Bindings do not override other
game controls: avoid keys already used by movement, jumping or debug controls.
Active hand switching uses the middle mouse button (CMB), replacing Q. Avoid assigning CMB
to a skill unless triggering it on the same click as hand switching is intentional.
