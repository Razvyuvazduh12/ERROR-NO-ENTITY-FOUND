// SpriteSheetGridSlicer.cs
// Put this file anywhere under a folder named "Editor" (e.g. Assets/Editor/).
// Menu: Tools > Sprites > Grid Slicer (Batch)
//
// Batch-slices ANY number of selected textures with the same grid settings.
// Solves: Unity's Sprite Editor can only slice one texture at a time.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.U2D.Sprites;
#endif

public class SpriteSheetGridSlicer : EditorWindow
{
    // ---- grid ----
    int columns = 10;
    int rows = 10;
    int maxCells = 26;              // 0 = slice every cell
    int offsetX = 0, offsetY = 0;   // top-left origin, in pixels
    int inset = 0;                  // shrink every cell by N px (kills edge bleeding)

    // ---- naming ----
    string cellNames = "abcdefghijklmnopqrstuvwxyz1234567890,.?![%~";
    bool useCellNames = true;
    bool prefixWithFileName = false;

    // ---- per-cell overrides (for glyphs that overflow their default cell, e.g. descenders) ----
    [TextArea(2, 6)]
    string cellOverrides = "f:0,20,0,0"; // one per line: name:expandTop,expandBottom,expandLeft,expandRight (px)

    // ---- sprite settings ----
    Vector2 pivot = new Vector2(0.5f, 0.5f);
    float pixelsPerUnit = 100f;
    int maxTextureSize = 8192;
    bool applyImportSettings = true;
    bool alphaIsTransparency = true;

    Vector2 scroll;

    [MenuItem("Tools/Sprites/Grid Slicer (Batch)")]
    static void Open()
    {
        GetWindow<SpriteSheetGridSlicer>(true, "Grid Slicer (Batch)").minSize = new Vector2(380, 560);
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        var textures = GetSelectedTextures();
        EditorGUILayout.HelpBox(
            textures.Count == 0
                ? "Select one or more textures in the Project window."
                : $"{textures.Count} texture(s) selected.",
            textures.Count == 0 ? MessageType.Warning : MessageType.Info);

        EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);
        columns = Mathf.Max(1, EditorGUILayout.IntField("Columns", columns));
        rows = Mathf.Max(1, EditorGUILayout.IntField("Rows", rows));
        maxCells = Mathf.Max(0, EditorGUILayout.IntField("Max cells (0 = all)", maxCells));
        offsetX = Mathf.Max(0, EditorGUILayout.IntField("Offset X (px)", offsetX));
        offsetY = Mathf.Max(0, EditorGUILayout.IntField("Offset Y (px, from top)", offsetY));
        inset = Mathf.Max(0, EditorGUILayout.IntField("Inset per cell (px)", inset));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Naming", EditorStyles.boldLabel);
        useCellNames = EditorGUILayout.Toggle("Use name list", useCellNames);
        using (new EditorGUI.DisabledScope(!useCellNames))
            cellNames = EditorGUILayout.TextField("Names (1 char each)", cellNames);
        prefixWithFileName = EditorGUILayout.Toggle("Prefix with file name", prefixWithFileName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Per-cell overrides", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "One override per line: name:top,bottom,left,right (all in pixels). Expands that " +
            "cell's rect in the given direction — e.g. f:0,20,0,0 extends f's cell 20px further " +
            "down to catch a descender that dips into the row below.",
            MessageType.None);
        cellOverrides = EditorGUILayout.TextArea(cellOverrides, GUILayout.Height(60));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sprite settings", EditorStyles.boldLabel);
        pivot = EditorGUILayout.Vector2Field("Pivot (0-1)", pivot);
        pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", pixelsPerUnit);
        applyImportSettings = EditorGUILayout.Toggle("Fix import settings", applyImportSettings);
        using (new EditorGUI.DisabledScope(!applyImportSettings))
        {
            maxTextureSize = EditorGUILayout.IntPopup("Max Texture Size", maxTextureSize,
                new[] { "1024", "2048", "4096", "8192", "16384" },
                new[] { 1024, 2048, 4096, 8192, 16384 });
            alphaIsTransparency = EditorGUILayout.Toggle("Alpha Is Transparency", alphaIsTransparency);
        }

        // live preview of cell size for the first selected texture
        if (textures.Count > 0 && TryGetSourceSize(textures[0], out int pw, out int ph))
        {
            float cw = (pw - offsetX) / (float)columns;
            float ch = (ph - offsetY) / (float)rows;
            string msg = $"Source: {pw} x {ph}  ->  cell {cw:0.##} x {ch:0.##} px";
            bool clean = Mathf.Approximately(cw, Mathf.Round(cw)) && Mathf.Approximately(ch, Mathf.Round(ch));
            EditorGUILayout.HelpBox(
                clean ? msg : msg + "\nNot a whole number: cells will be rounded (e.g. 460/461 px) and stay gap-free, but a texture size divisible by the column/row count is cleaner.",
                clean ? MessageType.None : MessageType.Warning);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(textures.Count == 0))
        {
            if (GUILayout.Button("Slice selected textures", GUILayout.Height(32)))
                SliceAll(textures);
        }

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------

