param( $remoteip, $remoteport, $hostip, $hostport, $hostuser, $domain, $downloadfile )

$username="${domain}\${hostuser}"
$password=Get-Content "securestring_${hostuser}.txt" | ConvertTo-SecureString
$cred=new-object -typename System.Management.Automation.PSCredential -argumentlist $username, $password

Set-Item WSMan:\localhost\Client\TrustedHosts -Force -Confirm:$false -Value "${hostip}"
Invoke-Command -ComputerName "${hostip}" -FilePath RemoteExecRavenDB.ps1 -ArgumentList ( "${remoteip}", "${remoteport}", "${hostuser}", "${downloadfile}", "${hostip}", "${hostport}" )  -Credential ${cred}