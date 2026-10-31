using TMPro;
using UnityEngine;

namespace Game.UI
{
    public static class WorldLabelStyle
    {
        private const float FontSize = 26f;
        private const float OutlineWidth = 0f;
        private static readonly Color TextColor = new(1f, .93f, .75f, 1f);

        public static void EnsureApplied(TMP_Text label)
        {
            if (label.color != TextColor || !Mathf.Approximately(label.outlineWidth, OutlineWidth) ||
                !Mathf.Approximately(label.fontSize, FontSize) || label.enableAutoSizing)
                Apply(label);
        }

        public static void Apply(TMP_Text label)
        {
            label.color = TextColor;
            label.enableAutoSizing = false;
            label.fontSize = FontSize;
            // TMP creates a per-label material instance; do not change the font
            // asset's shared material (menus and other HUD text use it too).
            label.outlineColor = Color.black;
            label.outlineWidth = OutlineWidth;
            if (label.fontMaterial != null && label.fontMaterial.HasProperty("_OutlineWidth"))
            {
                label.fontMaterial.SetFloat("_OutlineSoftness", 0f);
                label.fontMaterial.DisableKeyword("OUTLINE_ON");
            }
            label.extraPadding = false;
            label.UpdateMeshPadding();
            label.raycastTarget = false;
        }
    }
}
