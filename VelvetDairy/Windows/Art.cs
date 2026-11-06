using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace VelvetDairy.Windows;

/// <summary>
/// Loads the generated art from the plugin's Assets folder (made by tools/make_dairy_art.py).
/// Getters return null until an image is loaded, or if the file is missing, so callers fall back to plain shapes.
/// </summary>
public sealed class Art
{
    private readonly string dir;
    private readonly Dictionary<string, ISharedImmediateTexture?> cache = new();

    public Art()
    {
        dir = Path.Combine(Plugin.PluginInterface.AssemblyLocation.Directory?.FullName ?? "", "Assets");
    }

    public IDalamudTextureWrap? Get(string relative)
    {
        if (!cache.TryGetValue(relative, out var tex))
        {
            var path = Path.Combine(dir, relative);
            tex = File.Exists(path) ? Plugin.TextureProvider.GetFromFile(path) : null;
            if (tex == null) Plugin.Log.Warning($"Velvet Dairy art missing: {path}");
            cache[relative] = tex;
        }

        if (tex == null) return null;
        return tex.TryGetWrap(out var wrap, out _) ? wrap : null;
    }

    public IDalamudTextureWrap? Background => Get("bg.png");
    public IDalamudTextureWrap? Card(bool blue) => Get(blue ? "ui/card_blue.png" : "ui/card.png");
    public IDalamudTextureWrap? Button(string color) => Get($"ui/button_{color}.png");
    public IDalamudTextureWrap? Seal => Get("ui/seal.png");
    // Five bottle types (1 Quart, 2 Half gallon, 3 Gallon, 4 Big jug, 5 Milk churn), each in three layers
    public IDalamudTextureWrap? BottleBack(int tier) => Get($"ui/bottle{Math.Clamp(tier, 1, 5)}_back.png");
    public IDalamudTextureWrap? BottleMilk(int tier) => Get($"ui/bottle{Math.Clamp(tier, 1, 5)}_milk.png");
    public IDalamudTextureWrap? BottleFront(int tier) => Get($"ui/bottle{Math.Clamp(tier, 1, 5)}_front.png");
    public IDalamudTextureWrap? Icon(string name) => Get($"icons/{name}.png");
    public IDalamudTextureWrap? Avatar(string name) => Get($"avatars/{name}.png");

    /// <summary>
    /// Draws a texture stretched to a rectangle while keeping its corners and borders the same size (9-slice).
    /// texBorder is in texture pixels, screenBorder in screen pixels.
    /// </summary>
    public static void NineSlice(ImDrawListPtr dl, IDalamudTextureWrap tex, Vector2 min, Vector2 max,
        float texBorder, float screenBorder, uint tint = 0xFFFFFFFF)
    {
        var bu = texBorder / tex.Width;
        var bv = texBorder / tex.Height;
        var b = Math.Min(screenBorder, Math.Min((max.X - min.X) / 2, (max.Y - min.Y) / 2));
        float[] xs = { min.X, min.X + b, max.X - b, max.X };
        float[] ys = { min.Y, min.Y + b, max.Y - b, max.Y };
        float[] us = { 0, bu, 1 - bu, 1 };
        float[] vs = { 0, bv, 1 - bv, 1 };
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
        {
            if (xs[i + 1] <= xs[i] || ys[j + 1] <= ys[j]) continue;
            dl.AddImage(tex.Handle, new Vector2(xs[i], ys[j]), new Vector2(xs[i + 1], ys[j + 1]),
                new Vector2(us[i], vs[j]), new Vector2(us[i + 1], vs[j + 1]), tint);
        }
    }

    /// <summary>UVs that crop a texture to fill a rectangle without stretching it.</summary>
    public static (Vector2 Uv0, Vector2 Uv1) Cover(IDalamudTextureWrap tex, Vector2 size)
    {
        var texAspect = tex.Width / (float)tex.Height;
        var rectAspect = size.X / size.Y;
        if (rectAspect > texAspect)
        {
            var h = texAspect / rectAspect;
            return (new Vector2(0, (1 - h) / 2), new Vector2(1, (1 + h) / 2));
        }

        var w = rectAspect / texAspect;
        return (new Vector2((1 - w) / 2, 0), new Vector2((1 + w) / 2, 1));
    }
}
