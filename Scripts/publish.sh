#!/bin/bash
# Release build + distributable server folders, one per target, each packed into an archive:
#   publish/aaemu-<version>-<target>/{AAEmu.Login,AAEmu.Game,Tools,SQL}
#   publish/aaemu-<version>-<target>.tar.gz   (.zip on Windows)
#
# Usage: Scripts/publish.sh [target-triple ...]
#   no argument: the host target. Cross targets need their toolchain, e.g.
#   rustup target add x86_64-unknown-linux-musl && Scripts/publish.sh x86_64-unknown-linux-musl
# Extra xtask options can be given through PUBLISH_ARGS, e.g. PUBLISH_ARGS="--no-compact-db".
set -e
cd "$(dirname "$0")/../rust"

if [ $# -eq 0 ]; then
	cargo xtask publish --archive $PUBLISH_ARGS
else
	for target in "$@"; do
		cargo xtask publish --archive --target "$target" $PUBLISH_ARGS
	done
fi
