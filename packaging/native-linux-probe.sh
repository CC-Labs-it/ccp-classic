#!/usr/bin/env bash
set -euo pipefail

# This wrapper creates the only display and sound server the probe may use.
[[ "$(uname -s)" == Linux ]] || { echo 'Linux is required' >&2; exit 2; }
classic_probe_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "${1:-}" != --isolated ]]; then
  for classic_probe_tool in dotnet xvfb-run xauth picom pulseaudio pactl ffmpeg timeout; do
    command -v "$classic_probe_tool" >/dev/null || { echo "Missing probe dependency: $classic_probe_tool" >&2; exit 2; }
  done
  classic_probe_output="${1:-$classic_probe_root/artifacts/native-linux-probe}"
  mkdir -p -- "$classic_probe_output"
  classic_probe_output="$(cd -- "$classic_probe_output" && pwd)"
  export CCP_CLASSIC_XVFB_WRAPPER=1
  exec timeout --signal=TERM --kill-after=10s 180s xvfb-run --auto-servernum \
    --error-file="$classic_probe_output/xvfb.log" \
    --server-args='-screen 0 1024x768x24 -nolisten tcp' \
    bash "$classic_probe_root/packaging/native-linux-probe.sh" --isolated "$classic_probe_output"
fi

[[ "${CCP_CLASSIC_XVFB_WRAPPER:-}" == 1 && "${DISPLAY:-}" =~ ^:[0-9]+$
  && "${XAUTHORITY:-}" == */xvfb-run.*/Xauthority && -f "$XAUTHORITY" ]] \
  || { echo 'Private display unavailable' >&2; exit 2; }
classic_probe_output="$2"
classic_probe_tmp="$(mktemp -d "${TMPDIR:-/tmp}/ccp-classic-probe.XXXXXXXX")"
classic_probe_picom=''
classic_probe_pulse=''
classic_probe_cleanup() {
  trap - EXIT INT TERM
  for classic_probe_pid in "$classic_probe_picom" "$classic_probe_pulse"; do
    if [[ -n "$classic_probe_pid" ]]; then kill "$classic_probe_pid" 2>/dev/null || true; fi
  done
  for classic_probe_pid in "$classic_probe_picom" "$classic_probe_pulse"; do
    if [[ -n "$classic_probe_pid" ]]; then
      for ((classic_probe_attempt=0; classic_probe_attempt<20; classic_probe_attempt++)); do
        kill -0 "$classic_probe_pid" 2>/dev/null || break
        sleep .05
      done
      kill -KILL "$classic_probe_pid" 2>/dev/null || true
      wait "$classic_probe_pid" 2>/dev/null || true
    fi
  done
  [[ "$classic_probe_tmp" == /*/ccp-classic-probe.* && -d "$classic_probe_tmp" ]] && rm -rf -- "$classic_probe_tmp"
}
trap classic_probe_cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
unset WAYLAND_DISPLAY PULSE_SERVER DBUS_SESSION_BUS_ADDRESS
export XDG_SESSION_TYPE=x11 CCP_CLASSIC_NATIVE_PROBE=1 CCP_CLASSIC_PRIVATE_DISPLAY="$DISPLAY"
export XDG_RUNTIME_DIR="$classic_probe_tmp/runtime"
mkdir -m 700 -- "$XDG_RUNTIME_DIR"
export PULSE_SERVER="unix:$XDG_RUNTIME_DIR/pulse.sock"
export PULSE_SINK=classic_probe
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 AVALONIA_TELEMETRY_OPTOUT=1
classic_probe_project="$classic_probe_root/tools/CCP.Classic.Probe/CCP.Classic.Probe.csproj"
dotnet build "$classic_probe_project" -c Release -m:4 >"$classic_probe_output/build.log" 2>&1
classic_probe_dll="$classic_probe_root/tools/CCP.Classic.Probe/bin/Release/net10.0/CCP.Classic.Probe.dll"

# A separate process proves refusal before the private compositor starts.
dotnet "$classic_probe_dll" --no-compositor >"$classic_probe_output/no-compositor.jsonl" 2>"$classic_probe_output/no-compositor.log"
picom --backend xrender --config /dev/null >"$classic_probe_output/picom.log" 2>&1 &
classic_probe_picom=$!
cat >"$classic_probe_tmp/pulse.pa" <<EOF
load-module module-native-protocol-unix socket=$XDG_RUNTIME_DIR/pulse.sock auth-anonymous=1
load-module module-null-sink sink_name=classic_probe rate=22050 channels=1
set-default-sink classic_probe
EOF
pulseaudio --daemonize=no --exit-idle-time=-1 --disallow-exit --disable-shm --use-pid-file=no \
  -n --file="$classic_probe_tmp/pulse.pa" >"$classic_probe_output/pulse.log" 2>&1 &
classic_probe_pulse=$!
classic_probe_ready=0
for ((classic_probe_attempt=0; classic_probe_attempt<100; classic_probe_attempt++)); do
  kill -0 "$classic_probe_pulse" "$classic_probe_picom" 2>/dev/null || { echo 'Private probe service failed' >&2; exit 1; }
  if pactl info >/dev/null 2>&1; then classic_probe_ready=1; break; fi
  sleep .05
done
[[ "$classic_probe_ready" == 1 ]] || { echo 'Private sound server timed out' >&2; exit 1; }
ffmpeg -nostdin -v error -f lavfi -i 'color=c=0x3c83d9:s=320x180:r=15:d=2' \
  -an -c:v mpeg4 -threads 2 -pix_fmt yuv420p "$classic_probe_tmp/neutral.mp4"
ffmpeg -nostdin -v error -f lavfi -i 'anullsrc=r=22050:cl=mono' -t 2 \
  -c:a pcm_s16le -threads 2 "$classic_probe_tmp/silent.wav"
dotnet "$classic_probe_dll" --media "$classic_probe_tmp" >"$classic_probe_output/native.jsonl" 2>"$classic_probe_output/native.log"
cat -- "$classic_probe_output/no-compositor.jsonl" "$classic_probe_output/native.jsonl"
