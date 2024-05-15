#!/usr/bin/env python
# -*- coding: UTF-8 -*-

import datetime
from hashlib import md5
import json, os, sys

os.chdir(os.path.dirname(__file__))

with open("manifest.json", "r", -1, "utf8") as reader:
  data = json.load(reader)

versions: list[dict[str, str]] = data[0]["versions"]
new_version = os.environ["XZ_VERSION"] or sys.argv[1]

if not new_version:
  raise ValueError("version is empty")

for version in versions:
  if version["version"] == f"{new_version}.0":
    raise ValueError("version is existed")

major, minor, patch = new_version.split(".")

with open(f"dist/Jellyfin.Plugin.Douban.{new_version}.0.zip", "rb") as reader:
  md5sum = md5(reader.read()).hexdigest()

versions.append({
  "checksum": md5sum.upper(),
  "changelog": f"See: https://github.com/Xzonn/JellyfinPluginDouban/tree/v{new_version}",
  "targetAbi": f"10.{int(major) + 7}.0.0",
  "sourceUrl": f"https://xzonn.top/JellyfinPluginDouban/dist/Jellyfin.Plugin.Douban.{new_version}.0.zip",
  "timestamp": datetime.datetime.now(tz=datetime.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
  "version": f"{new_version}.0",
})

with open("manifest.json", "w", -1, "utf8") as writer:
  json.dump(data, writer, indent=2, ensure_ascii=False)
  writer.write("\n")