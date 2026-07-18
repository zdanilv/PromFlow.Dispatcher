#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 MAJOR.MINOR.PATCH [--gpg-key KEY_ID]" >&2
}

if [[ $# -lt 1 ]]; then
    usage
    exit 2
fi

VERSION="$1"
shift
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Version must have MAJOR.MINOR.PATCH format." >&2
    exit 2
fi

GPG_KEY=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --gpg-key)
            [[ $# -ge 2 ]] || { usage; exit 2; }
            GPG_KEY="$2"
            shift 2
            ;;
        *)
            usage
            exit 2
            ;;
    esac
done

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
SOLUTION="$REPO_ROOT/DesktopTemplate.slnx"
BOOT_PROJECT="$REPO_ROOT/Configurator.Boot/Configurator.Boot.csproj"
PUBLISH_DIR="$REPO_ROOT/artifacts/publish/$VERSION/linux-x64"
RELEASE_DIR="$REPO_ROOT/artifacts/release/$VERSION"
RPM_TOPDIR="$REPO_ROOT/artifacts/rpm/$VERSION"
SOURCE_NAME="promflow-dispatcher-$VERSION"
SOURCE_ROOT="$RPM_TOPDIR/SOURCES/$SOURCE_NAME"
SPEC_SOURCE="$SCRIPT_DIR/Linux/promflow-dispatcher.spec"
SPEC_PATH="$RPM_TOPDIR/SPECS/promflow-dispatcher.spec"

rm -rf -- "$PUBLISH_DIR" "$RPM_TOPDIR"
mkdir -p -- "$PUBLISH_DIR" "$RELEASE_DIR" "$RPM_TOPDIR/BUILD" "$RPM_TOPDIR/BUILDROOT" "$RPM_TOPDIR/RPMS" "$RPM_TOPDIR/SOURCES" "$RPM_TOPDIR/SPECS" "$RPM_TOPDIR/SRPMS"

cd -- "$REPO_ROOT"
dotnet restore "$SOLUTION"
dotnet build "$SOLUTION" -c Release --no-restore
dotnet test "$SOLUTION" -c Release --no-build --no-restore
dotnet restore "$BOOT_PROJECT" -r linux-x64
dotnet publish "$BOOT_PROJECT" -c Release -r linux-x64 --self-contained true --no-restore -p:PublishProfile=linux-x64 -p:Version="$VERSION" -o "$PUBLISH_DIR"

test -x "$PUBLISH_DIR/PromFlow.Dispatcher" || {
    echo "Published Linux launcher was not found or is not executable." >&2
    exit 1
}

mkdir -p -- "$SOURCE_ROOT/app"
cp -a -- "$PUBLISH_DIR/." "$SOURCE_ROOT/app/"
cp -- "$REPO_ROOT/LICENSE.txt" "$SOURCE_ROOT/LICENSE.txt"
cp -- "$SCRIPT_DIR/Linux/promflow-dispatcher.desktop" "$SOURCE_ROOT/promflow-dispatcher.desktop"
cp -- "$SCRIPT_DIR/Linux/promflow-dispatcher.png" "$SOURCE_ROOT/promflow-dispatcher.png"
tar -C "$RPM_TOPDIR/SOURCES" -czf "$RPM_TOPDIR/SOURCES/$SOURCE_NAME.tar.gz" "$SOURCE_NAME"
rm -rf -- "$SOURCE_ROOT"
cp -- "$SPEC_SOURCE" "$SPEC_PATH"

rpmbuild --define "_topdir $RPM_TOPDIR" --define "app_version $VERSION" -bb "$SPEC_PATH"

BUILT_RPM="$(find "$RPM_TOPDIR/RPMS" -type f -name "promflow-dispatcher-$VERSION-alt1.x86_64.rpm" -print -quit)"
if [[ -z "$BUILT_RPM" ]]; then
    echo "RPM was not created under $RPM_TOPDIR/RPMS." >&2
    exit 1
fi

FINAL_RPM="$RELEASE_DIR/promflow-dispatcher-$VERSION-alt1.x86_64.rpm"
cp -- "$BUILT_RPM" "$FINAL_RPM"

if [[ -n "$GPG_KEY" ]]; then
    if command -v rpmsign >/dev/null 2>&1; then
        rpmsign --define "_gpg_name $GPG_KEY" --addsign "$FINAL_RPM"
    else
        rpm --define "_gpg_name $GPG_KEY" --addsign "$FINAL_RPM"
    fi
    rpm --checksig "$FINAL_RPM"
else
    echo "WARNING: --gpg-key was not supplied; the RPM will be unsigned." >&2
fi

(
  cd "$RELEASE_DIR"
  sha256sum "$(basename "$FINAL_RPM")" > "$(basename "$FINAL_RPM").sha256"
)
echo "RPM:      $FINAL_RPM"
echo "SHA-256: $FINAL_RPM.sha256"
