using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Game.Items;
using Game.Player;
using Game.Items.Skills;
using Game.World.Blocks;
using Game.World.Rendering;

namespace Game.UI
{
    [ExecuteAlways]
    public sealed class EquipmentHud : MonoBehaviour
    {
        [Header("Panel frame")]
        [SerializeField] private RectTransform panelBorder;
        [Tooltip("Extra border space per side. X = left/right, Y = top/bottom.")]
        [SerializeField] private Vector2 panelBorderPadding = new Vector2(16f, 16f);

        [Header("Empty hand slots")]
        [SerializeField] private Sprite emptyLeftHandIcon;
        [SerializeField] private Sprite emptyRightHandIcon;

        [SerializeField] private Image[] icons = new Image[7];
        [SerializeField] private TMP_Text[] names = new TMP_Text[7];

        [Header("Active hand")]
        [SerializeField] private float activeHandBorderPadding = 4f;
        [SerializeField] private Color activeHandBorderColor = new Color(1f, 0.9f, 0.65f, 1f);
        private readonly Image[] activeHandBorders = new Image[2];

        [Header("Selected block")]
        [SerializeField] private float selectedBlockGap = 12f;
        [Header("Block picker")]
        [SerializeField] private Sprite blockPickerSeparator;
        private readonly RectTransform[] selectionFrames = new RectTransform[2];
        private readonly RawImage[] selectionIcons = new RawImage[2];

        public void SetSelectedBlock(int index, ItemPlacementSelection selection, BlockPlacementMode mode)
        {
            if (index < 0 || index >= 2) return;
            var renderer = ChunkProceduralRenderer.Instance;
            Texture2D atlas = null;
            Rect uv = default;
            bool visible = selection.Kind == PlacementSelectionKind.Block && selection.Id > 0 && selection.Id <= byte.MaxValue &&
                renderer != null && renderer.TryGetBlockSideIcon((BlockId)selection.Id, out atlas, out uv,
                    halfBlock: mode == BlockPlacementMode.Half);
            if (!visible)
            {
                if (selectionFrames[index] != null) selectionFrames[index].gameObject.SetActive(false);
                return;
            }
            if (selectionFrames[index] == null)
            {
                var hand = (RectTransform)icons[index].transform.parent;
                var panel = (RectTransform)hand.parent;
                var original = icons[2].transform.parent.GetComponent<Image>();
                var frame = new GameObject("Selected block", typeof(RectTransform)).GetComponent<RectTransform>();
                frame.SetParent(panel, false);
                frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = ((RectTransform)original.transform).rect.size;
                frame.anchoredPosition = new Vector2(hand.anchorMin.x * panel.rect.width + hand.anchoredPosition.x + hand.rect.center.x,
                    panelBorderPadding.y + selectedBlockGap);
                var border = frame.gameObject.AddComponent<Image>();
                border.sprite = original.sprite;
                border.type = original.type;
                border.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
                border.color = original.color;
                border.fillCenter = original.fillCenter;
                border.raycastTarget = false;
                var picture = new GameObject("Block icon", typeof(RectTransform)).GetComponent<RectTransform>();
                picture.SetParent(frame, false);
                picture.anchorMin = picture.anchorMax = new Vector2(0.5f, 0.5f);
                float size = Mathf.Max(1f, Mathf.Min(frame.rect.width, frame.rect.height) - 16f);
                picture.sizeDelta = new Vector2(size, size);
                var icon = picture.gameObject.AddComponent<RawImage>();
                icon.raycastTarget = false;
                selectionFrames[index] = frame;
                selectionIcons[index] = icon;
            }
            selectionIcons[index].texture = atlas;
            selectionIcons[index].uvRect = uv;
            selectionFrames[index].gameObject.SetActive(true);
        }

