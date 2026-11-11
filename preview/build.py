"""Refreshes data.js for the preview page from the plugin's source: version, What's new entries and theme colors.
Run from anywhere:  python3 preview/build.py"""
import json, os, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src = lambda p: open(os.path.join(ROOT, p), encoding="utf-8").read()

version = re.search(r"<Version>([^<]+)</Version>", src("VelvetDairy/VelvetDairy.csproj")).group(1)

entries = []
for m in re.finditer(r'new\("([\d.]+)",\s*"((?:[^"\\]|\\.)*)",\s*new\[\]\s*\{([\s\S]*?)\}\)', src("VelvetDairy/Windows/ChangelogWindow.cs")):
    notes = [n.replace('\\"', '"') for n in re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(3))]
    entries.append({"version": m.group(1), "title": m.group(2).replace('\\"', '"'), "notes": notes})

colors = {}
for name, r, g, b, a in re.findall(r"public static readonly Vector4 (\w+) = new\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)", src("VelvetDairy/Windows/Ui.cs")):
    colors[name] = "rgba(%d,%d,%d,%s)" % (round(float(r) * 255), round(float(g) * 255), round(float(b) * 255), a)

discord = re.search(r'DiscordUrl = "([^"]+)"', src("VelvetDairy/Plugin.cs")).group(1)

data = {"version": version, "changes": entries, "colors": colors, "discord": discord}
with open(os.path.join(ROOT, "preview", "data.js"), "w", encoding="utf-8") as f:
    f.write("// Made by preview/build.py from the plugin's source. Don't edit by hand.\nwindow.PREVIEW = " + json.dumps(data, indent=1, ensure_ascii=False) + ";\n")
print("preview/data.js: version", version, "·", len(entries), "What's new entries ·", len(colors), "colors")
