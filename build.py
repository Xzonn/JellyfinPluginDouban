import os
import re
import zipfile

CSPROJ_PATH = "Jellyfin.Plugin.Douban/Jellyfin.Plugin.Douban.csproj"

dotnet_version = os.environ.get("XZ_DOTNET_VERSION", "net8.0")
publish = os.environ.get("XZ_PUBLISH", "false").lower() == "true"
github_run = int(os.environ.get("XZ_GITHUB_RUN", "0"))

latest_tag = os.popen("git describe --tags --abbrev=0").read().strip().removeprefix("v") or "0.0.0"
major, minor, patch = latest_tag.split(".", 2)

if dotnet_version == "net8.0":
  major = 3
elif dotnet_version == "net6.0":
  major = 1

if not publish:
  patch =  f"{patch.split('.')[0]}.{github_run}"
elif "." not in patch:
  patch = f"{patch}.0"

new_version = f"{major}.{minor}.{patch}"

with open(CSPROJ_PATH, "r", -1, "utf8") as reader:
  original_text = reader.read()

replaced_text = re.sub(
  r"<((?:Assembly|File)?Version)>[\d\.]+</\1>",
  rf"<\1>{new_version}</\1>",
  original_text,
)

with open(CSPROJ_PATH, "w", -1, "utf8") as writer:
  writer.write(replaced_text)

print(f"Building version {new_version} for .NET {dotnet_version}")
os.system(f"dotnet publish \"{CSPROJ_PATH}\" -c Release -o temp -f {dotnet_version}")

with open(CSPROJ_PATH, "w", -1, "utf8") as writer:
  writer.write(original_text)

zip_path = f"temp/Jellyfin.Plugin.Douban.{new_version}.zip"
if os.path.exists(zip_path):
  os.remove(zip_path)

with zipfile.ZipFile(zip_path, "w") as zipf:
  for file in [
    "Jellyfin.Plugin.Douban.dll",
    "Jellyfin.Plugin.Douban.pdb",
    "Jellyfin.Plugin.Douban.deps.json",
    "AnitomySharp.dll",
    "HtmlAgilityPack.dll",
    "HtmlAgilityPack.CssSelectors.dll",
  ]:
    if os.path.exists(f"temp/{file}"):
      zipf.write(f"temp/{file}", file)
