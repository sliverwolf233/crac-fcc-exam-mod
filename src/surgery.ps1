$ErrorActionPreference = 'Stop'
Add-Type -Path 'D:/Codes/MockExamProgram_AmateurRadio/_work/dl/dnlib-4.4.0/lib/netstandard2.0/dnlib.dll'

$src = 'D:/Codes/MockExamProgram_AmateurRadio/_extracted/Simulation.exe'
$patchDll = 'D:/Codes/MockExamProgram_AmateurRadio/_work/surgery/SimPatches.dll'
$out = 'D:/Codes/MockExamProgram_AmateurRadio/_work/surgery/Simulation_patched.exe'
$mod = [dnlib.DotNet.ModuleDefMD]::Load($src)
$patch = [dnlib.DotNet.ModuleDefMD]::Load($patchDll)

function Import-M($typeName, $methodName) {
  $t = $patch.Find($typeName, $false)
  foreach ($m in $t.Methods) { if ([string]$m.Name -eq $methodName) { return $mod.Import($m) } }
  throw ('missing ' + $typeName + '::' + $methodName)
}
$mIdVal  = Import-M 'SimPatches.IdCheck' 'Validate'
$mOnCS   = Import-M 'SimPatches.MultiUI' 'OnCheckedSimu'
$mOnCL   = Import-M 'SimPatches.MultiUI' 'OnCheckedLearn'
$mHookS  = Import-M 'SimPatches.MultiUI' 'HookShowSimu'
$mHookL  = Import-M 'SimPatches.MultiUI' 'HookShowLearn'
$mKind   = Import-M 'SimPatches.KindPatch' 'ConvertAndAlias'
$mLabels = Import-M 'SimPatches.Labels' 'FixHome'
$mBrand  = Import-M 'SimPatches.Branding' 'OnNavigate'
$mRevMis = Import-M 'SimPatches.ReviewFix' 'FixMistakeView'
$mRevAna = Import-M 'SimPatches.ReviewFix' 'FixAnalysisList'
$mJudge  = Import-M 'SimPatches.PatchCore' 'Judge'
Write-Output 'patch methods imported'

function Find-M($typeName, $methodName) {
  $t = $mod.Find($typeName, $false)
  foreach ($m in $t.Methods) { if ([string]$m.Name -eq $methodName) { return $m } }
  throw ('missing ' + $typeName + '::' + $methodName)
}
function NewBody($m, $argIndexes, $callRef) {
  $body = New-Object dnlib.DotNet.Emit.CilBody
  foreach ($ai in $argIndexes) {
    $body.Instructions.Add([dnlib.DotNet.Emit.Instruction]::Create([dnlib.DotNet.Emit.OpCodes]::Ldarg, $m.Parameters[$ai]))
  }
  $body.Instructions.Add([dnlib.DotNet.Emit.Instruction]::Create([dnlib.DotNet.Emit.OpCodes]::Call, $callRef))
  $body.Instructions.Add([dnlib.DotNet.Emit.Instruction]::Create([dnlib.DotNet.Emit.OpCodes]::Ret))
  $m.Body = $body
}

# 1) body replacements
NewBody (Find-M 'Simulation.IDNumberCheck' 'Validate') @(1, 2) $mIdVal
NewBody (Find-M 'WPFClient.App.Views.Simu' 'rbOption_Check') @(0, 1) $mOnCS
NewBody (Find-M 'WPFClient.App.Views.Learn' 'rbOption_Check') @(0, 1) $mOnCL
NewBody (Find-M 'Simulation.KindModel' 'ConvertKindInfo') @(0) $mKind
Write-Output 'bodies replaced'

# 2) prologue insertions
$hooks = @(
  @{ t = 'WPFClient.App.Views.Simu';  m = 'ShowQuestion'; call = $mHookS },
  @{ t = 'WPFClient.App.Views.Learn'; m = 'ShowQuestion'; call = $mHookL },
  @{ t = 'WPFClient.App.Views.Home';  m = 'Refresh';      call = $mLabels },
  @{ t = 'WPFClient.App.MainWindow';  m = 'Navigate';     call = $mBrand },
  @{ t = 'WPFClient.App.Views.Mistake'; m = 'ShowQuestion'; call = $mRevMis },
  @{ t = 'WPFClient.App.Views.SimuFinished'; m = 'InitAnalysis'; call = $mRevAna }
)
foreach ($h in $hooks) {
  $m = Find-M $h.t $h.m
  $ld = [dnlib.DotNet.Emit.Instruction]::Create([dnlib.DotNet.Emit.OpCodes]::Ldarg_0)
  $ci = [dnlib.DotNet.Emit.Instruction]::Create([dnlib.DotNet.Emit.OpCodes]::Call, $h.call)
  $m.Body.Instructions.Insert(0, $ci)
  $m.Body.Instructions.Insert(0, $ld)
}
Write-Output 'prologues inserted'

