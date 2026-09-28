param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Rest)
. 'C:\Users\damia\AppData\Local\Temp\uas-sort-spike-winui\env.ps1'
$cmd = $Rest -join ' '
Invoke-Expression $cmd
exit $LASTEXITCODE
