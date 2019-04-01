param( $hostip, $hostuser, $domain )

$downloadfile="RavenDB-4.2.0-rc-42001-windows-x64.zip"
$remoteport="11800"

$hostport="11801"

$username="${domain}\${hostuser}"
$hostuser="${hostuser}"
$password=Get-Content 'securestring.txt' | ConvertTo-SecureString
$cred=new-object -typename System.Management.Automation.PSCredential -argumentlist $username, $password
$ipV4=Test-Connection -ComputerName (hostname) -Count 1  | Select IPV4Address
$ipV4=$ipV4.IPV4Address
$remoteip="${ipV4}"

Set-Item WSMan:\localhost\Client\TrustedHosts -Force -Confirm:$false -Value "${hostip}"
Invoke-Command -ComputerName "${hostip}" -FilePath RemoteExecRavenDB.ps1 -ArgumentList ( "${remoteip}", "${remoteport}", "${hostuser}", "${downloadfile}", "${hostip}", "${hostport}" )  -Credential ${cred}