$ErrorActionPreference = 'Stop'
$f = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319'
$pl = 'D:/Codes/MockExamProgram_AmateurRadio/_work/payload'
$cs = 'D:/Codes/MockExamProgram_AmateurRadio/_work/surgery/Launcher.cs'
$out = 'D:/Codes/MockExamProgram_AmateurRadio/_work/surgery/Launcher.exe'
$argList = New-Object 'System.Collections.Generic.List[string]'
$argList.Add('/nologo')
$argList.Add('/target:winexe')
$argList.Add('/codepage:65001')
$argList.Add('/out:' + $out)
$argList.Add($cs)
foreach ($gz in Get-ChildItem $pl -Filter '*.gz') {
  $n = [System.IO.Path]::GetFileNameWithoutExtension($gz.Name)
  $argList.Add('/resource:' + $gz.FullName + ',payload.' + $n + '.gz')
}
$argList.Add('/r:System.Windows.Forms.dll')
$argList.Add('/r:System.Drawing.dll')
$argList.Add('/r:System.dll')
$argList.Add('/r:System.Core.dll')
& ($f + '/csc.exe') $argList.ToArray()
Write-Output ('rc=' + $LASTEXITCODE)
Get-Item $out -ErrorAction SilentlyContinue | Select-Object Length | Out-String
