using UnityEngine;
using System.IO;
using System;

public static class Texture3DAtlas
{
    public static void SaveTexture3D(Texture3D texture3D, string fileName)
    {
        if (texture3D == null) return;

        int W = texture3D.width;
        int H = texture3D.height;
        int D = texture3D.depth;

        int atlasWidth = W * D;
        int atlasHeight = H;

        Texture2D atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false);
        Color[] allAtlasColors = new Color[atlasWidth * atlasHeight];
        Color[] all3DColors = texture3D.GetPixels();

        for (int z = 0; z < D; z++)
        {
            int atlasXStart = z * W;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int index3D = x + y * W + z * W * H;
                    int indexAtlas = y * atlasWidth + (atlasXStart + x);
                    allAtlasColors[indexAtlas] = all3DColors[index3D];
                }
            }
        }

        atlas.SetPixels(allAtlasColors);
        atlas.Apply();

        byte[] bytes = atlas.EncodeToPNG();
        string filePath = Path.Combine(Application.dataPath, $"{fileName}.png");

        File.WriteAllBytes(filePath, bytes);
        Debug.Log($"Texture3D Atlas saved successfully as a single row to: {filePath}");

        // Cleanup
        UnityEngine.Object.DestroyImmediate(atlas);

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }
}