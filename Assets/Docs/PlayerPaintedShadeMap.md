# Optional original painted texture in shade

AnimationDefinition now has **Painted Map** alongside Sprite Sheet (clean diffuse) and Normal Map. Assign the original fully shaded artwork to Painted Map. It must share the diffuse sheet dimensions, frame order and pixel alignment; use readable sRGB color import, not a Unity Normal Map texture.

Build Animation Texture Array now packs a third sRGB PaintedTextureArray using exactly the diffuse trim/layout/page/padding. Missing maps and transparent painted texels copy clean diffuse. Diffuse alpha alone determines the silhouette. Source textures are not modified. TargetMaterial receives _PaintedArray and an availability flag; when no animation uses a painted map, the extra texture sample is disabled. Old atlases remain usable if no PaintedMap has been assigned; assigning/changing a PaintedMap requires rebuilding, like other texture changes.

Runtime and Preview use the same blend:
directBrightness = dot(directLight, (0.2126,0.7152,0.0722))
paintedWeight = 1 - smoothstep(0,0.35,directBrightness)
albedo = lerp(cleanDiffuse,paintedTexture,paintedWeight)
result = albedo * saturate(ambient + directLight)

Direct light is the normal-dependent sun plus point-light response. Residual voxel fill counts as ambient. Thus painted shading is strongest with ambient-only lighting and fades under directional illumination. It is not emissive: complete darkness remains black. On the legacy/no-normal rendering path, the combined legacy light is used as the fade signal because it does not carry separate per-pixel directional lighting. Preview uses its existing isolated lighting approximation for that case.

Preview reads PaintedMap directly, so no atlas rebuild is needed to inspect it there. Paint still displays only raw normals; the painted texture does not affect normal painting or alpha. Existing normal edits and original artwork are untouched. The fade threshold is currently fixed at 0.35 in both C# and HLSL.

After assigning maps, rebuild the animation atlas, assign the newly built atlas to the player as before, and let Unity rebake. Editor and shader checks cover shared copy/padding, missing-map/transparent-hole fallback, shade/direct-light transitions and no self-light. In-editor scene visuals still require review.
