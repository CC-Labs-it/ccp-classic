#!/bin/sh
set -eu
classic_package_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$classic_package_dir/CCP.Classic.App" "$@"