        public void SetActiveHand(EquipmentSlot hand)
        {
            for (int i = 0; i < 2; i++)
            {
                if (icons[i] == null) continue;
                if (activeHandBorders[i] == null)
                {
                    var slot = icons[i].transform.parent;
                    var original = slot.GetComponent<Image>();
                    var rect = new GameObject("Active hand border", typeof(RectTransform)).GetComponent<RectTransform>();
                    rect.SetParent(slot, false);
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.one * -activeHandBorderPadding;
                    rect.offsetMax = Vector2.one * activeHandBorderPadding;
                    var frame = rect.gameObject.AddComponent<Image>();
                    frame.sprite = original.sprite;
                    frame.type = Image.Type.Sliced;
                    frame.fillCenter = false;
                    frame.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
                    frame.color = activeHandBorderColor;
                    frame.raycastTarget = false;
                    activeHandBorders[i] = frame;
                }
                activeHandBorders[i].gameObject.SetActive(hand == (i == 0 ? EquipmentSlot.LeftHand : EquipmentSlot.RightHand));
            }
        }

        [Header("Item tooltip")]
        [SerializeField] private float tooltipWidth = 300f;
        [SerializeField] private float tooltipGap = 12f;
        private readonly ItemDefinition[] handItems = new ItemDefinition[2];
        private readonly Image[] durabilityBars = new Image[2];

