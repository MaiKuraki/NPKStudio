#!/usr/bin/env python3
"""NPKStudio 发布打包。

版本号的唯一来源是 src/NPKStudio.Desktop/NPKStudio.Desktop.csproj 的 <Version>：
界面标题、exe 文件属性、zip 文件名全部来自它，不允许任何一处写死。

用法:
    python tools/pack.py            # 只打包
    python tools/pack.py --no-publish   # 复用上次 publish 的输出
"""

import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# 仓库根 = tools/ 的上一级。所有产物都留在仓库内，不依赖任何父目录。
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
CSPROJ = os.path.join(PROJECT_ROOT, "src", "NPKStudio.Desktop", "NPKStudio.Desktop.csproj")
DIST = os.path.join(PROJECT_ROOT, "dist")
FOLDER_NAME = "NPKStudio-win-x64"


def read_version() -> str:
    with open(CSPROJ, encoding="utf-8-sig") as f:
        match = re.search(r"<Version>([^<]+)</Version>", f.read())
    if not match:
        sys.exit("csproj 里找不到 <Version>，拒绝打包（否则版本号会再次漂移）")
    return match.group(1).strip()


def publish(target: str) -> None:
    cmd = [
        "dotnet", "publish",
        os.path.join(PROJECT_ROOT, "src", "NPKStudio.Desktop", "NPKStudio.Desktop.csproj"),
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-o", target,
    ]
    print("$", " ".join(cmd))
    subprocess.run(cmd, check=True, cwd=PROJECT_ROOT)


def stage(version: str, publish_dir: str) -> str:
    stage_root = os.path.join(tempfile.gettempdir(), f"npk-stage-{version}")
    shutil.rmtree(stage_root, ignore_errors=True)
    folder = os.path.join(stage_root, FOLDER_NAME)
    os.makedirs(os.path.join(folder, "示例"))

    exe = os.path.join(publish_dir, "NPKStudio.exe")
    if not os.path.isfile(exe):
        sys.exit(f"发布产物里没有 NPKStudio.exe（{publish_dir}），打包中止")

    shutil.copy2(exe, os.path.join(folder, "NPKStudio.exe"))
    shutil.copy2(os.path.join(PROJECT_ROOT, "docs", "使用说明.txt"),
                 os.path.join(folder, "使用说明.txt"))
    # 示例包用仓库自带的合成样本（纯几何图形，随 MIT 发布）；
    # 真实客户端 NPK 是第三方版权资产，不能进发布包。
    sample = os.path.join(PROJECT_ROOT, "tests", "samples", "synthetic_shapes.NPK")
    if os.path.isfile(sample):
        shutil.copy2(sample, os.path.join(folder, "示例", "synthetic_shapes.NPK"))
    return stage_root


def make_zip(stage_root: str, version: str) -> str:
    os.makedirs(DIST, exist_ok=True)
    out = os.path.join(DIST, f"NPKStudio-{version}-win-x64.zip")

    # 直接以 'w' 覆盖：不先删旧包。沙盒里 os.remove 会走回收站、可能失败，
    # 而 zipfile 的 'w' 本来就是截断写入，不需要先删。
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for directory, _, files in os.walk(os.path.join(stage_root, FOLDER_NAME)):
            for name in sorted(files):
                path = os.path.join(directory, name)
                arc = os.path.relpath(path, stage_root).replace(os.sep, "/")
                z.write(path, arc)

    with zipfile.ZipFile(out) as z:
        if z.testzip() is not None:
            sys.exit("zip CRC 校验失败")
    return out


def refresh_dist_folder(stage_root: str) -> None:
    """把新 exe 铺到 dist/NPKStudio-win-x64。程序正在运行时文件会锁住，跳过并提示。"""
    target = os.path.join(DIST, FOLDER_NAME)
    os.makedirs(os.path.join(target, "示例"), exist_ok=True)
    try:
        shutil.copy2(os.path.join(stage_root, FOLDER_NAME, "NPKStudio.exe"),
                     os.path.join(target, "NPKStudio.exe"))
        shutil.copy2(os.path.join(stage_root, FOLDER_NAME, "使用说明.txt"),
                     os.path.join(target, "使用说明.txt"))
        sample = os.path.join(stage_root, FOLDER_NAME, "示例", "synthetic_shapes.NPK")
        if os.path.isfile(sample):
            shutil.copy2(sample, os.path.join(target, "示例", "synthetic_shapes.NPK"))
        print(f"   已更新 {target}")
    except OSError as error:
        print(f"   ! 跳过 {target}：{error}")
        print("     （NPKStudio.exe 正在运行，关掉后重跑即可）")


def main() -> None:
    version = read_version()
    print(f"版本（来自 csproj）: {version}")

    publish_dir = os.path.join(tempfile.gettempdir(), "npk-publish-release")
    if "--no-publish" not in sys.argv:
        shutil.rmtree(publish_dir, ignore_errors=True)
        publish(publish_dir)
    elif not os.path.isfile(os.path.join(publish_dir, "NPKStudio.exe")):
        sys.exit("--no-publish 但上一次的发布产物不存在")

    stage_root = stage(version, publish_dir)
    archive = make_zip(stage_root, version)

    with open(archive, "rb") as f:
        digest = hashlib.sha256(f.read()).hexdigest()
    size = os.path.getsize(archive) / 1048576
    print(f"包: {archive}  ({size:.1f} MB)")
    print(f"SHA256: {digest}")

    refresh_dist_folder(stage_root)


if __name__ == "__main__":
    main()