    static List<TextureImporter> GetSelectedTextures()
    {
        var list = new List<TextureImporter>();
        foreach (var obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null) list.Add(imp);
        }
        return list;
    }

    // Parses the "name:top,bottom,left,right" text box into a lookup table.
    static Dictionary<string, (int top, int bottom, int left, int right)> ParseOverrides(string text)
    {
        var dict = new Dictionary<string, (int, int, int, int)>();
        if (string.IsNullOrEmpty(text)) return dict;

        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split(':');
            if (parts.Length != 2) continue;

            string name = parts[0].Trim();
            var nums = parts[1].Split(',');
            if (nums.Length != 4) continue;

            if (int.TryParse(nums[0].Trim(), out int top) &&
                int.TryParse(nums[1].Trim(), out int bottom) &&
                int.TryParse(nums[2].Trim(), out int left) &&
                int.TryParse(nums[3].Trim(), out int right))
            {
                dict[name] = (top, bottom, left, right);
            }
        }
        return dict;
    }

    void SliceAll(List<TextureImporter> importers)
    {
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < importers.Count; i++)
            {
                var imp = importers[i];
                EditorUtility.DisplayProgressBar("Grid Slicer",
                    System.IO.Path.GetFileName(imp.assetPath), (i + 1f) / importers.Count);
                SliceOne(imp);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }
        Debug.Log($"[Grid Slicer] Sliced {importers.Count} texture(s) into {columns}x{rows} cells.");
    }

    void SliceOne(TextureImporter importer)
    {
        if (!TryGetSourceSize(importer, out int texW, out int texH))
        {
            Debug.LogError($"[Grid Slicer] Could not read size of {importer.assetPath}");
            return;
        }

        if (applyImportSettings)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.npotScale = TextureImporterNPOTScale.None;   // critical: no rescaling
            importer.mipmapEnabled = false;
            importer.maxTextureSize = maxTextureSize;
            importer.alphaIsTransparency = alphaIsTransparency;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
        }
        else
        {
            importer.spriteImportMode = SpriteImportMode.Multiple;
        }

        string filePrefix = prefixWithFileName
            ? System.IO.Path.GetFileNameWithoutExtension(importer.assetPath) + "_"
            : "";

        var overrides = ParseOverrides(cellOverrides);

        int total = columns * rows;
        int limit = (maxCells <= 0) ? total : Mathf.Min(maxCells, total);

        var rects = new List<(string name, Rect rect)>(limit);
        int usableW = texW - offsetX;
        int usableH = texH - offsetY;

        for (int index = 0; index < limit; index++)
        {
            int col = index % columns;
            int row = index / columns;                    // row 0 = TOP row

            int x0 = offsetX + Mathf.RoundToInt(col * usableW / (float)columns);
            int x1 = offsetX + Mathf.RoundToInt((col + 1) * usableW / (float)columns);
            int t0 = offsetY + Mathf.RoundToInt(row * usableH / (float)rows);
            int t1 = offsetY + Mathf.RoundToInt((row + 1) * usableH / (float)rows);

            string cellName;
            if (useCellNames && index < cellNames.Length)
                cellName = cellNames[index].ToString();
            else
                cellName = index.ToString("000");

            // Apply an override, if this cell's name has one — BEFORE the bottom-left flip below,
            // while everything is still in simple top-origin pixel coordinates.
            if (overrides.TryGetValue(cellName, out var ov))
            {
                t0 -= ov.top;      // move the top edge further up (expand upward)
                t1 += ov.bottom;   // move the bottom edge further down (expand downward)
                x0 -= ov.left;     // move the left edge further left
                x1 += ov.right;    // move the right edge further right
            }

            int x = x0 + inset;
            int w = (x1 - x0) - inset * 2;
            int h = (t1 - t0) - inset * 2;
            int y = texH - t1 + inset;                    // flip to bottom-left origin

            if (w <= 0 || h <= 0) continue;

            rects.Add((filePrefix + cellName, new Rect(x, y, w, h)));
        }

        ApplyRects(importer, rects);
    }

    void ApplyRects(TextureImporter importer, List<(string name, Rect rect)> rects)
    {
#if UNITY_2021_2_OR_NEWER
        // Modern API (Sprite Editor Data Provider)
        var factories = new SpriteDataProviderFactories();
        factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        var spriteRects = new SpriteRect[rects.Count];
        var pairs = new List<SpriteNameFileIdPair>(rects.Count);
        for (int i = 0; i < rects.Count; i++)
        {
            var sr = new SpriteRect
            {
                name = rects[i].name,
                spriteID = GUID.Generate(),
                rect = rects[i].rect,
                alignment = SpriteAlignment.Custom,
                pivot = pivot,
                border = Vector4.zero
            };
            spriteRects[i] = sr;
            pairs.Add(new SpriteNameFileIdPair(sr.name, sr.spriteID));
        }

        provider.SetSpriteRects(spriteRects);
        var nameIdProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (nameIdProvider != null) nameIdProvider.SetNameFileIdPairs(pairs);
        provider.Apply();
#else
        var metas = new SpriteMetaData[rects.Count];
        for (int i = 0; i < rects.Count; i++)
        {
            metas[i] = new SpriteMetaData
            {
                name = rects[i].name,
                rect = rects[i].rect,
                alignment = (int)SpriteAlignment.Custom,
                pivot = pivot,
                border = Vector4.zero
            };
        }
        importer.spritesheet = metas;
#endif
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    /// <summary>Reads the ORIGINAL file resolution (not the clamped/imported one).</summary>
    public static bool TryGetSourceSize(TextureImporter importer, out int width, out int height)
    {
        width = height = 0;
        var mi = typeof(TextureImporter).GetMethod("GetWidthAndHeight",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi != null)
        {
            object[] args = { 0, 0 };
            mi.Invoke(importer, args);
            width = (int)args[0];
            height = (int)args[1];
            if (width > 0 && height > 0) return true;
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath);
        if (tex == null) return false;
        width = tex.width; height = tex.height;
        return width > 0 && height > 0;
    }
}
#endif