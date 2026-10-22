using UnityEngine;

// Separate serialized object permits regular Unity Undo without editing imported textures.
public sealed class CharacterNormalPaintDraft : ScriptableObject
{
    public Texture2D Source;
    public Color32[] Pixels;
    public string SourceHash;
    public bool Dirty;
}
