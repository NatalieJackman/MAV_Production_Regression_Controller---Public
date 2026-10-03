#!/usr/bin/env python3
"""Prepare MAV_SYSTEM_DEFINITION_UI local AI assets on an Internet-connected utility workstation.

This script can live beside index.html inside MAV_SYSTEM_DEFINITION_UI.zip, but the
MAV Deployment Utility normally points it at a shared AI root outside any one web app.
It is idempotent: existing valid files are reused and only missing/invalid assets are downloaded.

Default profile: all = runtime + Compatibility 135M + Balanced 360M + Quality 1.7B.
Use --profile standard only when you intentionally want to omit the Quality 1.7B WebGPU pack.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import urllib.request
from pathlib import Path
from typing import List, Optional

ROOT = Path(__file__).resolve().parent
AI_ROOT = ROOT / "ai"
VENDOR = AI_ROOT / "vendor"
MODELS_ROOT = AI_ROOT / "models"
MARKER = AI_ROOT / ".mav-ai-assets.json"

TRANSFORMERS_VERSION = "3.8.0"
USER_AGENT = "MAV-Assistant-AI-Prep/1.0"

COMMON_FILES = [
    "config.json",
    "generation_config.json",
    "merges.txt",
    "special_tokens_map.json",
    "tokenizer.json",
    "tokenizer_config.json",
    "vocab.json",
]

VENDOR_FILES = [
    (
        f"https://cdn.jsdelivr.net/npm/@huggingface/transformers@{TRANSFORMERS_VERSION}/dist/transformers.min.js",
        "transformers.min.js",
        None,
    ),
    (
        f"https://cdn.jsdelivr.net/npm/@huggingface/transformers@{TRANSFORMERS_VERSION}/dist/ort-wasm-simd-threaded.jsep.mjs",
        "ort-wasm-simd-threaded.jsep.js",
        None,
    ),
    (
        f"https://cdn.jsdelivr.net/npm/@huggingface/transformers@{TRANSFORMERS_VERSION}/dist/ort-wasm-simd-threaded.jsep.wasm",
        "ort-wasm-simd-threaded.jsep.wasm",
        None,
    ),
]

PACKS = {
    "compat": {
        "label": "Compatibility · SmolLM2-135M-Instruct",
        "repo": "onnx-community/SmolLM2-135M-Instruct-ONNX-MHA",
        "folder": "SmolLM2-135M-Instruct-ONNX-MHA",
        "weights": [
            (
                "onnx/model_quantized.onnx",
                "d5754584650dbbadc2b9d340e2881c51fb0f38c485b0387c04cbf5935a2d4041",
            )
        ],
    },
    "balanced": {
        "label": "Balanced · SmolLM2-360M-Instruct",
        "repo": "onnx-community/SmolLM2-360M-Instruct-ONNX",
        "folder": "SmolLM2-360M-Instruct-ONNX",
        "weights": [
            (
                "onnx/model_quantized.onnx",
                "5ee6c8dc6791050d8779687a16c8167643a234e00ea41e06a92e05f88003dbb3",
            )
        ],
    },
    "quality": {
        "label": "Quality · SmolLM2-1.7B-Instruct",
        "repo": "HuggingFaceTB/SmolLM2-1.7B-Instruct",
        "folder": "SmolLM2-1.7B-Instruct",
        "weights": [
            (
                "onnx/model_q4f16.onnx",
                "d94946187fb5f27579f3db4ba21fb7f7466c7cbd18956bd420d3981f75282f9c",
            )
        ],
    },
}


def display_path(path: Path) -> str:
    """Return a readable path even when the shared AI root lives outside the site root."""
    try:
        return str(path.relative_to(ROOT))
    except ValueError:
        return str(path)


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def download(url: str, destination: Path, expected_sha: Optional[str] = None) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)

    if destination.exists() and destination.stat().st_size > 0:
        if expected_sha:
            actual = sha256(destination)
            if actual.lower() == expected_sha.lower():
                print(f"  current: {display_path(destination)}")
                return
            print(f"  hash mismatch; replacing: {display_path(destination)}")
        else:
            print(f"  current: {display_path(destination)}")
            return

    temp = destination.with_suffix(destination.suffix + ".part")
    if temp.exists():
        temp.unlink()

    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=300) as response, temp.open("wb") as out:
        total = int(response.headers.get("Content-Length", "0") or 0)
        done = 0
        while True:
            chunk = response.read(1024 * 1024)
            if not chunk:
                break
            out.write(chunk)
            done += len(chunk)
            if total:
                pct = int(done * 100 / total)
                print(f"\r  {destination.name}: {pct:3d}%", end="", flush=True)
        if total:
            print()

    temp.replace(destination)

    if expected_sha:
        actual = sha256(destination)
        if actual.lower() != expected_sha.lower():
            destination.unlink(missing_ok=True)
            raise RuntimeError(
                f"SHA-256 mismatch for {destination.name}: expected {expected_sha}, got {actual}"
            )

    print(f"  saved:   {display_path(destination)} ({destination.stat().st_size:,} bytes)")


def stage_vendor() -> None:
    print("\nRuntime")
    for url, filename, expected_sha in VENDOR_FILES:
        download(url, VENDOR / filename, expected_sha)


def stage_pack(name: str) -> None:
    pack = PACKS[name]
    destination_root = MODELS_ROOT / pack["folder"]
    base = f"https://huggingface.co/{pack['repo']}/resolve/main/"

    print(f"\n{name.upper()}: {pack['label']}")
    for relative in COMMON_FILES:
        download(base + relative + "?download=true", destination_root / relative)
    for relative, expected_sha in pack["weights"]:
        download(base + relative + "?download=true", destination_root / relative, expected_sha)


def write_marker(profile: str, packs: List[str]) -> None:
    """Write a commit-style manifest for the fully prepared shared AI bundle.

    The deployment utility uploads this marker *last*.  On later deployments it can
    compare the remote marker and file sizes to the local bundle and skip the entire
    multi-gigabyte transfer when the processor already has the exact same assets.
    """
    AI_ROOT.mkdir(parents=True, exist_ok=True)

    assets = []
    for path in sorted(AI_ROOT.rglob("*"), key=lambda item: item.as_posix().lower()):
        if not path.is_file() or path == MARKER or path.name.endswith(".part"):
            continue
        relative = path.relative_to(AI_ROOT).as_posix()
        assets.append(
            {
                "path": relative,
                "bytes": path.stat().st_size,
                "sha256": sha256(path),
            }
        )

    identity = {
        "profile": profile,
        "transformersVersion": TRANSFORMERS_VERSION,
        "packs": packs,
        "assets": assets,
    }
    canonical = json.dumps(identity, sort_keys=True, separators=(",", ":")).encode("utf-8")
    payload = {
        "format": 2,
        **identity,
        "bundleSha256": hashlib.sha256(canonical).hexdigest(),
    }
    MARKER.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--target-root",
        default=None,
        help="Shared AI folder to populate. Defaults to ./ai beside this script for manual use.",
    )
    parser.add_argument(
        "--profile",
        choices=["standard", "all", "compat", "balanced", "quality"],
        default="all",
        help="all=135M+360M+1.7B (default); standard=135M+360M only",
    )
    args = parser.parse_args()

    global AI_ROOT, VENDOR, MODELS_ROOT, MARKER
    AI_ROOT = Path(args.target_root).expanduser().resolve() if args.target_root else (ROOT / "ai")
    VENDOR = AI_ROOT / "vendor"
    MODELS_ROOT = AI_ROOT / "models"
    MARKER = AI_ROOT / ".mav-ai-assets.json"

    if args.profile == "standard":
        packs = ["compat", "balanced"]
    elif args.profile == "all":
        packs = ["compat", "balanced", "quality"]
    else:
        packs = [args.profile]

    print("MAV Assistant AI asset preparation")
    print(f"Script root: {ROOT}")
    print(f"AI root:     {AI_ROOT}")
    print(f"Profile:     {args.profile}")
    print("Remote model loading remains disabled in the deployed site.\n")

    try:
        stage_vendor()
        for name in packs:
            stage_pack(name)
        write_marker(args.profile, packs)
    except KeyboardInterrupt:
        print("\nCancelled.")
        return 130
    except Exception as exc:
        print(f"\nERROR: {exc}")
        return 1

    print("\nMAV Assistant AI assets are ready.")
    print(f"Prepared folder: {AI_ROOT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
