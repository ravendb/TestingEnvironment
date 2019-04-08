param ( $remoteip, $hostip, $hostuser, $hostdomain, $orchestratorurl )

$scenarioport=11802

function LaunchScenario
{
  param ( $scenarioname, $port )

  date
  Write-Host "${scenario}"
  .\LaunchRemoteScenarioCommand.ps1 $remoteip $scenarioport $hostip $hostuser $hostdomain $scenario $orchestratorurl
}

$a=1
DO
{
  Write-Host "Starting Loop $a"
  
  $rnd = Get-Random -Minimum 1 -Maximum 100

  $p1=1
  $p2=20+$p1
  $p3=1+$p2
  $p4=5+$p3
  $p5=20+$p4
  $p6=10+$p5
  <# $p7 -> the rest : 43 #>

  If ( $rnd -eq $p1 ) {
	$scenario="AuthorizationBundle"
  } ElseIf ( $rnd -lt $p2 ) {
	$scenario="Counters"
  } ElseIf ( $rnd -lt $p3 ) {
	$scenario="BackupAndRestore"
  } ElseIf ( $rnd -lt $p4 ) {
	$scenario="CorruptedCasino"
  } ElseIf ( $rnd -lt $p5 ) {
	$scenario="MarineResearch"
  } ElseIf ( $rnd -lt $p6 ) {
	$scenario="Subscriptions"
  } Else {
	$scenario="BlogComment"
  }

 Write-Host "Launching ${scenario}"
 LaunchScenario "${scenario}" "${scenarioport}"
    
 $rnd = Get-Random -Minimum 30 -Maximum 900
 Write-Host "Sleep ${rnd} seconds"
 Start-Sleep -Seconds $rnd 
 
} While ($true)