#!/usr/bin/env bash
# install.sh - bootstrap do ralph no Linux/macOS (baixa release + executa "ralph install")
# Uso:
#   curl -fsSL https://raw.githubusercontent.com/rodrigojager/ralph/main/install.sh | bash
#   RALPH_REPO=owner/repo RALPH_VERSION=v1.2.3 bash install.sh

set -euo pipefail

REPO="${RALPH_REPO:-rodrigojager/ralph}"
VERSION="${RALPH_VERSION:-latest}"
BIN_DIR="${RALPH_BIN_DIR:-$HOME/.local/bin}"

export RALPH_REPO="$REPO"

OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Linux)
    RID="linux-x64"
    ;;
  Darwin)
    case "$ARCH" in
      arm64) RID="osx-arm64" ;;
      *) RID="osx-x64" ;;
    esac
    ;;
  *)
    echo "Sistema não suportado: $OS" >&2
    exit 1
    ;;
esac

if [[ "$VERSION" == "latest" ]]; then
  API_URL="https://api.github.com/repos/${REPO}/releases/latest"
else
  API_URL="https://api.github.com/repos/${REPO}/releases/tags/${VERSION}"
fi

echo "Consultando release em $API_URL ..."
RELEASE_JSON="$(curl -fsSL -H "User-Agent: ralph-installer" "$API_URL")"

DOWNLOAD_URL="$(printf '%s' "$RELEASE_JSON" | grep "browser_download_url" | grep "ralph-${RID}\"" | head -1 | cut -d'"' -f4)"
LANG_URL="$(printf '%s' "$RELEASE_JSON" | grep "browser_download_url" | grep "ralph-lang.zip" | head -1 | cut -d'"' -f4 || true)"

if [[ -z "${DOWNLOAD_URL:-}" ]]; then
  echo "Asset ralph-${RID} não encontrado na release. Repositório: $REPO" >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
cleanup() {
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT

BOOTSTRAP_BIN="$TMP_DIR/ralph"
echo "Baixando ralph-${RID} ..."
curl -fsSL -H "User-Agent: ralph-installer" "$DOWNLOAD_URL" -o "$BOOTSTRAP_BIN"
chmod +x "$BOOTSTRAP_BIN"

if [[ -n "${LANG_URL:-}" ]]; then
  echo "Baixando pacote de idiomas ..."
  mkdir -p "$TMP_DIR/assets"
  curl -fsSL -H "User-Agent: ralph-installer" "$LANG_URL" -o "$TMP_DIR/assets/ralph-lang.zip"
  mkdir -p "$TMP_DIR/lang"
  if command -v unzip >/dev/null 2>&1; then
    unzip -oq "$TMP_DIR/assets/ralph-lang.zip" -d "$TMP_DIR/lang"
  else
    python3 - "$TMP_DIR/assets/ralph-lang.zip" "$TMP_DIR/lang" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as zf:
    zf.extractall(sys.argv[2])
PY
  fi
fi

echo "Executando instalador interativo: ralph install $BIN_DIR"
"$BOOTSTRAP_BIN" install "$BIN_DIR"
echo
echo "Instalação concluída. Verifique em um novo terminal: ralph --help"
