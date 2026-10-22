# Body Idle head-detail normal draft

Output: `Art/Player/Animations/Body/body_idle_normal_head_details_draft.png`.

Built-in imagegen produces the proposed hair/beard/brow/eye volume; `PrepareBodyNormalDraft.ps1 -Layer HeadDetails` registers the full sprite bounding boxes, samples the generated normals and snaps them to the current block-editor palette. Transparent outside the head-detail mask; intended for composition with the separate skin and cloth layers. Original art and the existing layers are not modified. Pure black source ink is excluded.

The material mask is heuristic: source head pixels up to local row 40, excluding the skin/cloth selections. Registration uses the largest 8-connected alpha component per sprite to ignore detached generator speckles. Inspect hairline, beard, eye alignment and any residual near-black outline pixels manually. This is not yet connected to the player shader. Iris/pupil colour must remain in albedo, not become a cavity in normals.

## Generation prompt

undefined
