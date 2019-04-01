param( $hostip, $hostuser, $domain, $scenario, $ochestratorip )

$downloadfile="${scenario}.zip"
$remoteport="11802"

$username="${domain}\${hostuser}"
$hostuser="${hostuser}"
$password=Get-Content 'securestring.txt' | ConvertTo-SecureString
$cred=new-object -typename System.Management.Automation.PSCredential -argumentlist $username, $password
$ipV4=Test-Connection -ComputerName (hostname) -Count 1  | Select IPV4Address
$ipV4=$ipV4.IPV4Address
$remoteip="${ipV4}"

Write-Host "Launching with args: ${remoteip}", "${hostuser}", "${hostip}", "${hostport}, $scenario, $ochestratorip"

Set-Item WSMan:\localhost\Client\TrustedHosts -Force -Confirm:$false -Value "${hostip}"
Invoke-Command -ComputerName "${hostip}" -FilePath RemoteExecScenario.ps1 -ArgumentList ( "${remoteip}", "${remoteport}", "${hostuser}", "${hostip}", "${hostport}", "${scenario}", "${ochestratorip}" )  -Credential ${cred}