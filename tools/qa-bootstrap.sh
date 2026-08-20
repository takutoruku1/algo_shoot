#!/usr/bin/env bash
# QA 実行環境のブートストラップ＋起動ラッパ。
# クラウド/CI コンテナには Godot が無く、.godot/ も gitignore なので毎回ここから作り直す必要がある。
#
#   tools/qa-bootstrap.sh                       … Godot の用意と --import だけ済ませる
#   tools/qa-bootstrap.sh Rei --hard --seconds 45   … 用意した上で Rei.tscn を QA 起動
#
# 第1引数はシーン名（拡張子なし。res://<名前>.tscn になる）。以降は QaPilot へそのまま渡す。
# --qa と --quit は付けなくてよい（無ければ足す）。ログは build/qa/<シーン>.log。
set -euo pipefail

GODOT_VERSION="${GODOT_VERSION:-4.6.3}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CACHE="${GODOT_CACHE_DIR:-${TMPDIR:-/tmp}/godot_setup}"
DIRNAME="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64"
GODOT="$CACHE/$DIRNAME/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64"

# tuxfamily と builds.dotnet.microsoft.com はプロキシポリシーで 403。GitHub releases は通る。
URL="https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/${DIRNAME}.zip"

if [[ ! -x "$GODOT" ]]; then
  echo "[qa-bootstrap] Godot ${GODOT_VERSION} が無いので取得する: $URL"
  mkdir -p "$CACHE"
  tmp="$CACHE/$DIRNAME.zip"
  curl -fsSL --retry 3 -o "$tmp" "$URL"
  unzip -q -o "$tmp" -d "$CACHE"
  rm -f "$tmp"
  chmod +x "$GODOT"
fi
echo "[qa-bootstrap] godot = $GODOT"

# .godot/ が無い状態で QA 起動すると、インポートが走り切らずゲーム側の出力が一切出ないまま固まる。
if [[ ! -d "$ROOT/.godot/imported" ]]; then
  echo "[qa-bootstrap] 初回インポートを実行"
  "$GODOT" --headless --path "$ROOT" --import >/dev/null 2>&1 || true
fi

[[ $# -eq 0 ]] && { echo "[qa-bootstrap] 準備完了（シーン名を渡すと QA 起動する）"; exit 0; }

SCENE="$1"; shift
mkdir -p "$ROOT/build/qa"
LOG="$ROOT/build/qa/${SCENE}.log"

# Linux での正しい形：シーンパス → `--` 区切り → QaPilot 引数。区切りを抜くと Godot 側が食って無出力になる。
args=("$@")
[[ " ${args[*]} " == *" --qa "* ]]   || args=(--qa "${args[@]}")
[[ " ${args[*]} " == *" --quit "* ]] || args+=(--quit)

echo "[qa-bootstrap] run: res://${SCENE}.tscn -- ${args[*]}"
"$GODOT" --headless --path "$ROOT" "res://${SCENE}.tscn" -- "${args[@]}" > "$LOG" 2>&1 || true

echo "[qa-bootstrap] log = $LOG"
grep -E "QA-SUMMARY|QA-WARN|SCRIPT ERROR|NullReference" "$LOG" || echo "[qa-bootstrap] 所見の行は無し（ログ本体を確認すること）"
