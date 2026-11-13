#!/bin/bash
set +e
TARGET_USER='${TARGET_USER}'
NEW_PASSWORD='${NEW_PASSWORD}'
if [ -z "$TARGET_USER" ]; then TARGET_USER="root"; fi
if [ -z "$NEW_PASSWORD" ]; then echo "FAIL:1"; rm -f "$0"; exit 1; fi
printf '%s:%s\n' "$TARGET_USER" "$NEW_PASSWORD" | chpasswd 2>&1
RESULT=$?
if [ $RESULT -eq 0 ]; then echo "SUCCESS"; else echo "FAIL:$RESULT"; fi
rm -f "$0" 2>/dev/null
exit $RESULT