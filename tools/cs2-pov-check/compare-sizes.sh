#!/usr/bin/env bash
# Compares the three GOTV test recordings and prints MB-per-minute for each.
#
# Run after the three 5-minute recordings in RESULTS.md half 1b:
#   ./compare-sizes.sh [minutes]      (default 5)
#
# ponytail: size is a proxy for how much got captured. It cannot tell you
# whether the extra bytes are ANGLES rather than padding — that is what
# watching the same flick in both demos is for. Upgrade path: a demo parser
# that diffs the angle series, once there is a detector to feed.

set -euo pipefail
MINUTES="${1:-5}"
CS="/c/Program Files (x86)/Steam/steamapps/common/Counter-Strike Global Offensive/game/csgo"
[ -d "$CS" ] || { echo "CS2 not found at: $CS" >&2; exit 1; }

printf '%-14s %14s %12s %10s\n' RUN BYTES MB MB/MIN
base=""
for run in a b c; do
  f="$CS/ns_gotv_${run}.dem"
  if [ ! -f "$f" ]; then
    printf '%-14s %14s\n' "run_$run" "(not recorded)"
    continue
  fi
  bytes=$(stat -c%s "$f")
  [ -n "$base" ] || base=$bytes
  awk -v r="run_$run" -v b="$bytes" -v m="$MINUTES" -v base="$base" 'BEGIN{
    printf "%-14s %14d %12.1f %10.2f", r, b, b/1048576, b/1048576/m
    if (base > 0 && b != base) printf "   %.2fx vs A", b/base
    printf "\n"
  }'
done

echo
echo "Extrapolated, against 024's measured mean of 233 MB for a real match:"
for run in a b c; do
  f="$CS/ns_gotv_${run}.dem"
  [ -f "$f" ] || continue
  bytes=$(stat -c%s "$f")
  awk -v r="run_$run" -v b="$bytes" -v m="$MINUTES" 'BEGIN{
    printf "  %s -> ~%.0f MB per 40-minute match\n", r, (b/1048576/m)*40
  }'
done
