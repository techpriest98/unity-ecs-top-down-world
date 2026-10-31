# Stick crafting

Flint Shard (item 6) produces Craft Stick (skill 16). Its Visibility output uses Other Hand Item with Branch (item 8); no cursor or distance is required. The keyboard binding is K.

Execute uses Replace Equipped Item with Target Hand = Other Hand, Required Target Item = Branch, Replacement Item = Stick (item 11). One Branch becomes one Stick in the same hand. Flint Shard and its durability stay unchanged. The branch instance's durability and other mutable state are retained. Large Branch is not an ingredient in this recipe. Existing replacement assets default to Source Hand.

Stick is registered in ItemDatabase and ItemIds, supports either hand, uses the shared Throw skill, and is available in Rubedo's Items catalog. Ordinary equipment/world-item saving applies.

## Temporary artwork

Generated with the built-in imagegen tool. The source is retained at:

C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-216f6c67-c8db-43c0-a7fc-24a009340729.png

Final game assets:

- Art/UI/Icons/Stick.png — actual 32×32 PNG, point filtering, transparent.
- Art/UI/Icons/Stick_world.png — actual 32×16 PNG, point filtering, transparent.

Alpha-bound cropping and nearest-neighbour fitting reuse Save-FittedSprite from Tools/ItemGraphEditor/Editor/PreparePineResinSprites.ps1.

Final generation prompt:

Use case: stylized-concept. Asset type: pixel-art survival RPG inventory icon. Draw a single stripped wooden stick, completely peeled of bark and trimmed of every twig and side branch. Plain slender slightly irregular pale tan wood shaft with modest carved facets and blunt ends, no pointed spear tip. Diagonal from lower-left to upper-right, entire object visible, isolated on a genuinely transparent background. Muted dark medieval pixel-art style, clean dark brown outline, restrained beige highlights, coarse crisp hard pixel clusters designed to read at 32 by 32 pixels. No bark texture, no offshoots, no leaves, no rope, no decoration, no hands, no text, no border, no slot frame, no background and no cast shadow.

## Verification

EquippedItemCraftChecks.ps1 runs the actual ReplaceEquippedItemAction and PlayerEquipmentUtility source with ECS adapters: both active hands, unchanged tool, retained target state, no recrafting a Stick, wrong/empty/Large Branch targets, stale source, and default Source Hand behavior. SkillDataGraphChecks.ps1 validates all 11 skill graphs and serialized references. These checks do not replace visual/HUD testing in Unity Play Mode.
