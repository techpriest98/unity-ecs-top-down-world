using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Items.Skills
{
    public enum SkillInputButton { SlotNumber, Keyboard, LeftMouse, MiddleMouse, RightMouse, None }

    [System.Serializable]
    public struct SkillInputBinding
    {
        public SkillInputButton Button;
        public Key Key;

        public bool IsMouse => Button == SkillInputButton.LeftMouse || Button == SkillInputButton.MiddleMouse || Button == SkillInputButton.RightMouse;

        private Key ResolvedKey(int slot) => Button == SkillInputButton.SlotNumber && slot >= 0 && slot < 5
            ? (Key)((int)UnityEngine.InputSystem.Key.Digit1 + slot) : Key;

        public bool WasPressed(int slot)
        {
            var mouse = Mouse.current;
            switch (Button)
            {
                case SkillInputButton.LeftMouse: return mouse != null && mouse.leftButton.wasPressedThisFrame;
                case SkillInputButton.MiddleMouse: return mouse != null && mouse.middleButton.wasPressedThisFrame;
                case SkillInputButton.RightMouse: return mouse != null && mouse.rightButton.wasPressedThisFrame;
                case SkillInputButton.None: return false;
            }
            var keyboard = Keyboard.current;
            Key key = ResolvedKey(slot);
            if (keyboard == null || key == UnityEngine.InputSystem.Key.None) return false;
            foreach (var control in keyboard.allKeys)
                if (control.keyCode == key) return control.wasPressedThisFrame;
            return false;
        }

        public string Label(int slot)
        {
            switch (Button)
            {
                case SkillInputButton.LeftMouse: return "LMB";
                case SkillInputButton.MiddleMouse: return "CMB";
                case SkillInputButton.RightMouse: return "RMB";
                case SkillInputButton.None: return "";
                case SkillInputButton.SlotNumber: return (slot + 1).ToString();
            }
            Key key = ResolvedKey(slot);
            if (key == UnityEngine.InputSystem.Key.None) return "";
            // Key identifies the physical binding; displayName follows the OS
            // keyboard layout and would turn the HUD's Latin hints into Cyrillic.
            string name = key.ToString();
            return name.StartsWith("Digit") ? name.Substring(5) : name;
        }
    }

    [CreateAssetMenu(
        fileName = "ItemSkill",
        menuName = "Game/Items/Skills/Definition")]
    public sealed class ItemSkillDefinition : ScriptableObject
    {
        public int Id;
        public string DisplayName;
        public string Description;
        public Sprite Icon;
        [Tooltip("SlotNumber preserves the old 1–5 controls. Keyboard uses Key; mouse buttons ignore Key. Duplicate buttons use the first available HUD skill.")]
        public SkillInputBinding InputBinding;
        [HideInInspector] public bool UseChain = true;
        public SkillChainNode FirstNode;
        [HideInInspector] public ItemSkillCondition[] Conditions = System.Array.Empty<ItemSkillCondition>();
        [HideInInspector] public ItemSkillAction[] Actions = System.Array.Empty<ItemSkillAction>();

        public bool HasActions
        {
            get
            {
                if (UseChain) return true;
                if (Actions != null)
                    foreach (var action in Actions)
                        if (action != null)
                            return true;
                return false;
            }
        }
    }
}