        public void SetDurability(int index, float current, float maximum)
        {
            if (index < 0 || index >= 2 || icons[index] == null)
                return;
            if (durabilityBars[index] == null)
            {
                var rect = new GameObject("Durability bar", typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(icons[index].transform.parent, false);
                rect.anchorMin = new Vector2(0, 0);
                rect.anchorMax = new Vector2(0, 0);
                rect.pivot = new Vector2(0, 0);
                rect.sizeDelta = new Vector2(0, 5);
                rect.anchoredPosition = new Vector2(4, 2);
                var bar = rect.gameObject.AddComponent<Image>();
                bar.raycastTarget = false;
                bar.color = names[index].color;
                durabilityBars[index] = bar;
            }
            bool visible = maximum > 0 && current > 0 && current < maximum;
            var image = durabilityBars[index];
            image.gameObject.SetActive(visible);
            float width = Mathf.Max(0, ((RectTransform)icons[index].transform.parent).rect.width - 8);
            image.rectTransform.sizeDelta = new Vector2(width *
                Mathf.Clamp01(maximum > 0 ? current / maximum : 0), 5);
        }

        private readonly ItemSkillDefinition[] skillItems = new ItemSkillDefinition[5];
        private readonly EquipmentSlot[] skillSources = new EquipmentSlot[5];
        private RectTransform tooltip;
        private TMP_Text tooltipTitle;
        private TMP_Text tooltipDescription;
        private TMP_Text tooltipSource;
        private readonly Vector3[] corners = new Vector3[4];

        public void SetHandItem(int index, ItemDefinition item)
        {
            if (index >= 0 && index < handItems.Length)
                handItems[index] = item;
        }

        public void SetSkill(int index, ItemSkillDefinition skill, EquipmentSlot source)
        {
            if (index < 0 || index >= skillItems.Length)
                return;
            skillItems[index] = skill;
            skillSources[index] = source;
        }

        private void OnDisable()
        {
            CloseBlockPicker();
            if (tooltip != null)
                tooltip.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return;
            if (blockPicker != null)
            {
                UpdateBlockPicker();
                if (tooltip != null) tooltip.gameObject.SetActive(false);
                return;
            }
            int hovered = -1;
            var mouse = Mouse.current;
            var canvas = GetComponent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (mouse != null && Time.timeScale > 0f)
            {
                for (int i = 0; i < 7 && i < icons.Length; i++)
                    if (icons[i] != null && (i < 2 ? handItems[i] != null : skillItems[i - 2] != null) &&
                        RectTransformUtility.RectangleContainsScreenPoint(
                            (RectTransform)icons[i].transform.parent, mouse.position.ReadValue(), camera))
                        hovered = i;
            }
            if (hovered < 0)
            {
                if (tooltip != null)
                    tooltip.gameObject.SetActive(false);
                return;
            }
            if (tooltip == null)
                CreateTooltip();
            tooltip.gameObject.SetActive(true);
            bool isSkill = hovered >= 2;
            string title;
            string description;
            if (isSkill)
            {
                var skill = skillItems[hovered - 2];
                title = string.IsNullOrEmpty(skill.DisplayName) ? skill.name : skill.DisplayName;
                description = skill.Description;
            }
            else
            {
                var item = handItems[hovered];
                title = string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName;
                description = item.Description;
            }
            tooltipTitle.text = title;
            tooltipDescription.text = description ?? "";
            tooltipSource.gameObject.SetActive(isSkill);
            tooltipSource.text = isSkill
                ? (skillSources[hovered - 2] == EquipmentSlot.LeftHand ? "Left hand" : "Right hand")
                : "";
            var root = (RectTransform)transform;
            float width = Mathf.Min(tooltipWidth, root.rect.width - 24f);
            float titleHeight = Mathf.Max(26, tooltipTitle.GetPreferredValues(tooltipTitle.text, width - 32, 0).y);
            float descriptionHeight = string.IsNullOrEmpty(description) ? 0 :
                tooltipDescription.GetPreferredValues(tooltipDescription.text, width - 32, 0).y;
            float contentBottom = 16 + titleHeight;
            if (isSkill)
            {
                SetTextRect(tooltipSource, width - 32, 22, contentBottom + 4);
                contentBottom += 4 + 22;
            }
            float descriptionTop = contentBottom + (isSkill ? 16 : 8);
            float height = (descriptionHeight > 0
                ? descriptionTop + descriptionHeight
                : contentBottom) + 16;
            tooltip.sizeDelta = new Vector2(width, height);
            SetTextRect(tooltipTitle, width - 32, titleHeight, 16);
            SetTextRect(tooltipDescription, width - 32, descriptionHeight, descriptionTop);
            var slot = (RectTransform)icons[hovered].transform.parent;
            slot.GetWorldCorners(corners);
            float x = root.InverseTransformPoint((corners[1] + corners[2]) * 0.5f).x;
            panelBorder.GetWorldCorners(corners);
            float y = root.InverseTransformPoint(corners[1]).y + tooltipGap + panelBorderPadding.y;
            x = Mathf.Clamp(x, root.rect.xMin + width * 0.5f + 8,
                root.rect.xMax - width * 0.5f - 8);
            tooltip.anchoredPosition = new Vector2(x, y);
            tooltip.SetAsLastSibling();
        }

        private void CreateTooltip()
        {
            tooltip = new GameObject("Item tooltip", typeof(RectTransform)).GetComponent<RectTransform>();
            tooltip.SetParent(transform, false);
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            tooltip.pivot = new Vector2(0.5f, 0);
            var background = tooltip.gameObject.AddComponent<Image>();
            background.sprite = panelBorder.parent.GetComponent<Image>().sprite;
            background.color = Color.white;
            background.raycastTarget = false;
            var frame = new GameObject("Border", typeof(RectTransform)).GetComponent<RectTransform>();
            frame.SetParent(tooltip, false);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = -panelBorderPadding;
            frame.offsetMax = panelBorderPadding;
            var image = frame.gameObject.AddComponent<Image>();
            var original = panelBorder.GetComponent<Image>();
            image.sprite = original.sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
            image.raycastTarget = false;
            var diamond = new GameObject("Diamond", typeof(RectTransform)).GetComponent<RectTransform>();
            diamond.SetParent(frame, false);
            diamond.anchorMin = diamond.anchorMax = new Vector2(0.5f, 0f);
            diamond.pivot = new Vector2(0.5f, 0.5f);
            diamond.anchoredPosition = new Vector2(0f, panelBorderPadding.y);
            diamond.sizeDelta = new Vector2(6f, 6f);
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var diamondImage = diamond.gameObject.AddComponent<Image>();
            diamondImage.color = names[0].color;
            diamondImage.raycastTarget = false;
            tooltipTitle = TooltipText("Name", 22);
            tooltipDescription = TooltipText("Description", 17);
            tooltipSource = TooltipText("Source", 15);
        }

        private TMP_Text TooltipText(string objectName, float size)
        {
            var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(tooltip, false);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = names[0].font;
            text.fontSize = size;
            text.color = names[0].color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        private static void SetTextRect(TMP_Text text, float width, float height, float top)
        {
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0, -top);
        }

        private static EquipmentHud pickerOwner;
        public static bool IsBlockPickerOpen => pickerOwner != null && pickerOwner.blockPicker != null;
        private RectTransform blockPicker;
        private Unity.Entities.World pickerWorld;
        private Unity.Entities.Entity pickerPlayer;
        private EquipmentSlot pickerSlot;
        private int pickerItem;
        private Unity.Entities.SimulationSystemGroup pickerSimulation;
        private bool pickerSimulationWasEnabled;

        public static bool OpenBlockPicker(Unity.Entities.EntityManager manager,
            Unity.Entities.Entity player, EquipmentSlot slot, int item)
        {
            if (IsBlockPickerOpen || ChunkProceduralRenderer.Instance?.AvailableBlocks == null) return false;
            var hud = Object.FindAnyObjectByType<EquipmentHud>();
            if (hud == null) return false;
            var simulation = manager.World.GetExistingSystemManaged<Unity.Entities.SimulationSystemGroup>();
            if (simulation == null || !simulation.Enabled) return false;
            hud.pickerWorld = manager.World;
            hud.pickerPlayer = player;
            hud.pickerSlot = slot;
            hud.pickerItem = item;
            hud.pickerSimulation = simulation;
            hud.pickerSimulationWasEnabled = simulation.Enabled;
            pickerOwner = hud;
            hud.CreateBlockPicker();
            simulation.Enabled = false;
            return true;
        }

        public static void CancelBlockPicker()
        {
            if (pickerOwner != null) pickerOwner.CloseBlockPicker();
        }

        private void CloseBlockPicker()
        {
            if (blockPicker != null)
            {
                blockPicker.gameObject.SetActive(false);
                Destroy(blockPicker.gameObject);
                blockPicker = null;
            }
            if (pickerWorld != null && pickerWorld.IsCreated && pickerSimulation != null)
                pickerSimulation.Enabled = pickerSimulationWasEnabled;
            pickerSimulation = null;
            pickerWorld = null;
            if (pickerOwner == this) pickerOwner = null;
        }

        private void UpdateBlockPicker()
        {
            if (pickerWorld == null || !pickerWorld.IsCreated ||
                !pickerWorld.EntityManager.Exists(pickerPlayer) ||
                !pickerWorld.EntityManager.HasBuffer<PlayerEquipmentSlot>(pickerPlayer) ||
                PlayerEquipmentUtility.GetItem(pickerWorld.EntityManager.GetBuffer<PlayerEquipmentSlot>(pickerPlayer, true),
                    pickerSlot) != pickerItem || Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                CloseBlockPicker();
        }

        private void ChooseBlock(BlockId material, BlockPlacementMode mode)
        {
            if (pickerWorld == null || !pickerWorld.IsCreated || !pickerWorld.EntityManager.Exists(pickerPlayer) ||
                !pickerWorld.EntityManager.HasBuffer<PlayerEquipmentSlot>(pickerPlayer))
            { CloseBlockPicker(); return; }
            var slots = pickerWorld.EntityManager.GetBuffer<PlayerEquipmentSlot>(pickerPlayer);
            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                if (entry.Slot != pickerSlot || entry.Instance.DefinitionId != pickerItem) continue;
                entry.Instance.PlacementSelection = new ItemPlacementSelection { Kind = PlacementSelectionKind.Block, Id = (int)material };
                entry.Instance.PlacementMode = mode;
                slots[i] = entry;
                break;
            }
            CloseBlockPicker();
        }

        private static RectTransform PickerRect(string title, Transform parent)
        {
            var rect = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private TMP_Text PickerText(string value, Transform parent, float size)
        {
            var rect = PickerRect(value, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = names[0].font;
            text.fontSize = size;
            text.color = names[0].color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private void CreateBlockPicker()
        {
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            blockPicker = PickerRect("Rubedo block picker", transform);
            blockPicker.anchorMin = Vector2.zero;
            blockPicker.anchorMax = Vector2.one;
            blockPicker.offsetMin = blockPicker.offsetMax = Vector2.zero;
            var shade = blockPicker.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, .65f);
            var panel = PickerRect("Window", blockPicker);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.sizeDelta = new Vector2(700f, 540f);
            var background = panel.gameObject.AddComponent<Image>();
            var originalBackground = panelBorder.parent.GetComponent<Image>();
            background.sprite = originalBackground.sprite;
            background.color = originalBackground.color;
            background.type = originalBackground.type;
            var frame = PickerRect("Frame", panel);
            frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one;
            frame.offsetMin = -panelBorderPadding; frame.offsetMax = panelBorderPadding;
            var border = frame.gameObject.AddComponent<Image>();
            var originalBorder = panelBorder.GetComponent<Image>();
            border.sprite = originalBorder.sprite; border.type = originalBorder.type;
            border.color = originalBorder.color; border.pixelsPerUnitMultiplier = originalBorder.pixelsPerUnitMultiplier;
            border.raycastTarget = false;
            var title = PickerText("Rubedo", panel, 30f);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(600f, 42f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -30f);
            float diamondOffset = title.GetPreferredValues(title.text).x * .5f + 22f;
            foreach (float side in new[] { -1f, 1f })
            {
                var diamond = PickerRect(side < 0 ? "Title left diamond" : "Title right diamond", panel);
                diamond.anchorMin = diamond.anchorMax = new Vector2(.5f, 1f);
                diamond.anchoredPosition = new Vector2(side * diamondOffset, -30f);
                diamond.sizeDelta = new Vector2(7f, 7f);
                diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var decoration = diamond.gameObject.AddComponent<Image>();
                decoration.color = title.color;
                decoration.raycastTarget = false;
            }
            var close = PickerRect("Close", panel);
            close.anchorMin = close.anchorMax = new Vector2(1f, 1f);
            close.anchoredPosition = new Vector2(-25f, -25f);
            close.sizeDelta = new Vector2(32f, 32f);
            close.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, .06f);
            close.gameObject.AddComponent<Button>().onClick.AddListener(CloseBlockPicker);
            var closeText = PickerText("×", close, 24f);
            closeText.rectTransform.anchorMin = Vector2.zero; closeText.rectTransform.anchorMax = Vector2.one;
            closeText.rectTransform.offsetMin = closeText.rectTransform.offsetMax = Vector2.zero;
            var hint = PickerText("Choose a block and its shape   •   Esc to close", panel, 16f);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(.5f, 0f);
            hint.rectTransform.sizeDelta = new Vector2(650f, 30f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 22f);
            var separator = PickerRect("Footer separator", panel);
            separator.anchorMin = separator.anchorMax = new Vector2(.5f, 0f);
            separator.anchoredPosition = new Vector2(0f, 55f);
            separator.sizeDelta = new Vector2(652f, 140f);
            var separatorImage = separator.gameObject.AddComponent<Image>();
            separatorImage.sprite = blockPickerSeparator;
            separatorImage.raycastTarget = false;

            var scrollRect = PickerRect("Blocks", panel);
            scrollRect.anchorMin = Vector2.zero; scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(24f, 72f); scrollRect.offsetMax = new Vector2(-24f, -64f);
            var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            var viewport = PickerRect("Viewport", scrollRect);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            // A transparent graphic receives wheel/drag events in empty grid space.
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            var content = PickerRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1f); content.sizeDelta = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(100f, 120f); grid.spacing = new Vector2(8f, 10f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 6;
            grid.childAlignment = TextAnchor.UpperCenter;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            var seen = new System.Collections.Generic.HashSet<BlockId>();
            foreach (var block in ChunkProceduralRenderer.Instance.AvailableBlocks)
            {
                if (block.BlockId == BlockId.Air || !SplitCellUtility.IsKnownMaterial(block.BlockId) || !seen.Add(block.BlockId)) continue;
                AddBlockChoice(content, block.BlockId, BlockPlacementMode.Full);
                AddBlockChoice(content, block.BlockId, BlockPlacementMode.Half);
            }
            blockPicker.SetAsLastSibling();
        }

        private void AddBlockChoice(Transform parent, BlockId material, BlockPlacementMode mode)
        {
            if (!ChunkProceduralRenderer.Instance.TryGetBlockSideIcon(material, out var atlas, out var uv,
                    halfBlock: mode == BlockPlacementMode.Half)) return;
            var cell = PickerRect(material + " " + mode, parent);
            var frame = PickerRect("Slot", cell);
            frame.anchorMin = frame.anchorMax = new Vector2(.5f, 1f);
            frame.pivot = new Vector2(.5f, 1f); frame.sizeDelta = new Vector2(76f, 76f);
            var image = frame.gameObject.AddComponent<Image>();
            var original = icons[2].transform.parent.GetComponent<Image>();
            image.sprite = original.sprite; image.type = original.type; image.color = original.color;
            image.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
            var current = PlayerEquipmentUtility.GetInstance(
                pickerWorld.EntityManager.GetBuffer<PlayerEquipmentSlot>(pickerPlayer, true), pickerSlot);
            if (current.PlacementSelection.Kind == PlacementSelectionKind.Block &&
                current.PlacementSelection.Id == (int)material && current.PlacementMode == mode)
                image.color = activeHandBorderColor;
            var button = frame.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(() => ChooseBlock(material, mode));
            var picture = PickerRect("Icon", frame);
            picture.anchorMin = picture.anchorMax = new Vector2(.5f, .5f);
            picture.sizeDelta = new Vector2(60f, 60f);
            var icon = picture.gameObject.AddComponent<RawImage>();
            icon.texture = atlas; icon.uvRect = uv; icon.raycastTarget = false;
            var label = PickerText(System.Text.RegularExpressions.Regex.Replace(material.ToString(), "([a-z])([A-Z])", "$1 $2") +
                "\n" + mode, cell, 14f);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, 0f);
            label.rectTransform.pivot = new Vector2(.5f, 0f); label.rectTransform.sizeDelta = new Vector2(100f, 42f);
        }

        private void OnEnable() => ApplyBorderPadding();

        private void OnValidate() => ApplyBorderPadding();

        private void ApplyBorderPadding()
        {
            if (panelBorder == null)
                return;

            // Stretch relative to the background so resizing the panel also
            // preserves the configured expansion without a per-frame update.
            panelBorder.anchorMin = Vector2.zero;
            panelBorder.anchorMax = Vector2.one;
            panelBorder.offsetMin = -panelBorderPadding;
            panelBorder.offsetMax = panelBorderPadding;
        }

        public void SetSlot(int index, Sprite icon, string title)
        {
            if (index < 0 || index >= icons.Length || index >= names.Length ||
                icons[index] == null || names[index] == null)
                return;
            if (index == 0 && icon == null && title == "—")
                icon = emptyLeftHandIcon;
            else if (index == 1 && icon == null && title == "—")
                icon = emptyRightHandIcon;
            else if (index == 1 && icon == null && title == "—")
                icon = emptyRightHandIcon;
            if (index == 0 && icon == null && title == "—")
                icon = emptyLeftHandIcon;
            if (icons[index].sprite != icon)
                icons[index].sprite = icon;
            icons[index].enabled = icon != null;
            string fallback = icon != null ? "" : title;
            if (names[index].text != fallback)
                names[index].text = fallback;
        }
    }
}
