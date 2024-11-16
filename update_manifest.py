#!/usr/bin/env python
# -*- coding: UTF-8 -*-

import datetime
import json
import os
import re
import shutil

from hashlib import md5

FILE_NAME_PATTERN = re.compile(r"Jellyfin.Plugin.Douban.(\d+).(\d+).(\d+).(\d+).zip")
CHANGE_LOG_ITEM_PATTERN = re.compile(
  r"^- (v\d+\.\d+\.\d+(?:、v\d+\.\d+\.\d+)*)（\d{4}-\d{2}-\d{2}）：((?:\n +- .+)+)", re.M
)


def get_change_log(basic_path: str):
  path = f"{basic_path}/../ChangeLog.md"
  change_log = {}
  if not os.path.exists(path):
    return change_log

  with open(path, "r", -1, "utf8") as reader:
    text = reader.read()

  items: list[tuple[str, str]] = CHANGE_LOG_ITEM_PATTERN.findall(text)
  for versions, content in items:
    for version in versions.split("、"):
      change_log[version] = content.replace("\n  ", "\n").removeprefix("\n")

  return change_log


def main(basic_path: str):
  with open(f"{basic_path}/manifest.json", "r", -1, "utf8") as reader:
    data = json.load(reader)

  change_log = get_change_log(basic_path)

  versions: list[dict[str, str]] = data[0]["versions"]

  for dotnet_version in [
    "net6.0",
    "net8.0",
  ]:
    path = f"{basic_path}/temp/build-{dotnet_version}"
    if not os.path.exists(path):
      continue
    for file_name in os.listdir(path):
      new_version = FILE_NAME_PATTERN.search(file_name)
      if not new_version:
        continue

      major, minor, patch, revision = new_version.groups()

      with open(f"{path}/{file_name}", "rb") as reader:
        md5sum = md5(reader.read()).hexdigest()

      versions.append(
        {
          "checksum": md5sum.upper(),
          "changelog": r"https://xzonn.top/posts/Jellyfin-Plugin-Douban.html#%E6%9B%B4%E6%96%B0%E6%97%A5%E5%BF%97",
          "targetAbi": f"10.{int(major) + 7}.0.0",
          "sourceUrl": f"https://xzonn.top/JellyfinPluginDouban/dist/{file_name}",
          "timestamp": (datetime.datetime.now(tz=datetime.timezone.utc) + datetime.timedelta(minutes=int(major) - 1))
          .replace(microsecond=0)
          .isoformat()
          .replace("+00:00", "Z"),
          "version": f"{major}.{minor}.{patch}.{revision}",
        }
      )

      shutil.move(f"{path}/{file_name}", f"{basic_path}/dist/{file_name}")

  for version in versions:
    major, minor, patch, revision = version["version"].split(".")
    key = f"v{major}.{minor}.{patch}"
    if key in change_log:
      version["changelog"] = change_log[key]

  versions.sort(key=lambda x: x["timestamp"], reverse=True)

  with open(f"{basic_path}/manifest.json", "w", -1, "utf8") as writer:
    json.dump(data, writer, indent=2, ensure_ascii=False)
    writer.write("\n")


if __name__ == "__main__":
  basic_path = os.path.dirname(os.path.abspath(__file__))
  main(basic_path)
