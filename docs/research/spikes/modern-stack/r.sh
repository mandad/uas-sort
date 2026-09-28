#!/bin/bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Users\damia\AppData\Local\Temp\uas-sort-spike-modernstack\r.ps1' "$@" 2>&1 | tr -d '\r'
