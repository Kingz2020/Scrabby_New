#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// How the button art is imported, applied on the way in.
//
// Two things matter and neither is Unity's default. The button faces are
// 9-slice sprites, so they need their borders set, or they stretch as one
// picture and the rounded ends go oval. And they are smooth gradients, which
// texture compression turns into visible bands on a phone - exactly the
// stepping that was taken out of the shading by hand. So: sprite, borders,
// and no compression on any platform.
public class ButtonArtImport : AssetPostprocessor
{
    // Raised whenever these settings change, so Unity imports the art again
    // rather than keeping what it made under the old rules.
    public override uint GetVersion()
    {
        return 1;
    }

    private void OnPreprocessTexture()
    {
        string path = assetPath.Replace("\\", "/");

        if (!path.Contains("/Resources/Buttons/"))
            return;

        TextureImporter importer = assetImporter as TextureImporter;

        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // The faces: left, bottom, right, top. The bottom band is the deep
        // one, because it holds the button's thickness as well as the curve
        // of the face - both have to stay as drawn.
        importer.spriteBorder = Path.GetFileName(path).StartsWith("Button_")
            ? new Vector4(64f, 80f, 64f, 62f)
            : Vector4.zero;

        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.RGBA32;
        importer.SetPlatformTextureSettings(android);
    }
}
#endif