# 3) judge segment replacement (clone original instructions, swap compare for Judge call)
function Replace-Judge($typeName, $methodName) {
  $m = Find-M $typeName $methodName
  $instr = $m.Body.Instructions
  $eqIdx = -1
  for ($i = 0; $i -lt $instr.Count; $i++) {
    $opnd = ''
    if ($null -ne $instr[$i].Operand) { $opnd = $instr[$i].Operand.ToString() }
    if ($instr[$i].OpCode.Name -eq 'call' -and $opnd -like '*op_Equality*') {
      $hasIdx = $false
      for ($j = [Math]::Max(0, $i - 10); $j -lt $i; $j++) {
        $o2 = ''
        if ($null -ne $instr[$j].Operand) { $o2 = $instr[$j].Operand.ToString() }
        if ($instr[$j].OpCode.Name -eq 'callvirt' -and $o2 -like '*IndexOf*') { $hasIdx = $true }
      }
      if ($hasIdx) { $eqIdx = $i; break }
    }
  }
  if ($eqIdx -lt 0) { throw ('judge compare not found in ' + $methodName) }
  if ($instr[$eqIdx + 1].OpCode.Name -notlike 'brfalse*') { throw ('no brfalse after equality in ' + $methodName) }
  # the LEFT side of the equality is listQuestion[i].UserChoice — find its callvirt, then its expression-start ldsfld
  $ucIdx = -1
  for ($i = $eqIdx; $i -ge 0; $i--) {
    $o = ''
    if ($null -ne $instr[$i].Operand) { $o = $instr[$i].Operand.ToString() }
    if ($instr[$i].OpCode.Name -eq 'callvirt' -and $o -like '*get_UserChoice*') { $ucIdx = $i; break }
  }
  if ($ucIdx -lt 0) { throw ('left-side UserChoice not found in ' + $methodName) }
  $startIdx = -1
  for ($i = $ucIdx; $i -ge 0; $i--) {
    $o = ''
    if ($null -ne $instr[$i].Operand) { $o = $instr[$i].Operand.ToString() }
    if ($instr[$i].OpCode.Name -eq 'ldsfld' -and $o -like '*listQuestion*') { $startIdx = $i; break }
  }
  if ($startIdx -lt 0) { throw ('loop start not found in ' + $methodName) }
  # branch safety
  for ($si = 0; $si -lt $instr.Count; $si++) {
    if ($instr[$si].Operand -is [dnlib.DotNet.Emit.Instruction]) {
      $ti = $instr.IndexOf($instr[$si].Operand)
      if ($ti -ge $startIdx -and $ti -le $eqIdx -and $si -lt $startIdx) { throw ('external branch into judge range in ' + $methodName) }
    }
  }
  # find get_Item and index load within range
  $giIdx = -1
  for ($i = $startIdx; $i -le $eqIdx; $i++) {
    $o = ''
    if ($null -ne $instr[$i].Operand) { $o = $instr[$i].Operand.ToString() }
    if ($instr[$i].OpCode.Name -eq 'callvirt' -and $o -like '*get_Item*' -and $o -like '*QuestionModel*') { $giIdx = $i; break }
  }
  if ($giIdx -lt 0) { throw ('get_Item not found in ' + $methodName) }
  $idxLoad = $instr[$giIdx - 1].Clone()
  $getItemCall = $instr[$giIdx].Clone()
  $listField = $instr[$startIdx].Operand
  # capture instructions in range for later branch retarget
  $removed = @{}
  for ($i = $startIdx; $i -le $eqIdx; $i++) { $removed[$instr[$i]] = $true }
  # delete range
  $delCount = $eqIdx - $startIdx + 1
  for ($i = 0; $i -lt $delCount; $i++) { $instr.RemoveAt($startIdx) }
  # insert new sequence at startIdx (brfalse now sits there)
  $seq = @(
    [dnlib.DotNet.Emit.Instruction]::Create([dnlib.DotNet.Emit.OpCodes]::Ldsfld, $listField),
    $idxLoad,
    $getItemCall,
    [dnlib.DotNet.Emit.Instruction]::Create([dnlib.DotNet.Emit.OpCodes]::Call, $mJudge)
  )
  for ($i = $seq.Count - 1; $i -ge 0; $i--) { $instr.Insert($startIdx, $seq[$i]) }
  $newFirst = $instr[$startIdx]
  # retarget any branch/switch that pointed into the removed range
  foreach ($ins in $instr) {
    if ($ins.Operand -is [dnlib.DotNet.Emit.Instruction] -and $removed.ContainsKey($ins.Operand)) { $ins.Operand = $newFirst }
    elseif ($ins.Operand -is [dnlib.DotNet.Emit.Instruction[]]) {
      $arr = [dnlib.DotNet.Emit.Instruction[]]$ins.Operand
      for ($k = 0; $k -lt $arr.Length; $k++) { if ($removed.ContainsKey($arr[$k])) { $arr[$k] = $newFirst } }
    }
  }
  foreach ($eh in $m.Body.ExceptionHandlers) {
    foreach ($prop in @('TryStart', 'HandlerStart', 'FilterStart')) {
      $v = $eh.$prop
      if ($v -ne $null -and $removed.ContainsKey($v)) { $eh.$prop = $newFirst }
    }
  }
  Write-Output ('judge replaced in ' + $typeName + '::' + $methodName + ' (removed ' + $delCount + ', inserted ' + $seq.Count + ')')
}
Replace-Judge 'WPFClient.App.Views.Simu' 'Save'
Replace-Judge 'WPFClient.App.Views.Learn' 'SaveAndExit'

# publicize all members so SimPatches.dll (compiled against the publicized ref) can access them
foreach ($t in $mod.GetTypes()) {
  if ($t.IsNested) { $t.Attributes = ($t.Attributes -bor [dnlib.DotNet.TypeAttributes]::NestedPublic) -bxor [dnlib.DotNet.TypeAttributes]::NestedAssembly }
  else { $t.Attributes = ($t.Attributes -bor [dnlib.DotNet.TypeAttributes]::Public) -bxor [dnlib.DotNet.TypeAttributes]::NotPublic }
  foreach ($m in $t.Methods) { $m.Access = [dnlib.DotNet.MethodAttributes]::Public }
  foreach ($f in $t.Fields) { $f.Access = [dnlib.DotNet.FieldAttributes]::Public }
}

$mod.Write($out)
Write-Output ('patched exe written: ' + $out)

