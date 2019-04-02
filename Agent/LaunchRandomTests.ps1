param ( $remoteip, $hostip, $hostuser, $hostdomain, $ochestratorurl )

function LaunchScenario
{
  param ( $scenarioname, $port )

  date
  Write-Host "${scenario}"
  .\LaunchRemoteScenarioCommand.ps1 $remoteip $scenarioport $hostip $hostuser $hostdomain $scenario $ochestratorurl
}

$a=1
DO
{
  Write-Host "Starting Loop $a"
  
  $rnd = Get-Random -Minimum 1 -Maximum 30

  If ( $rnd -eq 2 ) {
	$scenario="AuthorizationBundle"
	$scenarioport=11804
  } ElseIf ( $rnd -lt 15 ) {
	$scenario="BlogComment"
	$scenarioport=11802
  } Else {
	$scenario="Counters"
	$scenarioport=11803
  }

 Write-Host "Launching ${scenario}"
 LaunchScenario "${scenario}" "${scenarioport}"
    
 $rnd = Get-Random -Minimum 30 -Maximum 900
 Write-Host "Sleep ${rnd} seconds"
 Start-Sleep -Seconds $rnd 
 
} While ($true)