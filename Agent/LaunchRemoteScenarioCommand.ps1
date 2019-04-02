param( $remoteip, $remoteport, $hostip, $hostuser, $domain, $scenario, $ochestratorip )

$downloadfile="${scenario}.zip"

$username="${domain}\${hostuser}"
$hostuser="${hostuser}"
$password=Get-Content "securestring_${hostuser}.txt" | ConvertTo-SecureString
$cred=new-object -typename System.Management.Automation.PSCredential -argumentlist $username, $password

Write-Host Launching with args: "${remoteip}", "${remoteport}", "${hostuser}", "${hostip}", "${hostport}", "${scenario}", "${ochestratorip}"

Set-Item WSMan:\localhost\Client\TrustedHosts -Force -Confirm:$false -Value "${hostip}"
Invoke-Command -ComputerName "${hostip}" -FilePath RemoteExecScenario.ps1 -ArgumentList ( "${remoteip}", "${remoteport}", "${hostuser}", "${hostip}", "${hostport}", "${scenario}", "${ochestratorip}" )  -Credential ${cred}