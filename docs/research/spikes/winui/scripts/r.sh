#!/bin/bash
# usage: r.sh '<powershell command>'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Users\damia\AppData\Local\Temp\uas-sort-spike-winui\r.ps1' "$@" 2>&1 | tr -d '\r'
